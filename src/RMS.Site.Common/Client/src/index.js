/*
  ================================================================================================
  RMS CONSOLES - THE PAGE SCRIPT
  ================================================================================================

  This is the whole client side of the three consoles that share it -- the DevOps console, the
  Configuration console and the documentation site: no framework, and nothing here that a browser of
  the age those consoles are read from cannot run as it stands. It is loaded on every page of each of
  them and does three things.

    1. It refreshes the regions that are marked as live, by fetching the markup for them and
       replacing what is there. The markup is built by the same C# that rendered the page, so a
       table cannot come out one shape when the page loads and a different shape when it updates.

    2. It follows a log while the run it belongs to is still going, asking for what has been
       written since the last answer rather than for the file again.

    3. It sorts a table when its header is clicked.

  Three rules run through all of it.

  The first is that nothing here builds markup from data. Anything that came from a service -- a
  service name, a log line, a cell's contents -- is written as text, with textContent or a text node,
  so a value containing angle brackets is displayed as the characters it is made of and cannot become
  part of the document. There is one innerHTML assignment in the file, in the live-region refresh, and
  what it takes is not data: it is the markup the same C# that rendered the page produced for that
  region, which is the whole reason a refreshed table cannot come out a different shape from the one
  the page loaded with.

  The second is that a failure never leaves the page looking as though it succeeded. A region that
  could not be refreshed keeps what it had and says so, rather than being emptied.

  The third is that nothing polls when the page is not being looked at. A console left open on a
  second monitor is the ordinary case, and it should not cost a request every fifteen seconds for
  a page nobody is reading.

  WHAT THE BROWSER GETS IS NOT THIS FILE
    The webpack build beside this one writes Scripts/site.js into each of the three site projects,
    and what it writes keeps no comments and no original names: it is a file to be executed, not a
    file to be read. This one is a file to be read, which is why it sits outside all three web
    projects -- a project does not publish anything it does not list, so nothing here is ever served.
*/
(function () {
  'use strict';

  /* How long the answer to one refresh may take before it is abandoned, in milliseconds. Longer
     than any call the site itself makes, because this one goes through the site's own handler. */
  var REQUEST_TIMEOUT_MS = 30000;

  /* How much of a followed log is kept in the document, in characters. A run that has been going for
     a day would otherwise grow the document until the browser stops responding, and the point of
     following a log is to watch the end of it, not to keep all of it. */
  var LOG_KEEP_CHARS = 200000;

  /* How often the log is polled while it is being followed, in milliseconds. Faster than a region,
     because the log is what somebody is watching. */
  var LOG_INTERVAL_MS = 2000;

  var regions = [];
  var logs = [];

  function readRefreshSeconds() {
    var meta = document.querySelector('meta[name="rms-refresh"]');

    if (!meta) {
      return 15;
    }

    var seconds = parseInt(meta.getAttribute('content'), 10);

    if (isNaN(seconds) || seconds < 2) {
      return 15;
    }

    return seconds > 3600 ? 3600 : seconds;
  }

  /* ---------------------------------------------------------------------------------------------
     Fetching
     --------------------------------------------------------------------------------------------- */

  /*
    Fetches text, with a deadline of its own. AbortController is not present on every browser this
    console is read from, and its absence is not an error: the request is then made without a
    deadline, which is worse but still works.
  */
  function fetchText(url, onDone) {
    var controller = null;
    var settled = false;

    function finish(result) {
      if (settled) {
        return;
      }

      settled = true;

      onDone(result);
    }

    var options = {
      credentials: 'same-origin',
      headers: { 'Accept': 'text/html, application/json' },
      cache: 'no-store'
    };

    if (typeof AbortController === 'function') {
      controller = new AbortController();
      options.signal = controller.signal;

      window.setTimeout(function () {
        finish({ ok: false, status: 0, text: '', timedOut: true });
        controller.abort();
      }, REQUEST_TIMEOUT_MS);
    }

    fetch(url, options).then(function (response) {
      return response.text().then(function (text) {
        finish({ ok: response.ok, status: response.status, text: text, timedOut: false });
      });
    }).catch(function () {
      finish({ ok: false, status: 0, text: '', timedOut: false });
    });
  }

  /* ---------------------------------------------------------------------------------------------
     Live regions
     --------------------------------------------------------------------------------------------- */

  /*
    Replaces a region's contents with what the site answers for it.

    The answer is markup and it is trusted as markup, which is only acceptable because it is built by
    this application from data the application has already encoded. It is never built from a value a
    browser sent.
  */
  function refreshRegion(region) {
    if (region.getAttribute('data-busy') === '1') {
      return;
    }

    region.setAttribute('data-busy', '1');

    fetchText(region.getAttribute('data-url'), function (result) {
      region.removeAttribute('data-busy');

      if (result.status === 401) {
        // The session ended while the page was open. There is nothing useful to say inside the
        // region, because every other region and every link would fail the same way.
        window.location.href = '/Account/Login.aspx?reason=' + encodeURIComponent('Your session ended.');
        return;
      }

      if (!result.ok) {
        markStale(region);
        return;
      }

      region.innerHTML = result.text;
      region.removeAttribute('data-stale');

      applyStoredSort(region);
    });
  }

  /*
    Marks a region as not having been refreshed. The contents are left alone: showing the last thing
    that was true, with a note saying when, is more useful than an empty box.
  */
  function markStale(region) {
    region.setAttribute('data-stale', '1');
  }

  function startRegions() {
    regions = Array.prototype.slice.call(document.querySelectorAll('.rms-live[data-url]'));

    if (regions.length === 0) {
      return;
    }

    var interval = readRefreshSeconds() * 1000;

    window.setInterval(function () {
      if (document.hidden) {
        return;
      }

      for (var index = 0; index < regions.length; index++) {
        if (!regions[index].isConnected) {
          // A region can be replaced by its own refresh on a page that swaps whole panels. Throwing
          // it away here is cheaper than asking for something no longer on the page.
          regions.splice(index, 1);
          index--;
          continue;
        }

        refreshRegion(regions[index]);
      }
    }, interval);
  }

  /* ---------------------------------------------------------------------------------------------
     Following a log
     --------------------------------------------------------------------------------------------- */

  function appendLogText(pre, text) {
    pre.appendChild(document.createTextNode(text));

    var excess = pre.textContent.length - LOG_KEEP_CHARS;

    if (excess > 0) {
      // Whole nodes are dropped from the front until the overrun is covered. Dropping a node
      // entirely rather than cutting one in half keeps what is left a log and not a fragment.
      while (excess > 0 && pre.firstChild) {
        excess -= pre.firstChild.textContent.length;
        pre.removeChild(pre.firstChild);
      }
    }
  }

  function followLog(pre) {
    if (pre.getAttribute('data-busy') === '1') {
      return;
    }

    pre.setAttribute('data-busy', '1');

    var url = pre.getAttribute('data-log-url') + '&offset=' + encodeURIComponent(pre.getAttribute('data-offset'));

    fetchText(url, function (result) {
      pre.removeAttribute('data-busy');

      if (result.status === 401) {
        window.location.href = '/Account/Login.aspx?reason=' + encodeURIComponent('Your session ended.');
        return;
      }

      if (!result.ok) {
        stopFollowing(pre, 'The log could not be read.');
        return;
      }

      var slice;

      try {
        slice = JSON.parse(result.text);
      } catch (error) {
        stopFollowing(pre, 'The log could not be read.');
        return;
      }

      if (!slice || typeof slice.text !== 'string') {
        stopFollowing(pre, 'The log could not be read.');
        return;
      }

      if (slice.text.length > 0) {
        var atBottom = pre.scrollTop + pre.clientHeight >= pre.scrollHeight - 8;

        appendLogText(pre, slice.text);

        // The view is only dragged down when the reader was already at the end. Somebody who has
        // scrolled back to read an earlier line is not pulled away from it by an arriving line.
        if (atBottom) {
          pre.scrollTop = pre.scrollHeight;
        }
      }

      pre.setAttribute('data-offset', String(slice.offset + slice.length));

      if (slice.complete) {
        stopFollowing(pre, null);
      }
    });
  }

  function stopFollowing(pre, message) {
    pre.removeAttribute('data-live');

    if (message) {
      var note = document.createElement('p');
      note.className = 'text-muted log-note';
      note.textContent = message;

      if (pre.parentNode) {
        pre.parentNode.insertBefore(note, pre.nextSibling);
      }
    }
  }

  function startLogs() {
    logs = Array.prototype.slice.call(document.querySelectorAll('pre.log-view[data-live="true"][data-log-url]'));

    if (logs.length === 0) {
      return;
    }

    window.setInterval(function () {
      if (document.hidden) {
        return;
      }

      for (var index = 0; index < logs.length; index++) {
        var pre = logs[index];

        if (!pre.isConnected || pre.getAttribute('data-live') !== 'true') {
          logs.splice(index, 1);
          index--;
          continue;
        }

        followLog(pre);
      }
    }, LOG_INTERVAL_MS);
  }

  /* ---------------------------------------------------------------------------------------------
     Sorting
     --------------------------------------------------------------------------------------------- */

  /*
    The value a cell sorts by.

    A time column shows a phrase ("4 minutes ago") and carries the absolute moment in a title
    attribute, so sorting on the phrase would sort alphabetically and be wrong. The title is
    therefore preferred wherever there is one, and everything else falls back to the text.
  */
  function cellKey(row, index) {
    var cell = row.cells[index];

    if (!cell) {
      return '';
    }

    var explicit = cell.getAttribute('data-sort');

    if (explicit !== null) {
      return explicit;
    }

    var titled = cell.querySelector('[title]');

    if (titled) {
      return titled.getAttribute('title');
    }

    return cell.textContent.trim();
  }

  /* A number, when the value is one: an optional sign, digits, grouping commas and a decimal part. */
  function asNumber(value) {
    if (!/^-?[\d,]+(\.\d+)?$/.test(value)) {
      return null;
    }

    var parsed = parseFloat(value.replace(/,/g, ''));

    return isNaN(parsed) ? null : parsed;
  }

  function compare(left, right) {
    var leftNumber = asNumber(left);
    var rightNumber = asNumber(right);

    if (leftNumber !== null && rightNumber !== null) {
      return leftNumber - rightNumber;
    }

    return left.localeCompare(right, undefined, { numeric: true, sensitivity: 'base' });
  }

  function sortTable(region, table, index, direction) {
    var head = table.tHead;

    if (!head || !head.rows.length) {
      return;
    }

    var body = table.tBodies[0];

    if (!body) {
      return;
    }

    var rows = Array.prototype.slice.call(body.rows).filter(function (row) {
      // A row that spans the table is the empty-state row, and it belongs last whatever the order.
      return row.cells.length > 1;
    });

    var spanning = Array.prototype.slice.call(body.rows).filter(function (row) {
      return row.cells.length === 1;
    });

    rows.sort(function (left, right) {
      var result = compare(cellKey(left, index), cellKey(right, index));

      return direction === 'desc' ? -result : result;
    });

    // appendChild on a row already in the table moves it, which is what makes this reorder rather
    // than copy. The spanning rows are re-appended afterwards so they stay at the bottom.
    for (var position = 0; position < rows.length; position++) {
      body.appendChild(rows[position]);
    }

    for (var extra = 0; extra < spanning.length; extra++) {
      body.appendChild(spanning[extra]);
    }

    var headers = head.rows[0].cells;

    for (var cell = 0; cell < headers.length; cell++) {
      headers[cell].classList.remove('sorted-asc');
      headers[cell].classList.remove('sorted-desc');
    }

    if (headers[index]) {
      headers[index].classList.add(direction === 'desc' ? 'sorted-desc' : 'sorted-asc');
    }

    if (region) {
      region.setAttribute('data-sort-col', String(index));
      region.setAttribute('data-sort-dir', direction);
    }
  }

  /*
    Wires the headers of every sortable table inside a container.

    Called once for the page and again after each region is replaced, because the table that arrives
    is a new element and carries none of the listeners the old one had.
  */
  function applySorting(container, region) {
    var tables = container.querySelectorAll('table[data-sortable="true"]');

    for (var index = 0; index < tables.length; index++) {
      var table = tables[index];

      if (table.getAttribute('data-sort-wired') === '1') {
        continue;
      }

      table.setAttribute('data-sort-wired', '1');

      wireTable(table, region);
    }
  }

  function wireTable(table, region) {
    var head = table.tHead;

    if (!head || !head.rows.length) {
      return;
    }

    var headers = head.rows[0].cells;

    for (var index = 0; index < headers.length; index++) {
      /* eslint-disable no-loop-func */
      (function (column, header) {
        var span = header.querySelector('.sortable-head');

        if (!span) {
          return;
        }

        header.classList.add('sortable-header');

        header.addEventListener('click', function () {
          var direction = 'asc';

          if (header.classList.contains('sorted-asc')) {
            direction = 'desc';
          } else if (header.classList.contains('sorted-desc')) {
            direction = 'asc';
          }

          sortTable(region, table, column, direction);
        });
      }(index, headers[index]));
      /* eslint-enable no-loop-func */
    }
  }

  /*
    Re-applies the order a region was left in, to the table that has just arrived in it.
  */
  function applyStoredSort(region) {
    var column = region.getAttribute('data-sort-col');

    if (column === null) {
      applySorting(region, region);
      return;
    }

    var table = region.querySelector('table[data-sortable="true"]');

    if (!table) {
      applySorting(region, region);
      return;
    }

    applySorting(region, region);
    sortTable(region, table, parseInt(column, 10), region.getAttribute('data-sort-dir') || 'asc');
  }

  /* ---------------------------------------------------------------------------------------------
     Small behaviours
     --------------------------------------------------------------------------------------------- */

  /*
    Dismisses an alert. The theme ships this as a Bootstrap plugin, and loading a whole script
    library to hide one element is not a trade worth making.
  */
  function startDismissals() {
    document.addEventListener('click', function (event) {
      var button = event.target;

      while (button && button !== document) {
        if (button.getAttribute && button.getAttribute('data-dismiss') === 'alert') {
          var alert = button.closest ? button.closest('.alert') : null;

          if (alert && alert.parentNode) {
            alert.parentNode.removeChild(alert);
          }

          event.preventDefault();
          return;
        }

        button = button.parentNode;
      }
    });
  }

  /* ---------------------------------------------------------------------------------------------
     Start-up
     --------------------------------------------------------------------------------------------- */

  function start() {
    applySorting(document, null);
    startRegions();
    startLogs();
    startDismissals();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', start);
  } else {
    start();
  }
}());
