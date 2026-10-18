// ================================================================================================
// RMS CONSOLES - THE CLIENT BUILD
// ================================================================================================
//
// One source, three bundles. The DevOps console, the Configuration console and the documentation
// site all load the same script -- it refreshes their live regions, follows their logs, sorts their
// tables and wires their dismissable alerts, and none of that is specific to which console is on
// screen. The sources for it are `src/index.js` in this directory.
//
// Three bundles rather than one shared file because a browser fetches a script from an origin, and
// each console is its own origin: they are separate web sites, possibly on separate hosts. A single
// copy served by one of them would be a cross-origin script on the other two, refused by their own
// policy -- `SitePage.RenderHead` allows scripts from this origin and no other. So each site gets
// its own file, at its own path, built from the same source in the same run.
//
// Two decisions are written down here because they are the kind that get undone by accident.
//
// The first is `devtool: false`. There are no source maps, deliberately. A source map is a file that
// reconstructs the sources of everything the browser was sent, served from the same origin as the
// pages, and these consoles are reached only from inside an operator's network. Nothing about a
// stack trace here is worth publishing the sources for.
//
// The second is that the shipped file keeps nothing. Every comment is dropped and every name is
// mangled: the output is a file to be executed, not a file to be read. The readable form of
// everything in it is `src/index.js`, one file, commented at the length the subject deserves. What
// the browser is handed is the smallest thing that does the same work.
//
// Keeping comments out is the minifier's job, and it takes two settings, which is why both are set
// below:
//
//   * `terserOptions.format.comments: false` drops every comment carried up from the source.
//   * `extractComments: false` stops Terser from doing the opposite of that -- left at its default
//     it hunts for comments that look like licence notices and writes them to a second file beside
//     the bundle, which would mean each site directory contained a file this config never named,
//     that nothing loads, and that published comment text nobody asked for.
//
// Webpack's own scaffolding comments are not a third setting: the reader they exist for is a
// developer inside `webpack --watch`, and the minifier removes them from this output along with
// everything else.

'use strict';

const path = require('path');
const TerserPlugin = require('terser-webpack-plugin');

module.exports = {
    // The consoles are read from the machines running the scanner, and this script is written the way
    // it is -- `var`, no arrows, no `const` -- so that it runs on the oldest browser that might be
    // left open on a second monitor. Nothing is transpiled, and the minifier is held to the same
    // level below, so nothing newer than this can appear in the output either.
    target: ['web', 'es5'],

    // This directory is the only entry point. Every entry below is the same file.
    context: __dirname,

    entry: {
        // Each name is the project whose Scripts directory receives the bundle, and each becomes a
        // directory level in `output.filename`.
        'RMS.Devops.Site': path.resolve(__dirname, 'src', 'index.js'),
        'RMS.Configuration.Site': path.resolve(__dirname, 'src', 'index.js'),
        'RMS.Docs.Site': path.resolve(__dirname, 'src', 'index.js')
    },

    mode: 'production',

    // No source maps, anywhere, under any mode.
    devtool: false,

    optimization: {
        // The output is minified as hard as the minifier can manage without changing behaviour.
        minimize: true,

        minimizer: [
            new TerserPlugin({
                // One minifier per core. Three small bundles in parallel, which is faster than three
                // in a row, and the number of them grows with the number of consoles.
                parallel: true,

                // No second file beside any bundle. See the note at the top of this file.
                extractComments: false,

                terserOptions: {
                    // ES5, and not a step further. An operator's second monitor is exactly where an
                    // old browser survives, and minification is not allowed to be the thing that
                    // finally breaks it -- so constructs the minifier could synthesise but the
                    // target cannot parse are ruled out here rather than discovered later.
                    ecma: 5,

                    compress: {
                        // More than one pass: the first pass exposes work a second pass can drop.
                        passes: 3,

                        // `debugger` statements are left in a source only by accident, and this is a
                        // production artifact, so they go. `console` calls are deliberately kept:
                        // they are how an operator reads what a console is doing when something is
                        // wrong, and dropping them would remove the only evidence available in a
                        // browser they cannot attach a debugger to.
                        drop_debugger: true,

                        // Rule out the rewrites that would introduce syntax the target predates.
                        arrows: false,

                        // Dead code is worth more to remove from the shipped file than to keep. The
                        // `unsafe*` family of options is left off on purpose: they are named that
                        // because they can reorder or delete work that has an effect.
                        dead_code: true,
                        unused: true
                    },

                    mangle: {
                        // Each bundle is one self-contained IIFE that nothing imports, so its top
                        // level names are private to it and can be shortened along with the rest.
                        toplevel: true
                    },

                    format: {
                        // Every comment, including the ones this source wrote for a reader.
                        comments: false,

                        // Emit stock ASCII so the file is not sensitive to how the response is
                        // encoded on the way out, which for a WebForms handler is not this file's
                        // business to assume.
                        ascii_only: true
                    }
                }
            })
        ],

        // One file per entry, and no file shared between them: the three bundles have to stand alone,
        // because the browser loading one of them has never loaded the other two.
        splitChunks: false,
        runtimeChunk: false
    },

    output: {
        // The repository's src directory -- two levels up from this one, because this client sits
        // inside the shared library rather than beside the sites it builds for.
        path: path.resolve(__dirname, '..', '..'),

        // A project name and the path inside it. The directory each project publishes is the one its
        // own .csproj lists as content, and each of the three lists Scripts\site.js.
        filename: '[name]/Scripts/site.js',

        // The Scripts directories hold files this build did not produce, so they are not swept.
        clean: false,

        // Each bundle is an ordinary script of its own, not a library anything else imports.
        iife: true,
        library: undefined

        // Nothing above the file: no banner. There is no `output.comments` here to set, because
        // webpack 5 has no such option -- every comment, the scaffolding included, is removed by
        // the minifier.
    },

    performance: {
        // The ceiling here is a guard, not an optimisation: each of these is a few kilobytes
        // minified, and a minified bundle that suddenly passes 64 KiB means something was added that
        // deserves a second look before it ships.
        maxEntrypointSize: 65536,
        maxAssetSize: 65536,
        hints: 'warning'
    },

    stats: {
        colors: false,
        modules: true,
        reasons: true,
        errorDetails: true
    }
};
