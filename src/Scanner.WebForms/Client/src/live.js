/*
  ================================================================================================
  RMS MODEL SCANNER - THE REFRESHING REGIONS
  ================================================================================================

  A region is an element with a data-url. Every so often this module fetches that address and
  replaces the element's contents with the answer. The answer is markup -- the same markup the page
  was served with, built by the same C# -- so a table cannot come out one shape when the page loads
  and a different shape when it updates.

  Three things it is careful about.

  The first is that it does not ask while nobody is looking. A tab in the background does not need a
  fresh table, and a page left open overnight should not have spent the night asking an API for a
  catalogue that had not changed.

  The second is that it abandons a request that has taken too long rather than queueing the next one
  behind it. A slow API would otherwise build a queue of doomed requests and the page would go on
  loading things long after the reader had navigated away.

  The third is that a failed refresh is shown inside the region it was refreshing, and not as a page
  that has quietly stopped updating. A table that is five minutes stale with nothing to say so is
  worse than one that says it could not read the API.

  The address comes off the element and is never built here. This script has no idea what the API is
  called or where it lives: it asks this site, at an address this site wrote into the page.
*/

var REQUEST_TIMEOUT_MS = 30000;
var DEFAULT_REFRESH_SECONDS = 15;

// Handlers run after a region's contents have been replaced. Sorting has to be re-applied and a
// dismissable alert inside the new markup has to be wired up again, and neither of those things can
// be done by the region itself because it has just been thrown away and rebuilt.
var listeners = [];

// The identifiers of the regions currently being refreshed. Used to keep a slow region from having a
// second request started on top of it by the next tick of the clock.
var busy = Object.create(null);

/**
 * Registers a handler to run after any region has been refreshed.
 *
 * @param {function(Element): void} handler the handler, given the region that changed.
 */
export function onRefresh(handler) {
    if (typeof handler === 'function') listeners.push(handler);
}

/**
 * Reads how often a page wants its regions refreshed.
 *
 * The interval is written into the document by the site itself rather than chosen here, so a
 * deployment can slow every page down from its own configuration without this file being edited.
 *
 * @returns {number} the interval, in milliseconds.
 */
function refreshInterval() {
    var meta = document.querySelector('meta[name="rms-refresh"]');

    if (!meta) {
        return DEFAULT_REFRESH_SECONDS * 1000;
    }

    var seconds = parseInt(meta.getAttribute('content'), 10);

    if (!isFinite(seconds) || seconds < 2) {
        return DEFAULT_REFRESH_SECONDS * 1000;
    }

    return seconds * 1000;
}

/**
 * Fetches text, giving up after a while.
 *
 * @param {string} url the address to read.
 * @param {function(number, string): void} done called with the status and the body. The body is empty
 *        for a status that carries none.
 */
function fetchText(url, done) {
    var controller = typeof AbortController === 'function' ? new AbortController() : null;
    var timer = window.setTimeout(function () {
        if (controller) {
            controller.abort();
        }
    }, REQUEST_TIMEOUT_MS);

    var options = {
        method: 'GET',
        credentials: 'same-origin',
        headers: { 'X-Requested-With': 'fetch' },
        cache: 'no-store'
    };

    if (controller) {
        options.signal = controller.signal;
    }

    window.fetch(url, options).then(function (response) {
        return response.text().then(function (body) {
            return { status: response.status, body: body };
        });
    }).then(function (result) {
        window.clearTimeout(timer);
        done(result.status, result.body);
    }).catch(function () {
        // An aborted request and a refused connection are the same thing here: the region did not get
        // an answer. The distinct cases are the reader's to notice, not the page's to guess at.
        window.clearTimeout(timer);
        done(0, '');
    });
}

/**
 * Replaces a region's contents and tells everything that cares.
 *
 * @param {Element} region the region.
 * @param {string} markup the replacement contents, which were built by this site.
 */
function replace(region, markup) {
    // Assigning markup is safe here and is the only place in this file that builds markup at all. The
    // value is not data: it is a document this site's own handler produced, and the same handler that
    // produced the one already on the page. Nothing that came from the catalogue is assembled here --
    // it arrives already encoded by the renderer.
    region.innerHTML = markup;

    for (var index = 0; index < listeners.length; index++) {
        try {
            listeners[index](region);
        } catch (error) {
            // A handler that throws must not stop the others, and must not stop the region from having
            // been refreshed. There is nothing useful to say about it on a page, so it is left alone.
        }
    }
}

/**
 * Says, inside a region, that it could not be refreshed.
 *
 * @param {Element} region the region.
 * @param {string} message the sentence to show.
 */
function complain(region, message) {
    var note = document.createElement('p');

    note.className = 'alert alert-warning refresh-note';
    note.setAttribute('role', 'alert');
    note.textContent = message;

    // Only if the region has gone stale in place. A region that already holds a warning from the last
    // attempt is not made to hold two.
    var existing = region.querySelector('.refresh-note');

    if (existing) {
        existing.textContent = message;
        return;
    }

    region.insertBefore(note, region.firstChild);
}

/**
 * Refreshes one region.
 *
 * @param {Element} region the region.
 */
function refresh(region) {
    var url = region.getAttribute('data-url');

    if (!url) {
        return;
    }

    var key = url;

    if (busy[key]) {
        return;
    }

    busy[key] = true;
    region.setAttribute('data-busy', '1');

    fetchText(url, function (status, body) {
        delete busy[key];
        region.removeAttribute('data-busy');

        if (status === 200) {
            replace(region, body);
            return;
        }

        if (status === 401) {
            // The session ended while the page was open. The only useful thing to do is to go and sign
            // in again, and to say why, so that a reader who was looking at a table when it happened is
            // not left wondering what they did.
            window.location.href = '/Account/Login.aspx?reason=' + encodeURIComponent('Your session ended.');
            return;
        }

        if (status === 0) {
            complain(region, 'This could not be refreshed. The site did not answer.');
            return;
        }

        complain(region, 'This could not be refreshed. The site answered ' + status + '.');
    });
}

/**
 * Starts refreshing every region on the page.
 *
 * @returns {void}
 */
export function startLiveRegions() {
    var regions = Array.prototype.slice.call(document.querySelectorAll('.rms-live[data-url]'));

    if (regions.length === 0) {
        return;
    }

    var interval = refreshInterval();

    window.setInterval(function () {
        // A page nobody is looking at is a page that does not need to be current, and a browser that has
        // been left open on a wall display should not be asking all night.
        if (document.hidden) {
            return;
        }

        for (var index = 0; index < regions.length; index++) {
            refresh(regions[index]);
        }
    }, interval);
}
