using System;
using System.IO;
using System.Runtime.InteropServices;

namespace RMS.Site.Common.Implementation
{
    /// <summary>
    /// Loads the native SQLite library by its full path before the provider looks for it by name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The SQLite provider arrives as a managed assembly plus a native library that sits under
    /// <c>runtimes/&lt;platform&gt;/native</c>. On a console or a service the provider's own probe finds it.
    /// Inside IIS it does not, because a web application's binaries are collected under <c>bin</c> while the
    /// native library is resolved relative to the application root, and a development host and a production
    /// host disagree about which of those the current directory is.
    /// </para>
    /// <para>
    /// Rather than depend on that, the library is loaded here, by absolute path, before the first connection is
    /// opened. Windows resolves a later <c>LoadLibrary("e_sqlite3")</c> to the module already in the process,
    /// whatever the search path would have said, and the same holds for <c>dlopen</c> on Unix. The provider
    /// therefore succeeds for the same reason it succeeds everywhere else, and no environment variable has to
    /// be set on the server for the site to open its own database.
    /// </para>
    /// <para>
    /// The work is attempted once. A site that cannot load its database must fail visibly at the first
    /// request rather than retry a file system walk on every page, and <see cref="Diagnostic"/> is what
    /// explains the failure without exposing it to the browser.
    /// </para>
    /// </remarks>
    public static class NativeSqlite
    {
        /// <summary>
        /// The name the provider looks for on Windows.
        /// </summary>
        private const string WindowsLibrary = "e_sqlite3.dll";

        /// <summary>
        /// The name the provider looks for on Linux.
        /// </summary>
        private const string LinuxLibrary = "libe_sqlite3.so";

        /// <summary>
        /// The name the provider looks for on macOS.
        /// </summary>
        private const string MacLibrary = "libe_sqlite3.dylib";

        private static readonly object Gate = new object();

        private static bool _attempted;
        private static string _diagnostic = "not attempted";

        /// <summary>
        /// Gets a description of what happened, for the site's own diagnostics.
        /// </summary>
        public static string Diagnostic
        {
            get
            {
                lock (Gate)
                {
                    return _diagnostic;
                }
            }
        }

        /// <summary>
        /// Loads the native library if it has not already been loaded.
        /// </summary>
        /// <returns>The path that was loaded, or an empty string when none could be.</returns>
        public static string EnsureLoaded()
        {
            lock (Gate)
            {
                if (_attempted) return _diagnostic;

                _attempted = true;

                string root = AppDomain.CurrentDomain.BaseDirectory;

                foreach (string candidate in Candidates(root))
                {
                    if (!File.Exists(candidate)) continue;

                    // An already-loaded module is the success case rather than an error, so the result is not
                    // inspected: what matters is that the name resolves from here on.
                    Load(candidate);

                    _diagnostic = candidate;
                    return candidate;
                }

                _diagnostic = "no native SQLite library was found under " + root;
                return string.Empty;
            }
        }

        /// <summary>
        /// Builds the list of paths to try, most likely first.
        /// </summary>
        /// <param name="root">The application's base directory.</param>
        /// <returns>The candidate paths.</returns>
        private static string[] Candidates(string root)
        {
            string library = LibraryName();
            string architecture = ArchitectureTag();

            // A published console or service keeps the runtime folders beside the assemblies. A web
            // application publishes them beside the assemblies too, and the assemblies are under bin, so both
            // layouts are tried rather than guessing which kind of host this is.
            return new[]
            {
                Path.Combine(root, "runtimes", architecture, "native", library),
                Path.Combine(root, "bin", "runtimes", architecture, "native", library),
                Path.Combine(root, "bin", library),
                Path.Combine(root, library),
            };
        }

        /// <summary>
        /// Gets the native library's file name for the running platform.
        /// </summary>
        /// <returns>The file name.</returns>
        private static string LibraryName()
        {
            if (IsWindows()) return WindowsLibrary;

            return IsMac() ? MacLibrary : LinuxLibrary;
        }

        /// <summary>
        /// Gets the runtime identifier fragment for the running architecture.
        /// </summary>
        /// <returns>The tag, for example <c>win-x64</c>.</returns>
        /// <remarks>
        /// The tag is built from what the process actually is rather than from the operating system, so a
        /// 32-bit application pool on a 64-bit machine loads the library it can actually call into. Choosing
        /// by operating system alone is the mistake that produces a bad-image error at the first query.
        /// </remarks>
        private static string ArchitectureTag()
        {
            string prefix = IsWindows() ? "win" : IsMac() ? "osx" : "linux";
            string suffix = IntPtr.Size == 8 ? "x64" : "x86";

            return prefix + "-" + suffix;
        }

        /// <summary>
        /// Loads a library by absolute path.
        /// </summary>
        /// <param name="path">The library's path.</param>
        private static void Load(string path)
        {
            if (IsWindows())
            {
                LoadLibraryWindows(path);
                return;
            }

            LoadLibraryPosix(path);
        }

        /// <summary>
        /// Determines whether the process is running on Windows.
        /// </summary>
        /// <returns><see langword="true"/> when it is.</returns>
        private static bool IsWindows()
        {
            return Path.DirectorySeparatorChar == '\\';
        }

        /// <summary>
        /// Determines whether the process is running on macOS.
        /// </summary>
        /// <returns><see langword="true"/> when it is.</returns>
        private static bool IsMac()
        {
            return !IsWindows() && Directory.Exists("/Library/Frameworks");
        }

        /// <summary>
        /// Loads a library through the Windows loader.
        /// </summary>
        /// <param name="path">The library's path.</param>
        private static void LoadLibraryWindows(string path)
        {
            // The wide entry point is used explicitly so that a path outside the ANSI code page still works,
            // which matters because a deployment directory can be named in any language.
            IntPtr handle = LoadLibraryW(path);

            // A failure is not fatal here. It means the provider will have to find the library itself, which
            // it may well do, and the site's first query will report the truth either way.
            GC.KeepAlive(handle);
        }

        /// <summary>
        /// Loads a library through the Unix dynamic loader.
        /// </summary>
        /// <param name="path">The library's path.</param>
        private static void LoadLibraryPosix(string path)
        {
            // The loader is provisionally named "dl" on both Linux and macOS. The two other spellings exist
            // because glibc 2.34 folded libdl into libc and musl never had it, and a web site that cannot open
            // its own database over a linker detail is a web site with an unreadable error message.
            foreach (string loader in new[] { "dl", "libdl.so.2", "libSystem.dylib" })
            {
                try
                {
                    if (dlopen(path, 2) != IntPtr.Zero) return;
                }
                catch (DllNotFoundException)
                {
                    // Try the next spelling.
                }
                catch (EntryPointNotFoundException)
                {
                    // Try the next spelling.
                }
            }
        }

        [DllImport("kernel32", EntryPoint = "LoadLibraryW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string fileName);

        [DllImport("dl", EntryPoint = "dlopen")]
        private static extern IntPtr dlopen(string fileName, int flags);
    }
}
