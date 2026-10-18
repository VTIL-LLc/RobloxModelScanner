/*
  ================================================================================================
  RMS MODEL SCANNER - CLOSING AN ALERT
  ================================================================================================

  The notices at the top of a page and the warnings a page shows about its own state can be closed.
  The button is rendered by the server with the markup of the alert it belongs to, and carries a
  data-dismiss attribute; the behaviour is attached here.

  It is done this way rather than with an inline handler because the pages are served under a policy
  that permits scripts from this origin only, so an attribute containing a script would be refused --
  and because a page whose markup contains its own script is a page whose script is in two places.
*/

/**
 * Wires the close buttons of every dismissable alert under an element.
 *
 * @param {Element} root the element to search.
 * @returns {void}
 */
export function wireDismiss(root) {
    if (!root) {
        return;
    }

    var buttons = root.querySelectorAll('[data-dismiss="alert"]');

    for (var index = 0; index < buttons.length; index++) {
        var button = buttons[index];

        // A button inside a region that has just been rebuilt arrives without a handler and with no memory
        // of having had one, so it is wired again. A button that was never rebuilt still has its mark and
        // is left alone, or it would be given a second handler and close on a click it had already handled.
        if (button.getAttribute('data-dismiss-wired') === '1') {
            continue;
        }

        button.setAttribute('data-dismiss-wired', '1');

        button.addEventListener('click', function (event) {
            var alert = event.currentTarget.closest('.alert');

            if (alert) {
                alert.parentNode.removeChild(alert);
            }
        });
    }
}

/**
 * Wires the filter form so that changing a select submits it.
 *
 * @param {Element} root the element to search.
 * @returns {void}
 */
export function wireFilters(root) {
    if (!root) {
        return;
    }

    var forms = root.querySelectorAll('form.filter-form');

    for (var index = 0; index < forms.length; index++) {
        var form = forms[index];

        if (form.getAttribute('data-filters-wired') === '1') {
            continue;
        }

        form.setAttribute('data-filters-wired', '1');

        var selects = form.querySelectorAll('select');

        for (var position = 0; position < selects.length; position++) {
            selects[position].addEventListener('change', function () {
                // A select is a choice rather than a value somebody is in the middle of typing, so there is
                // nothing to lose by acting on it immediately. The text box beside it is left alone: that
                // one is submitted by its own button.
                form.submit();
            });
        }
    }
}
