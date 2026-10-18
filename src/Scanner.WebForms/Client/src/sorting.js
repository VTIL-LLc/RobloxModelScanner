/*
  ================================================================================================
  RMS MODEL SCANNER - SORTING A TABLE
  ================================================================================================

  Clicking a column's heading arranges the rows that are on screen. That is all it does: it does not
  ask the API for a different order and then show the same rows in a way that disagrees with the
  pager. Which order a column means is decided by the API and by the filter, and this is the reader
  rearranging what they already have in front of them.

  What a cell sorts on is its data-sort attribute when it has one, and its text otherwise. The
  identifier column carries the attribute because a column of numbers sorted as text puts 100 before
  99 -- and a reader who sees that once stops trusting the other columns.

  The order a reader chose is remembered on the region they chose it in, and re-applied after the
  region refreshes. Without that, a table would silently rearrange itself every fifteen seconds,
  which is exactly the kind of thing that makes a page feel broken.
*/

var EMPTY = '\u0000';

/**
 * Reads the value a cell should be compared on.
 *
 * @param {Element} cell the cell.
 * @returns {string} the value.
 */
function key(cell) {
    var explicit = cell.getAttribute('data-sort');

    if (explicit !== null) {
        return explicit;
    }

    var titled = cell.querySelector('[title]');

    // A cell whose whole meaning is in a tooltip -- an instant, which is rendered as "4 minutes ago" and
    // titled with the moment itself -- sorts on the tooltip. Sorting those on the words would order them
    // by how long the phrase is.
    if (titled && !cell.textContent.trim()) {
        return titled.getAttribute('title');
    }

    return cell.textContent.trim();
}

/**
 * Compares two values the way a person expects a column to be ordered.
 *
 * @param {string} left the left value.
 * @param {string} right the right value.
 * @returns {number} the comparison.
 */
function compare(left, right) {
    if (left === EMPTY && right === EMPTY) return 0;
    if (left === EMPTY) return 1;
    if (right === EMPTY) return -1;

    // localeCompare with numeric:true is what orders "model 9" before "model 10". It is the browser's own
    // collation rather than a hand-rolled pair of tests, so it does the right thing for the digits and
    // the punctuation that actually appear in these columns.
    return left.localeCompare(right, undefined, { numeric: true, sensitivity: 'base' });
}

/**
 * Sorts a table's rows in place.
 *
 * @param {Element} table the table.
 * @param {number} column the index of the column to sort on.
 * @param {string} direction either "asc" or "desc".
 */
function sortRows(table, column, direction) {
    var body = table.tBodies[0];

    if (!body) {
        return;
    }

    var rows = Array.prototype.slice.call(body.rows);
    var empty = [];

    // A row that spans the whole table is a note -- "nothing matched", "no models on this page" -- and it
    // belongs at the end whatever the order, not in the middle of the results where its column index
    // does not even exist.
    rows = rows.filter(function (row) {
        if (row.cells.length === 1 && row.cells[0].colSpan > 1) {
            empty.push(row);
            return false;
        }

        return true;
    });

    rows.sort(function (left, right) {
        var leftValue = left.cells[column] ? key(left.cells[column]) : EMPTY;
        var rightValue = right.cells[column] ? key(right.cells[column]) : EMPTY;

        var result = compare(leftValue, rightValue);

        return direction === 'desc' ? -result : result;
    });

    for (var index = 0; index < rows.length; index++) {
        body.appendChild(rows[index]);
    }

    for (var note = 0; note < empty.length; note++) {
        body.appendChild(empty[note]);
    }
}

/**
 * Wires the headings of every sortable table under an element.
 *
 * @param {Element} root the element to search.
 * @returns {void}
 */
export function wireSorting(root) {
    if (!root) {
        return;
    }

    var tables = root.querySelectorAll('table[data-sortable="true"]');

    for (var index = 0; index < tables.length; index++) {
        var table = tables[index];

        // A table the reader has already sorted, or one this page has already wired, is not wired twice:
        // a second set of click handlers would make one click sort twice and appear to do nothing.
        if (table.getAttribute('data-sort-wired') === '1') {
            continue;
        }

        table.setAttribute('data-sort-wired', '1');

        var headings = table.querySelectorAll('thead th');

        for (var column = 0; column < headings.length; column++) {
            attach(table, headings[column], column);
        }
    }
}

/**
 * Attaches the click handler to one heading.
 *
 * @param {Element} table the table the heading belongs to.
 * @param {Element} heading the heading cell.
 * @param {number} column the column's index.
 */
function attach(table, heading, column) {
    // A heading is a span and not an anchor, so this is a click rather than a navigation, and the reader
    // does not get a history entry for every column they tried.
    var label = heading.querySelector('.sortable-head');

    if (label) {
        label.setAttribute('role', 'button');
        label.setAttribute('tabindex', '0');
    }

    function activate() {
        var region = table.closest('.rms-live');

        var current = region ? region.getAttribute('data-sort-col') : null;
        var direction = region ? region.getAttribute('data-sort-dir') : null;

        // Clicking the column already sorted flips it. Clicking another one starts that column ascending,
        // which is what a first click on a numeric column should do about half the time -- and there is no
        // way to know which half, so the predictable answer wins.
        var next = String(column) === current
            ? (direction === 'asc' ? 'desc' : 'asc')
            : 'asc';

        if (region) {
            region.setAttribute('data-sort-col', String(column));
            region.setAttribute('data-sort-dir', next);
        }

        sortRows(table, column, next);
        mark(table, column, next);
    }

    heading.addEventListener('click', activate);

    heading.addEventListener('keydown', function (event) {
        if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            activate();
        }
    });
}

/**
 * Marks which column is sorted and which way.
 *
 * @param {Element} table the table.
 * @param {number} column the column's index.
 * @param {string} direction the direction.
 */
function mark(table, column, direction) {
    var headings = table.querySelectorAll('thead th');

    for (var index = 0; index < headings.length; index++) {
        headings[index].removeAttribute('aria-sort');
        headings[index].classList.remove('sorted-asc', 'sorted-desc');
    }

    if (!headings[column]) {
        return;
    }

    headings[column].setAttribute('aria-sort', direction === 'asc' ? 'ascending' : 'descending');
    headings[column].classList.add(direction === 'asc' ? 'sorted-asc' : 'sorted-desc');
}

/**
 * Re-applies the order a reader chose, after their region has been rebuilt.
 *
 * @param {Element} region the region that was refreshed.
 * @returns {void}
 */
export function restoreSorting(region) {
    if (!region) {
        return;
    }

    var column = region.getAttribute('data-sort-col');

    if (column === null) {
        return;
    }

    var direction = region.getAttribute('data-sort-dir') || 'asc';

    var tables = region.querySelectorAll('table[data-sortable="true"]');

    for (var index = 0; index < tables.length; index++) {
        sortRows(tables[index], parseInt(column, 10), direction);
        mark(tables[index], parseInt(column, 10), direction);
    }
}
