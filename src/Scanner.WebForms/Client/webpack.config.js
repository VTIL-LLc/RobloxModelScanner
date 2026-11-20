// ================================================================================================
// RMS MODEL SCANNER - THE CLIENT BUILD
// ================================================================================================
//
// The site's script is written as modules under src/ and built into one file at Scripts/site.js.
// The deployer runs `npm install` and then `npm run build` in this directory before it builds the
// web project, and the built file is committed as well so that the site still runs from a checkout
// that has never had npm installed.
//
// Two decisions are written down here because they are the kind that get undone by accident.
//
// The first is `devtool: false`. There are no source maps, deliberately. A source map is a file that
// reconstructs the sources of everything the browser was sent, served from the same origin as the
// pages, and this site is reached only from inside an operator's network. Nothing about a stack trace
// here is worth publishing the sources for.
//
// The second is that the shipped file keeps nothing. Every comment is dropped and every name is
// mangled: the output is a file to be executed, not a file to be read. The readable form of
// everything in it is `src/`, four modules, one file each, which is where a reader should go. What
// the browser is handed is the smallest thing that does the same work -- there is no reason to spend
// an operator's bandwidth, or a reviewer's attention, on prose that the browser discards on arrival.
//
// Keeping comments out is the minifier's job, and it takes two settings, which is why both are set
// below:
//
//   * `terserOptions.format.comments: false` drops every comment carried up from the modules.
//   * `extractComments: false` stops Terser from doing the opposite of that -- left at its default it
//     hunts for comments that look like licence notices and writes them to a second file beside the
//     bundle, which would mean the build directory contained a file this config never named, that
//     nothing loads, and that published comment text nobody asked for. There is nothing in the bundle
//     worth preserving that way, so nothing is extracted.
//
// Webpack's own scaffolding comments -- the `/* webpack/runtime/... */` banners it writes around the
// runtime -- are not a third setting: the reader they exist for is a developer inside `webpack --watch`,
// and the minifier removes them from this output along with everything else.

'use strict';

const fs = require('fs');
const path = require('path');
const TerserPlugin = require('terser-webpack-plugin');

const { minifyCss } = require('./tools/minify-css');

// ================================================================================================
// THE STYLESHEET
// ================================================================================================
//
// The page loads two stylesheets, and only one of them used to be built. The framework's is vendored
// and was minified once, by hand, before it was committed; this site's own was edited in place under
// Content/ and served exactly as written, which meant the only file in the repository that a publish
// shipped unminified was the one this project actually maintains.
//
// It is now a source under src/ like everything else, and it is written out to Content/site.css by this
// build. The output is committed beside the script, for the same reason the script is: a checkout that
// has never had npm installed still runs this site.
//
// Emitting it from here rather than from a loader keeps the dependency list to what it is. A loader
// would parse the CSS into a module graph this project has no use for: there is one stylesheet, nothing
// imports it, and nothing about it is dynamic. What is wanted is a file that is copied with its comments
// and its layout removed, which is a hundred lines in tools/minify-css.js and no new package.
class StylesheetPlugin {
    apply(compiler) {
        compiler.hooks.thisCompilation.tap('Stylesheet', (compilation) => {
            compilation.hooks.processAssets.tap(
                { name: 'Stylesheet', stage: compiler.webpack.Compilation.PROCESS_ASSETS_STAGE_ADDITIONAL },
                () => {
                    const source = fs.readFileSync(path.resolve(__dirname, 'src', 'site.css'), 'utf8');
                    const css = minifyCss(source);

                    compilation.emitAsset('Content/site.css', new compiler.webpack.sources.RawSource(css));
                });
        });
    }
}

module.exports = {
    // The sources are modules, so they are parsed as modules. Nothing is transpiled: the browsers that
    // reach this site are the ones on the machines running the scanner, and every one of them has
    // supported ES modules and the DOM methods used here for years.
    target: ['web', 'es2017'],

    // This directory is the only entry point. Everything else is reached from it.
    context: __dirname,
    entry: path.resolve(__dirname, 'src', 'index.js'),

    mode: 'production',

    // No source maps, anywhere, under any mode.
    devtool: false,

    optimization: {
        // The output is minified as hard as the minifier can manage without changing behaviour.
        minimize: true,

        minimizer: [
            new TerserPlugin({
                // One minifier per core. The bundle is small, so this saves little, but it costs
                // nothing on a build machine and keeps the build time flat as the script grows.
                parallel: true,

                // No second file beside the bundle. See the note at the top of this file.
                extractComments: false,

                terserOptions: {
                    // The target above is ES2017, so the minifier is told it may use it.
                    ecma: 2017,

                    compress: {
                        // More than one pass: the first pass exposes work a second pass can drop.
                        passes: 3,

                        // `debugger` statements are left in a source only by accident, and this is a
                        // production artifact, so they go. `console` calls are deliberately kept:
                        // they are how an operator reads what the page is doing when something is
                        // wrong, and dropping them would remove the only evidence available in a
                        // browser they cannot attach a debugger to.
                        drop_debugger: true,

                        // Dead code is worth more to remove from the shipped file than to keep. The
                        // `unsafe*` family of options is left off on purpose: they are named that
                        // because they can reorder or delete work that has an effect.
                        dead_code: true,
                        unused: true
                    },

                    mangle: {
                        // The bundle is one self-contained IIFE that nothing imports, so its top
                        // level names are private to it and can be shortened along with the rest.
                        toplevel: true
                    },

                    format: {
                        // Every comment, including the ones the modules wrote for a reader.
                        comments: false,

                        // Emit stock ASCII so the file is not sensitive to how the response is
                        // encoded on the way out, which for a WebForms handler is not this file's
                        // business to assume.
                        ascii_only: true
                    }
                }
            })
        ],

        // One file, not one per module: the page loads exactly one script, and the site's policy allows
        // scripts from its own origin, which a chunk loaded later would still satisfy but at the cost of
        // a request per module.
        splitChunks: false,
        runtimeChunk: false
    },

    output: {
        // The web project itself, because this build writes two things into it: the script under
        // Scripts/, which is where the site's own element in the document points, and the stylesheet
        // under Content/, which is where the document's link points.
        path: path.resolve(__dirname, '..'),
        filename: 'Scripts/site.js',

        // The directories hold files this build did not produce -- the icons, the framework stylesheet --
        // so they are not swept. Nothing is named with a hash, because the page names one file and a
        // server-rendered element cannot know a hash it would have to be given. The address is stamped
        // with the file's write time instead, which is the server's job and not the build's.
        clean: false,

        // The bundle is an ordinary script of its own, not a library anything else imports.
        iife: true,
        library: undefined

        // Nothing above the file: no banner. There is no `output.comments` here to set, because webpack
        // 5 has no such option -- every comment, the scaffolding included, is removed by the minifier.
    },

    // The build writes the stylesheet as well as the script. See the note above it.
    plugins: [new StylesheetPlugin()],

    performance: {
        // The ceiling here is a guard, not an optimisation: this script is a few kilobytes minified,
        // and a minified bundle that suddenly passes 64 KiB means something was added that deserves a
        // second look before it ships.
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
