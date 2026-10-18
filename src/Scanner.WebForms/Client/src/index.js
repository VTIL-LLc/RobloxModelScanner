/*
  ================================================================================================
  RMS MODEL SCANNER - THE PAGE SCRIPT
  ================================================================================================

  This is the whole client side of the site: four modules and no framework. What it does is in them,
  and this file only decides when.

  The one thing worth saying here is the order. A region is refreshed by replacing its contents, so
  everything that was wired inside it is gone and has to be wired again -- sorting, the close buttons
  of the alerts that arrived with it, and the filter form if it was inside a region (it is not, but
  the handler is written not to care either way). That is why the refresh callback exists rather than
  each module doing its own watching.
*/

import { onRefresh, startLiveRegions } from './live';
import { restoreSorting, wireSorting } from './sorting';
import { wireDismiss, wireFilters } from './alerts';

/**
 * Wires everything that lives inside an element.
 *
 * @param {Element} root the element.
 * @returns {void}
 */
function wire(root) {
    wireSorting(root);
    wireDismiss(root);
    wireFilters(root);
}

/**
 * Starts everything once the document is there.
 *
 * @returns {void}
 */
function start() {
    wire(document);

    // A refreshed region arrives with a table that has never been wired and, if the reader had sorted it,
    // an order that has to be put back on it. Both happen after the contents are in place, which is why
    // this is a callback rather than something each module watches for on its own.
    onRefresh(function (region) {
        wire(region);
        restoreSorting(region);
    });

    startLiveRegions();
}

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', start);
} else {
    start();
}
