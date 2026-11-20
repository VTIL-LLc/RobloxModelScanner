// ================================================================================================
// RMS MODEL SCANNER - THE STYLESHEET MINIFIER
// ================================================================================================
//
// One function, no dependencies, for the one thing the build needs from CSS: the served file keeps
// nothing a reader does not need. Comments go, indentation goes, runs of whitespace become one space.
//
// It is not a general CSS minifier and is not written as one. A real one parses the stylesheet and can
// therefore drop the last semicolon in a block, shorten `#ffffff` to `#fff`, merge duplicate rules and
// know that `calc(1px + 2px)` needs the spaces around the plus. This does none of that, because doing it
// with a regular expression is how a build breaks a layout in a way nobody can find: the cost of being
// wrong here is a page that renders differently, and the saving it would buy is a few hundred bytes.
//
// What it does do is string-aware. A quoted run is copied through untouched, because whitespace inside a
// font family or a content string is content; everything outside a quote is where collapsing is safe.

'use strict';

/**
 * Removes the comments and the layout from a stylesheet.
 *
 * @param {string} css the stylesheet.
 * @returns {string} the stylesheet, in one line where it can be.
 */
function minifyCss(css) {
    let out = '';
    let at = 0;
    let space = false;

    const flushSpace = (before) => {
        // A space is only worth writing where it separates two things. Around a brace, a semicolon or a
        // comma it separates nothing, so it is dropped rather than kept -- except before a comma, where a
        // selector like `a, b` would read the same either way but keeping it costs one byte.
        if (!space) return;
        space = false;

        if (!out) return;

        const last = out.charAt(out.length - 1);

        if ('{};,'.indexOf(last) >= 0) return;
        if (before && '{};,'.indexOf(before) >= 0) return;

        out += ' ';
    };

    while (at < css.length) {
        const character = css[at];

        if (character === '/' && css[at + 1] === '*') {
            const end = css.indexOf('*/', at + 2);

            at = end < 0 ? css.length : end + 2;
            space = true;
            continue;
        }

        if (character === '"' || character === "'") {
            flushSpace(character);

            let end = at + 1;

            while (end < css.length && css[end] !== character) {
                // A backslash escapes the next character, which may be the quote itself.
                if (css[end] === '\\') end += 1;
                end += 1;
            }

            out += css.slice(at, end + 1);
            at = end + 1;
            continue;
        }

        if (character === ' ' || character === '\t' || character === '\n' || character === '\r' || character === '\f') {
            space = true;
            at += 1;
            continue;
        }

        flushSpace(character);
        out += character;
        at += 1;
    }

    return out;
}

module.exports = { minifyCss };
