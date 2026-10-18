/*
 * RMS DEPLOYER — SINGLE TRANSLATION UNIT BOOTSTRAPPER
 * ============================================================================
 * PURPOSE
 *   Take a bare machine and make the whole RMS stack run on it. The program
 *   detects the platform, downloads and installs only the prerequisites that
 *   are actually missing, builds every project in dependency order, publishes
 *   the results, and registers the resulting processes as supervised services.
 *
 *   It is one C translation unit with no third-party dependency, because the
 *   machine it has to run on is by definition the machine that has nothing
 *   installed yet. A .NET or Go bootstrapper would have to arrive as a binary
 *   per platform and would need a runtime that is not there; C is already
 *   there, and every target this project supports has a compiler or a package
 *   for one.
 *
 * SUPPORTED HOSTS
 *   Windows x64 and arm64, Linux x64 and arm64, macOS x64 and arm64. The
 *   program is the same on all three; only the download, extraction and
 *   service-management paths differ, and each of those is chosen from the
 *   detected platform rather than compiled out.
 *
 * WHAT IT INSTALLS
 *   - The .NET SDK, used to build and run every service.
 *   - Node.js, used only to produce the site bundles.
 *   - On Windows, the Visual Studio Build Tools, because the Web Forms sites
 *     target .NET Framework and the SDK's MSBuild cannot compile them.
 *   - On Linux and macOS, Mono, which supplies both the MSBuild that can
 *     compile the Web Forms sites and the xsp host that serves them. Linux
 *     installs it through the distribution's package manager; macOS installs
 *     it through Homebrew. Both are declared in the manifest, not hard-coded.
 *
 * DESIGN
 *   Nothing about the stack is hard-coded here. Three INI manifests beside
 *   this file describe it:
 *     - rmsdeploy.requirements: what must exist on the machine, per platform,
 *       how to fetch it, and how to tell whether it is already there.
 *     - rmsdeploy.build: the ordered list of things to build, each with an
 *       explicit kind and an explicit list of the platforms it applies to.
 *     - rmsdeploy.services: what to run once everything is built.
 *   Adding a project is a manifest edit, not a code change.
 *
 * IDEMPOTENCE
 *   Every step is safe to repeat. A prerequisite whose verification command
 *   already succeeds is never fetched. A build step is delegated to a tool
 *   that skips up-to-date outputs. Service registration replaces an existing
 *   definition rather than failing on it. Running the program twice in a row
 *   is therefore cheap, which is what makes it usable from a scheduled job.
 *
 * SECURITY NOTES
 *   - Only https URLs are accepted for downloads. A manifest that names an
 *     http endpoint is rejected rather than silently downgraded.
 *   - Nothing is extracted outside its own installation directory, and no
 *     downloaded archive is ever executed except for the installer kind, which
 *     is explicitly declared in the manifest and visible in review.
 *   - Service definitions written to systemd or launchd are given a dedicated
 *     least-privilege user where the platform allows it, and never a shell.
 *   - Secrets are not handled here. Anything a service needs is read from the
 *     environment by the service itself.
 *
 * BUILDING THIS FILE
 *   Linux:   cc -std=c11 -O2 -Wall -Wextra -o rmsdeploy rmsdeploy.c
 *   macOS:   cc -std=c11 -O2 -Wall -Wextra -o rmsdeploy rmsdeploy.c
 *   Windows: cl /nologo /O2 /W4 /D_CRT_SECURE_NO_WARNINGS rmsdeploy.c winhttp.lib
 *   All three are wired up in build.sh and build.cmd beside this file.
 *
 * USAGE
 *   rmsdeploy probe                     Report the platform and what is missing.
 *   rmsdeploy bootstrap                 Install missing prerequisites only.
 *   rmsdeploy build [step-id]           Build everything, or one step.
 *   rmsdeploy publish                   Build and copy publish output to --out.
 *   rmsdeploy services <action>         install | uninstall | start | stop | restart | status.
 *   rmsdeploy deploy                    bootstrap + build + publish + services.
 *
 *   Global options: --root <dir>  --tools <dir>  --out <dir>  --dry-run
 *                   --verbose     --keep-cache  --continue-on-error
 */

#if !defined(_WIN32)
#  if !defined(_POSIX_C_SOURCE)
#    define _POSIX_C_SOURCE 200809L
#  endif
#  if !defined(_DEFAULT_SOURCE)
#    define _DEFAULT_SOURCE 1
#  endif
#  if defined(__APPLE__) && !defined(_DARWIN_C_SOURCE)
#    define _DARWIN_C_SOURCE 1
#  endif
#endif

#if defined(_WIN32)
#  if !defined(_CRT_SECURE_NO_WARNINGS)
#    define _CRT_SECURE_NO_WARNINGS
#  endif
#  define RMS_WINDOWS 1
#elif defined(__APPLE__)
#  define RMS_MACOS 1
#  define RMS_POSIX 1
#else
#  define RMS_POSIX 1
#endif

#include <ctype.h>
#include <errno.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

#if defined(RMS_WINDOWS)
#  include <windows.h>
#  include <winhttp.h>
#  include <direct.h>
#  include <io.h>
#  include <process.h>
#  include <sys/stat.h>
#  include <sys/types.h>
#  define RMS_MKDIR(path) _mkdir(path)
#  define RMS_STRDUP      _strdup
#  define RMS_SEP         '\\'
#  define RMS_PATH_LIST   ';'
#else
#  include <dirent.h>
#  include <spawn.h>
#  include <sys/stat.h>
#  include <sys/types.h>
#  include <sys/wait.h>
#  include <unistd.h>
#  define RMS_MKDIR(path) mkdir((path), 0755)
#  define RMS_STRDUP      strdup
#  define RMS_SEP         '/'
#  define RMS_PATH_LIST   ':'
extern char **environ;
#endif

/*
 * Read the working directory.
 *
 * The buffer length is an int on Windows and a size_t elsewhere, so the cast
 * that the Windows signature needs is written once here rather than being
 * repeated -- or, worse, silently performed -- at each call site. Every buffer
 * this is used with is RMS_MAX_PATH bytes, which always fits.
 */
#if defined(RMS_WINDOWS)
#  define RMS_GETCWD(buffer, size) _getcwd((buffer), (int)(size))
#else
#  define RMS_GETCWD(buffer, size) getcwd((buffer), (size))
#endif

/* ==========================================================================
 * LIMITS
 *
 * Fixed buffers are used throughout because every string this program handles
 * is bounded by construction: a path, a URL from a manifest, or an identifier
 * from a manifest. A manifest cannot inject an unbounded value, because the
 * loader refuses a field wider than the buffer it has to fit in rather than
 * truncating it.
 * ========================================================================== */

#define RMS_MAX_PATH    4096
#define RMS_MAX_LINE    8192
/*
 * A Windows command line. It is larger than a line because the toolchain path,
 * the project path and the arguments are all quoted into one string, and every
 * one of them can be close to RMS_MAX_PATH on a deep checkout.
 */
#define RMS_MAX_COMMAND (RMS_MAX_PATH * 4)
#define RMS_MAX_ID      96
#define RMS_MAX_KIND    48
#define RMS_MAX_VERSION 96
#define RMS_MAX_VALUE   2048
#define RMS_MAX_NOTE    256

#define RMS_MAX_KEYS    24      /* Distinct keys a manifest section may carry. */
#define RMS_MAX_PLATFORMS 24    /* Section count ceiling per manifest. */

/* Exit codes, chosen so a caller can distinguish the failure classes. */
#define RMS_OK              0
#define RMS_ERR_USAGE       2
#define RMS_ERR_IO          3
#define RMS_ERR_DOWNLOAD    4
#define RMS_ERR_EXTRACT     5
#define RMS_ERR_BUILD       6
#define RMS_ERR_TOOLCHAIN   7

/* ==========================================================================
 * GLOBAL OPTIONS
 * ========================================================================== */

typedef struct
{
    char root[RMS_MAX_PATH];    /* Repository root. Manifests and projects hang off this. */
    char tools[RMS_MAX_PATH];   /* Where prerequisites are installed. */
    char out[RMS_MAX_PATH];     /* Where publish output is collected. */
    int  dry_run;               /* Print the plan without touching the machine. */
    int  verbose;               /* Echo every command before running it. */
    int  keep_cache;            /* Keep downloaded archives. */
    int  stop_on_error;         /* Abort the build on the first failing step. */
} Options;

static Options g_opts;

/* ==========================================================================
 * PLATFORM
 *
 * The platform name is the key that every per-platform manifest value is
 * selected with, so it is decided once, here, and used everywhere.
 * ========================================================================== */

static const char *
host_platform(void)
{
#if defined(RMS_WINDOWS)
    return "windows";
#elif defined(RMS_MACOS)
    return "macos";
#else
    return "linux";
#endif
}

static const char *
host_architecture(void)
{
#if defined(_WIN64) || defined(__x86_64__) || defined(__aarch64__) || defined(__ppc64__)
    return "64";
#else
    return "32";
#endif
}

/* ==========================================================================
 * CONSOLE OUTPUT
 *
 * Colour is emitted only on a terminal-capable host, so redirecting the output
 * to a log file cannot fill it with escape sequences.
 * ========================================================================== */

#if defined(RMS_WINDOWS)
#  define RMS_COLOUR_RESET  ""
#  define RMS_COLOUR_STEP   ""
#else
#  define RMS_COLOUR_RESET  "\033[0m"
#  define RMS_COLOUR_STEP   "\033[1m"
#endif

static void
rms_log(const char *level, const char *fmt, ...)
{
    va_list args;
    time_t now = time(NULL);
    struct tm local;

#if defined(RMS_WINDOWS)
    localtime_s(&local, &now);
#else
    localtime_r(&now, &local);
#endif

    fprintf(stdout, "%04d-%02d-%02d %02d:%02d:%02d %-5s ",
            local.tm_year + 1900, local.tm_mon + 1, local.tm_mday,
            local.tm_hour, local.tm_min, local.tm_sec, level);

    va_start(args, fmt);
    vfprintf(stdout, fmt, args);
    va_end(args);

    fputc('\n', stdout);
    fflush(stdout);
}

#define RMS_INFO(...)  rms_log("info",  __VA_ARGS__)
#define RMS_WARN(...)  rms_log("warn",  __VA_ARGS__)
#define RMS_ERROR(...) rms_log("error", __VA_ARGS__)

static void
rms_step(const char *fmt, ...)
{
    va_list args;

    fprintf(stdout, "\n%s", RMS_COLOUR_STEP);
    va_start(args, fmt);
    vfprintf(stdout, fmt, args);
    va_end(args);
    fprintf(stdout, "%s\n", RMS_COLOUR_RESET);
    fflush(stdout);
}

/* ==========================================================================
 * STRING AND PATH HELPERS
 * ========================================================================== */

/*
 * Copy into a fixed buffer, refusing to truncate silently. A truncated path is
 * always a bug, and continuing with it would install files into the wrong
 * directory, which is worse than stopping.
 *
 * The copy is a move rather than a plain copy so that passing the same buffer as
 * the source and the destination is defined rather than merely working on the
 * implementations where overlapping copies happen to be tolerated. Callers that
 * need to rewrite a value in place are common enough that the alternative is a
 * scratch buffer at each of them.
 */
static int
rms_copy(char *dst, size_t size, const char *src)
{
    size_t length = src != NULL ? strlen(src) : 0;

    if (length + 1 > size)
    {
        RMS_ERROR("value of %zu bytes does not fit in a %zu byte buffer: %.64s...", length, size, src);
        return -1;
    }

    memmove(dst, src, length + 1);
    return 0;
}

/*
 * Copy a known number of bytes into a fixed buffer, and terminate.
 *
 * Used where the text being copied is one entry inside a longer line -- an
 * argument, an environment assignment -- so the source is a length rather than
 * a string with an end. Truncating would silently change what a service is
 * started with, so an entry that does not fit is an error rather than a
 * shortened value.
 */
static int
rms_copy_n(char *dst, size_t size, const char *src, size_t length)
{
    if (length + 1 > size)
    {
        RMS_ERROR("an entry of %zu bytes does not fit in a %zu byte field: %.64s...",
                  length, size, src);
        return -1;
    }

    memcpy(dst, src, length);
    dst[length] = '\0';

    return 0;
}

static int
rms_join(char *dst, size_t size, const char *left, const char *right)
{
    size_t left_length = strlen(left);

    if (left_length == 0)
        return rms_copy(dst, size, right);

    if (snprintf(dst, size, "%s%c%s", left, RMS_SEP, right) >= (int)size)
    {
        RMS_ERROR("joined path does not fit in a %zu byte buffer", size);
        return -1;
    }

    return 0;
}

static int
rms_file_exists(const char *path)
{
    struct stat info;
    return stat(path, &info) == 0 && (info.st_mode & S_IFMT) != S_IFDIR;
}

static int
rms_dir_exists(const char *path)
{
    struct stat info;
    return stat(path, &info) == 0 && (info.st_mode & S_IFMT) == S_IFDIR;
}

/*
 * Rewrite a path as an absolute one, relative to the working directory.
 *
 * The publish root reaches a plan file, and a plan file reaches a service
 * definition, and a service definition is read by a process that was started
 * somewhere the deployer did not choose. A relative path anywhere in that chain
 * would be resolved against the wrong directory eventually, so the deployer
 * resolves them all here, once, rather than each consumer having to defend
 * itself.
 */
static int
rms_absolute(char *out, size_t size, const char *path)
{
    char   working[RMS_MAX_PATH];
    char   combined[RMS_MAX_PATH];
    size_t length;

    if (path[0] == '\0')
        return rms_copy(out, size, path);

#if defined(RMS_WINDOWS)
    /* "C:\" and a bare "C:" are taken as they stand: resolving a drive letter
     * would use that drive's own current directory, which is not what a
     * manifest means by "C:", and would make the result depend on state the
     * deployer never set. A UNC path is already absolute. */
    if ((path[1] == ':' && (path[2] == RMS_SEP || path[2] == '\0')) ||
        (path[0] == RMS_SEP && path[1] == RMS_SEP))
        return rms_copy(out, size, path);
#endif

    if (path[0] == RMS_SEP)
        return rms_copy(out, size, path);

    if (RMS_GETCWD(working, sizeof working) == NULL)
    {
        RMS_ERROR("the working directory could not be read: %s", strerror(errno));
        return -1;
    }

    /* A trailing separator would be doubled by the join below. */
    length = strlen(working);
    while (length > 1 && working[length - 1] == RMS_SEP)
        working[--length] = '\0';

    /* The join goes through a scratch buffer because the caller is allowed to
     * pass one buffer as both the input and the output, and the result of
     * formatting into a destination that overlaps a source is undefined. */
    if (rms_join(combined, sizeof combined, working, path) != 0)
        return -1;

    return rms_copy(out, size, combined);
}

/*
 * Create every missing component of a path. Existing components are left alone,
 * so calling this on a path that already exists is a no-op rather than an error.
 */
static int
rms_make_dirs(const char *path)
{
    char  scratch[RMS_MAX_PATH];
    size_t length;
    size_t index;

    if (rms_copy(scratch, sizeof scratch, path) != 0)
        return -1;

    /* Strip any trailing separator so the loop does not try to create "". */
    length = strlen(scratch);
    while (length > 1 && (scratch[length - 1] == RMS_SEP || scratch[length - 1] == '/'))
        scratch[--length] = '\0';

    for (index = 1; index < length; index++)
    {
        if (scratch[index] != RMS_SEP && scratch[index] != '/')
            continue;

#if defined(RMS_WINDOWS)
        /* Do not attempt to create a drive root such as "C:". */
        if (index == 2 && scratch[1] == ':')
            continue;
#endif

        scratch[index] = '\0';

        if (!rms_dir_exists(scratch) && RMS_MKDIR(scratch) != 0 && errno != EEXIST)
        {
            RMS_ERROR("cannot create directory %s: %s", scratch, strerror(errno));
            return -1;
        }

        scratch[index] = RMS_SEP;
    }

    if (!rms_dir_exists(scratch) && RMS_MKDIR(scratch) != 0 && errno != EEXIST)
    {
        RMS_ERROR("cannot create directory %s: %s", scratch, strerror(errno));
        return -1;
    }

    return 0;
}

/*
 * Remove a path and, if it is a directory, everything beneath it.
 *
 * A publish produces the files a project has today. It says nothing about the
 * files it produced yesterday, and the web publishing pipeline is a copier: it
 * adds and overwrites and never deletes. So a page that has been renamed, a
 * handler that has been retired and an assembly whose project has left the
 * solution all outlive the publish that should have removed them. In a deployed
 * web root that is not untidiness, it is code that still answers a request, and
 * a retired page is exactly the page nobody is looking at any more.
 *
 * A path that does not exist is success, because the caller's intent is that it
 * is gone, and it is.
 *
 * Nothing here follows a symbolic link. A link inside a published directory
 * would be removed as the link it is, rather than as the tree it points at, so
 * the reach of a wrong path stays where the publish put it.
 *
 * The refusal of a bare name is a floor, not a policy: this is called with
 * directories the deployer composed from its own publish table, and the one way
 * it could do harm is by being called with something that resolves against a
 * working directory. It declines that shape outright rather than trusting every
 * call site to have absolute paths, because the cost of being wrong is somebody
 * else's files.
 */
static int
rms_remove_tree(const char *path)
{
    char scratch[RMS_MAX_PATH];

    if (path == NULL || path[0] == '\0')
        return 0;

    if (rms_copy(scratch, sizeof scratch, path) != 0)
        return -1;

    /* Trim any trailing separator, so a path that ends with one is treated as
     * the directory it names rather than as an empty child of it. */
    while (strlen(scratch) > 1 && (scratch[strlen(scratch) - 1] == RMS_SEP || scratch[strlen(scratch) - 1] == '/'))
        scratch[strlen(scratch) - 1] = '\0';

    if (strchr(scratch, RMS_SEP) == NULL && strchr(scratch, '/') == NULL)
    {
        RMS_ERROR("refusing to remove %s: it is not a path", scratch);
        return -1;
    }

    if (!rms_file_exists(scratch) && !rms_dir_exists(scratch))
        return 0;

#if defined(RMS_WINDOWS)
    if (!rms_dir_exists(scratch))
    {
        /* A file copied out of a controlled directory can carry the read-only
         * attribute with it, and a read-only file is one DeleteFileA declines. */
        SetFileAttributesA(scratch, FILE_ATTRIBUTE_NORMAL);

        if (!DeleteFileA(scratch))
        {
            RMS_ERROR("cannot remove %s: error %lu", scratch, (unsigned long)GetLastError());
            return -1;
        }

        return 0;
    }

    {
        char            pattern[RMS_MAX_PATH];
        WIN32_FIND_DATAA found;
        HANDLE          listing;

        if (snprintf(pattern, sizeof pattern, "%s%c*", scratch, RMS_SEP) >= (int)sizeof pattern)
            return -1;

        listing = FindFirstFileA(pattern, &found);

        if (listing != INVALID_HANDLE_VALUE)
        {
            do
            {
                char child[RMS_MAX_PATH];

                if (strcmp(found.cFileName, ".") == 0 || strcmp(found.cFileName, "..") == 0)
                    continue;

                if (rms_join(child, sizeof child, scratch, found.cFileName) != 0 ||
                    rms_remove_tree(child) != 0)
                {
                    FindClose(listing);
                    return -1;
                }
            } while (FindNextFileA(listing, &found));

            FindClose(listing);
        }
    }

    if (!RemoveDirectoryA(scratch))
    {
        RMS_ERROR("cannot remove directory %s: error %lu", scratch, (unsigned long)GetLastError());
        return -1;
    }

    return 0;
#else
    {
        struct stat info;

        if (lstat(scratch, &info) != 0)
        {
            /* Vanished between the check above and here. The intent is still
             * met, so this is not reported as a failure. */
            return errno == ENOENT ? 0 : -1;
        }

        if (S_ISLNK(info.st_mode) || !S_ISDIR(info.st_mode))
        {
            if (unlink(scratch) != 0)
            {
                RMS_ERROR("cannot remove %s: %s", scratch, strerror(errno));
                return -1;
            }

            return 0;
        }
    }

    {
        DIR           *directory;
        struct dirent *entry;

        directory = opendir(scratch);

        if (directory == NULL)
        {
            RMS_ERROR("cannot read directory %s: %s", scratch, strerror(errno));
            return -1;
        }

        while ((entry = readdir(directory)) != NULL)
        {
            char child[RMS_MAX_PATH];

            if (strcmp(entry->d_name, ".") == 0 || strcmp(entry->d_name, "..") == 0)
                continue;

            if (rms_join(child, sizeof child, scratch, entry->d_name) != 0 ||
                rms_remove_tree(child) != 0)
            {
                closedir(directory);
                return -1;
            }
        }

        closedir(directory);
    }

    if (rmdir(scratch) != 0)
    {
        RMS_ERROR("cannot remove directory %s: %s", scratch, strerror(errno));
        return -1;
    }

    return 0;
#endif
}

/*
 * Expand %NAME% placeholders in a manifest value.
 *
 * Two kinds of name are understood. %ROOT% is the repository root, which is
 * what lets a manifest name an absolute path without knowing where the tree
 * will be checked out. Any other name is read from the environment, so a
 * manifest can say %ProgramFiles(x86)% or %HOME% and mean what the operator's
 * shell means by it.
 *
 * A name that is neither the root nor set in the environment is left in place
 * rather than replaced with nothing. A literal %ProgramFiles% left in a path is
 * obviously wrong when it is read back, whereas an empty substitution yields a
 * plausible-looking path that points somewhere unintended.
 *
 * The percent syntax is used on all three platforms, including the POSIX ones,
 * so that one manifest reads the same everywhere and nobody has to remember
 * which shell's convention is in force. A lone percent with no closing partner
 * is literal text, not a malformed placeholder.
 */
static int
expand_placeholders(const char *input, char *output, size_t size)
{
    const char *cursor = input;
    size_t      used = 0;

    for (;;)
    {
        const char *opening = strchr(cursor, '%');
        const char *closing;
        const char *value;
        size_t      chunk;
        char        name[RMS_MAX_ID];

        if (opening == NULL)
        {
            chunk = strlen(cursor);

            if (used + chunk + 1 > size)
                goto too_long;

            memcpy(output + used, cursor, chunk);
            used += chunk;
            break;
        }

        chunk = (size_t)(opening - cursor);

        if (used + chunk + 1 > size)
            goto too_long;

        memcpy(output + used, cursor, chunk);
        used += chunk;

        closing = strchr(opening + 1, '%');

        if (closing == NULL)
        {
            if (used + 2 > size)
                goto too_long;

            output[used++] = '%';
            cursor = opening + 1;
            continue;
        }

        {
            size_t name_length = (size_t)(closing - opening - 1);

            if (name_length == 0 || name_length >= sizeof name)
            {
                RMS_ERROR("a placeholder in a manifest value has an unusable name");
                return -1;
            }

            memcpy(name, opening + 1, name_length);
            name[name_length] = '\0';
        }

        value = strcmp(name, "ROOT") == 0 ? g_opts.root : getenv(name);

        if (value == NULL)
        {
            size_t token_length = (size_t)(closing - opening + 1);

            if (used + token_length + 1 > size)
                goto too_long;

            memcpy(output + used, opening, token_length);
            used += token_length;
            cursor = closing + 1;
            continue;
        }

        {
            size_t value_length = strlen(value);

            if (used + value_length + 1 > size)
                goto too_long;

            memcpy(output + used, value, value_length);
            used += value_length;
        }

        cursor = closing + 1;
    }

    output[used] = '\0';
    return 0;

too_long:
    RMS_ERROR("a manifest value is too long once its placeholders are expanded");
    return -1;
}

/* Expand in place, so the call site does not have to own a second buffer. */
static int
expand_value(char *value, size_t size)
{
    char expanded[RMS_MAX_VALUE];

    if (value[0] == '\0')
        return 0;

    if (expand_placeholders(value, expanded, sizeof expanded) != 0)
        return -1;

    return rms_copy(value, size, expanded);
}

/*
 * Rewrite forward slashes as the host separator.
 *
 * Manifests are written with forward slashes because that reads the same on all
 * three platforms, and %ROOT% expands to a native path -- so a Windows value
 * comes out mixed, which is accepted by the filesystem but looks wrong in a
 * status listing and in a generated service definition.
 *
 * Only fields that are genuinely filesystem paths are passed through here. A
 * value that may hold a URL must not be, because a URL's separators are part of
 * the URL and rewriting them would produce a broken address.
 */
static void
normalise_separators(char *path)
{
#if defined(RMS_WINDOWS)
    while (*path != '\0')
    {
        if (*path == '/')
            *path = RMS_SEP;

        path++;
    }
#else
    (void)path;     /* The POSIX separator is already the forward slash. */
#endif
}

/* ==========================================================================
 * PROCESS EXECUTION
 *
 * Two entry points. run_argv executes a program directly, which is what every
 * real build invocation should do because it avoids shell quoting entirely.
 * run_shell exists only for verification commands and package-manager calls,
 * which a manifest author must be able to copy and paste into a terminal.
 * ========================================================================== */

/*
 * Change the working directory for the duration of a child, then restore it.
 *
 * The parent's directory is changed rather than the child's, because the
 * portable POSIX way to do the latter is a GNU/BSD extension that is not
 * declared under the strict feature profile used here. This program is
 * single-threaded, so there is nothing else for the change to affect.
 */
static int
enter_directory(const char *directory, char *original, size_t size)
{
    if (directory == NULL)
        return 0;

#if defined(RMS_WINDOWS)
    if (RMS_GETCWD(original, size) == NULL)
        return -1;

    if (_chdir(directory) != 0)
    {
        RMS_ERROR("cannot enter %s", directory);
        return -1;
    }
#else
    if (RMS_GETCWD(original, size) == NULL)
        return -1;

    if (chdir(directory) != 0)
    {
        RMS_ERROR("cannot enter %s: %s", directory, strerror(errno));
        return -1;
    }
#endif

    return 1;
}

static void
leave_directory(const char *directory, const char *original)
{
    if (directory == NULL)
        return;

#if defined(RMS_WINDOWS)
    _chdir(original);
#else
    if (chdir(original) != 0)
        RMS_WARN("could not return to %s", original);
#endif
}

#if defined(RMS_WINDOWS)
/*
 * Append one argument to a Windows command line.
 *
 * Windows does not hand a new process an argument vector. It hands it a single
 * string, and the child splits that string again -- so an argument that contains
 * a space has to be quoted, and backslashes that immediately precede a quote have
 * to be doubled, or the quote is eaten as an escape and everything after it
 * shifts by one position.
 *
 * The rule about runs of backslashes is not a corner case here: a path is almost
 * entirely backslashes, and a path that ends in one before a closing quote is
 * the exact shape that breaks. Getting this wrong is what made the first build
 * report that "dotnet-Files\dotnet\dotnet.exe does not exist": the toolchain path
 * C:\Program Files\dotnet\dotnet.exe was passed unquoted, so the child saw
 * "C:\Program" as its program name and "Files\dotnet\dotnet.exe" as its first
 * argument, which .NET then read as the subcommand to run.
 */
static int
append_windows_argument(char *command_line, size_t size, const char *argument)
{
    size_t      used = strlen(command_line);
    size_t      backslashes = 0;
    const char *cursor;
    int         quoted = argument[0] == '\0' || strpbrk(argument, " \t\"") != NULL;

    if (quoted)
    {
        if (used + 2 > size)
            return -1;

        command_line[used++] = '"';
    }

    for (cursor = argument; *cursor != '\0'; cursor++)
    {
        if (*cursor == '\\')
        {
            backslashes++;
            continue;
        }

        if (*cursor == '"')
        {
            /* Double the pending backslashes, then escape the quote itself. */
            if (used + backslashes * 2 + 2 > size)
                return -1;

            while (backslashes > 0)
            {
                command_line[used++] = '\\';
                command_line[used++] = '\\';
                backslashes--;
            }

            command_line[used++] = '\\';
            command_line[used++] = '"';
        }
        else
        {
            if (used + backslashes + 2 > size)
                return -1;

            while (backslashes > 0)
            {
                command_line[used++] = '\\';
                backslashes--;
            }

            command_line[used++] = *cursor;
        }
    }

    if (used + backslashes * 2 + 2 > size)
        return -1;

    while (backslashes > 0)
    {
        /* Doubled only when a closing quote follows, which is exactly now. */
        command_line[used++] = '\\';

        if (quoted)
            command_line[used++] = '\\';

        backslashes--;
    }

    if (quoted)
        command_line[used++] = '"';

    command_line[used] = '\0';
    return 0;
}

/*
 * Build the command line for a child process, argv[0] included.
 *
 * A Windows command line conventionally begins with the program name even when
 * the executable is named separately, and the child's own argument parsing
 * depends on it being there. Writing it as an ordinary quoted argument keeps the
 * two views of the process -- the one Windows uses to start it, and the one the
 * program uses to read its arguments -- in agreement.
 */
static int
build_windows_command_line(char *const argv[], char *command_line, size_t size)
{
    int index;

    command_line[0] = '\0';

    for (index = 0; argv[index] != NULL; index++)
    {
        if (index > 0 && strcat(command_line, " ") == NULL)
            return -1;

        if (append_windows_argument(command_line, size, argv[index]) != 0)
            return -1;
    }

    return 0;
}
#endif /* RMS_WINDOWS */

static int
run_argv_in(const char *working_directory, char *const argv[])
{
    char original[RMS_MAX_PATH];
    int  entered;
    int  index;
    int  result;
    /*
     * The command line is printed for a dry run as well as for a verbose one. A dry run exists to show
     * what would be executed, and a plan that names steps without naming their commands shows the shape
     * of the build but not the thing an operator reviewing it most needs to see.
     */
    if (g_opts.verbose || g_opts.dry_run)
    {
        fprintf(stdout, "  $");
        for (index = 0; argv[index] != NULL; index++)
            fprintf(stdout, " %s", argv[index]);
        fputc('\n', stdout);
        fflush(stdout);
    }

    /*
     * Nothing is started. The caller is told the step succeeded, which is what lets the plan walk the
     * whole list and report what it would have done; the summary below is worded so that this is not
     * mistaken for a build that ran.
     */
    if (g_opts.dry_run)
        return RMS_OK;

    entered = enter_directory(working_directory, original, sizeof original);
    if (entered < 0)
        return -1;

#if defined(RMS_WINDOWS)
    {
        STARTUPINFOA        startup;
        PROCESS_INFORMATION process;
        char                command_line[RMS_MAX_COMMAND];
        DWORD               exit_code = 0;

        if (build_windows_command_line(argv, command_line, sizeof command_line) != 0)
        {
            RMS_ERROR("the command line for %s does not fit in %d bytes",
                      argv[0], (int)sizeof command_line);
            leave_directory(working_directory, original);
            return -1;
        }

        memset(&startup, 0, sizeof startup);
        startup.cb = sizeof startup;

        memset(&process, 0, sizeof process);

        /*
         * A name with a separator in it is used as the executable directly.
         * A bare name -- msbuild, xsp4, cmd -- is left to Windows to resolve
         * against PATH, and CreateProcess only does that when the executable is
         * named through the command line rather than on its own. Anything the
         * deployer launches is written one way or the other by the manifest, so
         * the two cases are both reachable.
         */
        if (!CreateProcessA(strpbrk(argv[0], "/\\") != NULL ? argv[0] : NULL,
                            command_line,
                            NULL, NULL, TRUE, CREATE_UNICODE_ENVIRONMENT,
                            NULL, NULL, &startup, &process))
        {
            RMS_ERROR("cannot start %s: error %lu", argv[0], (unsigned long)GetLastError());
            leave_directory(working_directory, original);
            return -1;
        }

        CloseHandle(process.hThread);
        WaitForSingleObject(process.hProcess, INFINITE);

        if (!GetExitCodeProcess(process.hProcess, &exit_code))
            exit_code = (DWORD)-1;

        CloseHandle(process.hProcess);

        result = (int)exit_code;
    }
#else
    {
        pid_t child;
        int   status;
        int   spawned = posix_spawnp(&child, argv[0], NULL, NULL, argv, environ);

        if (spawned != 0)
        {
            RMS_ERROR("cannot start %s: %s", argv[0], strerror(spawned));
            result = -1;
        }
        else if (waitpid(child, &status, 0) < 0)
        {
            result = -1;
        }
        else
        {
            result = WIFEXITED(status) ? WEXITSTATUS(status) : -1;
        }
    }
#endif

    if (entered == 1)
        leave_directory(working_directory, original);

    return result;
}

static int
run_argv(char *const argv[])
{
    return run_argv_in(NULL, argv);
}

/*
 * Run a command through the platform shell.
 *
 * When quiet is set the whole command is parenthesised before the redirection
 * is appended. A bare trailing redirection binds to the last command in an
 * `a || b` chain rather than to the chain, so a check written as a fallback
 * sequence leaked the first command's error output onto the terminal -- which is
 * exactly the output a quiet check exists to avoid, and it appeared at the point
 * where the operator is being told nothing is wrong.
 */
static int
run_shell(const char *command, int quiet)
{
    char wrapped[RMS_MAX_LINE];

    if (g_opts.verbose && !quiet)
        fprintf(stdout, "  $ %s\n", command);

    if (g_opts.dry_run)
        return RMS_OK;

#if defined(RMS_WINDOWS)
    if (snprintf(wrapped, sizeof wrapped, "%s%s%s",
                 quiet ? "(" : "", command, quiet ? ") >nul 2>&1" : "") >= (int)sizeof wrapped)
        return -1;

    {
        /* Routed through the same launcher as everything else, so that the
         * command line is quoted one way in this program rather than two. The
         * command itself is a single argument here, which is what makes a
         * manifest able to write shell syntax without the deployer having to
         * decide where one argument ends. */
        char *argv[] = { (char *)"cmd.exe", (char *)"/C", wrapped, NULL };

        return run_argv_in(NULL, argv);
    }
#else
    if (snprintf(wrapped, sizeof wrapped, "%s%s%s",
                 quiet ? "( " : "", command, quiet ? " ) >/dev/null 2>&1" : "") >= (int)sizeof wrapped)
        return -1;

    {
        char *const argv[] = { (char *)"/bin/sh", (char *)"-c", wrapped, NULL };
        return run_argv(argv);
    }
#endif
}

/*
 * Run a command and report whether it succeeded, without printing anything.
 * This is the primitive every "is it already installed" check is built on, so
 * it must never produce noise on the failure path: a missing tool is the normal
 * case on a fresh machine, not an error worth showing the operator.
 */
static int
command_succeeds(const char *command)
{
    if (g_opts.dry_run)
        return 0;

    return run_shell(command, 1) == 0;
}

/* ==========================================================================
 * HTTP DOWNLOAD
 *
 * Windows uses WinHTTP, which is part of the operating system, so no runtime
 * has to be bootstrapped before the bootstrapper can fetch anything. Linux and
 * macOS use curl, which ships with macOS and is present on every mainstream
 * Linux distribution and container base image, with wget as a fallback.
 * ========================================================================== */

#if defined(RMS_WINDOWS)

static int
utf8_to_wide(const char *source, wchar_t *destination, int capacity)
{
    return MultiByteToWideChar(CP_UTF8, 0, source, -1, destination, capacity) > 0 ? 0 : -1;
}

static int
http_download(const char *url, const char *destination)
{
    wchar_t         wide_url[RMS_MAX_VALUE];
    wchar_t         scheme[16] = { 0 };
    wchar_t         host[256] = { 0 };
    wchar_t         path[RMS_MAX_VALUE] = { 0 };
    wchar_t         extra[512] = { 0 };
    wchar_t         request_path[RMS_MAX_VALUE];
    URL_COMPONENTS  parts;
    HINTERNET       session = NULL;
    HINTERNET       connection = NULL;
    HINTERNET       request = NULL;
    FILE           *sink = NULL;
    DWORD           status_code = 0;
    DWORD           status_size = sizeof status_code;
    int             result = -1;
    char            buffer[65536];

    if (utf8_to_wide(url, wide_url, RMS_MAX_VALUE) != 0)
        return -1;

    ZeroMemory(&parts, sizeof parts);
    parts.dwStructSize = sizeof parts;
    parts.lpszScheme = scheme;
    parts.dwSchemeLength = (DWORD)(sizeof scheme / sizeof scheme[0]) - 1;
    parts.lpszHostName = host;
    parts.dwHostNameLength = (DWORD)(sizeof host / sizeof host[0]) - 1;
    parts.lpszUrlPath = path;
    parts.dwUrlPathLength = (DWORD)(sizeof path / sizeof path[0]) - 1;
    parts.lpszExtraInfo = extra;
    parts.dwExtraInfoLength = (DWORD)(sizeof extra / sizeof extra[0]) - 1;

    if (!WinHttpCrackUrl(wide_url, 0, 0, &parts))
    {
        RMS_ERROR("the URL could not be parsed: %s", url);
        return -1;
    }

    /* Refusing a non-https scheme here means a tampered manifest cannot turn a
     * download into a plaintext fetch of an executable toolchain. */
    if (_wcsicmp(scheme, L"https") != 0)
    {
        RMS_ERROR("refusing a non-https download: %s", url);
        return -1;
    }

    if (swprintf(request_path, RMS_MAX_VALUE, L"%ls%ls", path, extra) < 0)
        return -1;

    session = WinHttpOpen(L"rmsdeploy/1.0", WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY,
                          WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, 0);
    if (session == NULL)
        goto cleanup;

    connection = WinHttpConnect(session, host, parts.nPort, 0);
    if (connection == NULL)
        goto cleanup;

    request = WinHttpOpenRequest(connection, L"GET", request_path, NULL,
                                 WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES,
                                 WINHTTP_FLAG_SECURE);
    if (request == NULL)
        goto cleanup;

    {
        /* Modern TLS plus an unconditional redirect policy: the toolchain URLs
         * are version-pinned but they sit behind redirecting shims such as
         * aka.ms, and a bootstrapper that cannot follow a redirect cannot
         * install anything on a clean image. */
        DWORD protocols = WINHTTP_FLAG_SECURE_PROTOCOL_TLS1_2;
        DWORD redirect = WINHTTP_OPTION_REDIRECT_POLICY_ALWAYS;

#if defined(WINHTTP_FLAG_SECURE_PROTOCOL_TLS1_3)
        protocols |= WINHTTP_FLAG_SECURE_PROTOCOL_TLS1_3;
#endif

        WinHttpSetOption(request, WINHTTP_OPTION_SECURE_PROTOCOLS, &protocols, sizeof protocols);
        WinHttpSetOption(request, WINHTTP_OPTION_REDIRECT_POLICY, &redirect, sizeof redirect);
    }

    if (!WinHttpSendRequest(request, WINHTTP_NO_ADDITIONAL_HEADERS, 0,
                            WINHTTP_NO_REQUEST_DATA, 0, 0, 0))
    {
        RMS_ERROR("the download request failed: %s", url);
        goto cleanup;
    }

    if (!WinHttpReceiveResponse(request, NULL))
    {
        RMS_ERROR("no response was received: %s", url);
        goto cleanup;
    }

    if (!WinHttpQueryHeaders(request, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
                             WINHTTP_HEADER_NAME_BY_INDEX, &status_code, &status_size,
                             WINHTTP_NO_HEADER_INDEX))
        goto cleanup;

    if (status_code != 200)
    {
        RMS_ERROR("the download returned HTTP %lu: %s", (unsigned long)status_code, url);
        goto cleanup;
    }

    sink = fopen(destination, "wb");
    if (sink == NULL)
    {
        RMS_ERROR("cannot write %s: %s", destination, strerror(errno));
        goto cleanup;
    }

    for (;;)
    {
        DWORD available = 0;
        DWORD read_now = 0;

        if (!WinHttpQueryDataAvailable(request, &available))
            goto cleanup;

        if (available == 0)
            break;

        if (available > sizeof buffer)
            available = (DWORD)sizeof buffer;

        if (!WinHttpReadData(request, buffer, available, &read_now))
            goto cleanup;

        if (read_now == 0)
            break;

        if (fwrite(buffer, 1, read_now, sink) != read_now)
        {
            RMS_ERROR("short write to %s", destination);
            goto cleanup;
        }
    }

    result = 0;

cleanup:
    if (sink != NULL)
        fclose(sink);

    if (result != 0)
        remove(destination);

    if (request != NULL)
        WinHttpCloseHandle(request);
    if (connection != NULL)
        WinHttpCloseHandle(connection);
    if (session != NULL)
        WinHttpCloseHandle(session);

    return result;
}

#else /* Linux and macOS */

static int
http_download(const char *url, const char *destination)
{
    char command[RMS_MAX_LINE * 2];

    if (strncmp(url, "https://", 8) != 0)
    {
        RMS_ERROR("refusing a non-https download: %s", url);
        return -1;
    }

    /* Pinning the flags matters: --fail turns an HTTP error into an exit code
     * instead of a saved error page, and -L is required for the redirecting
     * shims the toolchain URLs sit behind. */
    if (snprintf(command, sizeof command,
                 "curl -fsSL --retry 3 --retry-delay 2 -o '%s' '%s'", destination, url)
        >= (int)sizeof command)
        return -1;

    if (run_shell(command, 1) == 0)
        return 0;

    if (snprintf(command, sizeof command,
                 "wget -q --tries=3 -O '%s' '%s'", destination, url) >= (int)sizeof command)
        return -1;

    if (run_shell(command, 1) == 0)
        return 0;

    RMS_ERROR("neither curl nor wget could fetch %s", url);
    return -1;
}

#endif

/* ==========================================================================
 * ARCHIVE EXTRACTION
 *
 * The platform's own archive reader is used rather than a bundled one, and the
 * manifest declares how many leading path components an archive wraps its
 * payload in. Declaring the strip count rather than detecting it keeps the
 * extraction correct for archives with more than one top-level entry, where a
 * "lift the single directory" heuristic would silently do nothing.
 * ========================================================================== */

#if defined(RMS_WINDOWS)
static int flatten_single_directory(const char *directory);
#endif

static int
extract_archive(const char *archive, const char *destination, int strip)
{
    char   command[RMS_MAX_LINE * 2];
    size_t length = strlen(archive);

    if (rms_make_dirs(destination) != 0)
        return -1;

    if (length > 4 && strcmp(archive + length - 4, ".zip") == 0)
    {
#if defined(RMS_WINDOWS)
        if (snprintf(command, sizeof command,
                     "powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass "
                     "-Command \"Expand-Archive -Force -LiteralPath '%s' -DestinationPath '%s'\"",
                     archive, destination) >= (int)sizeof command)
            return -1;
#else
        if (snprintf(command, sizeof command,
                     "unzip -o -q '%s' -d '%s'", archive, destination) >= (int)sizeof command)
            return -1;
#endif
    }
    else if ((length > 4 && strcmp(archive + length - 4, ".tgz") == 0) ||
             (length > 7 && strcmp(archive + length - 7, ".tar.gz") == 0))
    {
        if (snprintf(command, sizeof command,
                     "tar -xzf '%s' -C '%s'%s", archive, destination,
                     strip > 0 ? " --strip-components=1" : "") >= (int)sizeof command)
            return -1;
    }
    else if (length > 7 && strcmp(archive + length - 7, ".tar.xz") == 0)
    {
        if (snprintf(command, sizeof command,
                     "tar -xJf '%s' -C '%s'%s", archive, destination,
                     strip > 0 ? " --strip-components=1" : "") >= (int)sizeof command)
            return -1;
    }
    else if (length > 4 && strcmp(archive + length - 4, ".pkg") == 0)
    {
        /* A macOS installer package is handed to the system installer rather
         * than unpacked. It is the only form Mono is published in upstream. */
        if (snprintf(command, sizeof command,
                     "installer -pkg '%s' -target /", archive) >= (int)sizeof command)
            return -1;

        if (run_shell(command, 0) != 0)
        {
            RMS_ERROR("the package installer refused %s", archive);
            return RMS_ERR_EXTRACT;
        }

        return RMS_OK;
    }
    else
    {
        RMS_ERROR("unsupported archive format: %s", archive);
        return -1;
    }

    if (run_shell(command, 1) != 0)
    {
        RMS_ERROR("the archive could not be extracted: %s", archive);
        return RMS_ERR_EXTRACT;
    }

#if defined(RMS_WINDOWS)
    if (strip > 0)
        flatten_single_directory(destination);
#endif

    return RMS_OK;
}

#if defined(RMS_WINDOWS)

/*
 * Lift a single wrapping directory up one level.
 *
 * Only the Windows .NET and Node archives need this: the .NET zip has its
 * payload at the root, while the Node zip wraps everything in a versioned
 * directory. On Linux and macOS the same archives are tars, which tar can
 * strip while extracting. Normalising here means the manifest can name one
 * installation directory per platform and the PATH entries never have to
 * encode an archive's internal layout.
 */
static int
flatten_single_directory(const char *directory)
{
    WIN32_FIND_DATAA found;
    HANDLE  listing;
    char    pattern[RMS_MAX_PATH];
    char    inner[RMS_MAX_PATH];
    char    entry[RMS_MAX_PATH];
    int     child_count = 0;

    if (rms_join(pattern, sizeof pattern, directory, "*") != 0)
        return -1;

    listing = FindFirstFileA(pattern, &found);
    if (listing == INVALID_HANDLE_VALUE)
        return 0;

    do
    {
        if (strcmp(found.cFileName, ".") == 0 || strcmp(found.cFileName, "..") == 0)
            continue;

        if (found.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)
        {
            child_count++;
            rms_copy(inner, sizeof inner, found.cFileName);
        }
        else
        {
            /* A file at the root means the archive was already flat. */
            FindClose(listing);
            return 0;
        }
    } while (FindNextFileA(listing, &found));

    FindClose(listing);

    if (child_count != 1)
        return 0;

    if (rms_join(entry, sizeof entry, directory, inner) != 0)
        return -1;

    {
        char command[RMS_MAX_LINE * 2];

        if (snprintf(command, sizeof command,
                     "cmd.exe /C move \"%s\" \"%s\" >nul 2>&1", entry, directory)
            >= (int)sizeof command)
            return -1;

        if (run_shell(command, 1) != 0)
        {
            /* A failed flatten is not fatal: the install directory still holds
             * the tool, just nested. The verification command in the manifest
             * is what decides whether the dependency is usable. */
            RMS_WARN("could not lift %s out of %s; the extracted layout will be used", inner, directory);
        }
    }

    return 0;
}

#endif /* RMS_WINDOWS */

/* ==========================================================================
 * MANIFEST READER
 *
 * The manifests are INI files, one section per entity. INI is chosen over a
 * fixed column layout because every manifest now carries per-platform values,
 * and a column layout would need a column per platform per attribute: cheap to
 * parse, impossible to read or extend. The parser below is deliberately small
 * and strict: it understands sections, key = value pairs, comments, and
 * nothing else, so a malformed manifest is reported rather than guessed at.
 * ========================================================================== */

#define RMS_MAX_SECTIONS 48

typedef struct
{
    char name[RMS_MAX_ID];
    char keys[RMS_MAX_KEYS][RMS_MAX_ID];
    char values[RMS_MAX_KEYS][RMS_MAX_VALUE];
    int  count;
    int  line;
} Section;

typedef struct
{
    Section sections[RMS_MAX_SECTIONS];
    int     count;
} Manifest;

static const char *
section_value(const Section *section, const char *key)
{
    int index;

    for (index = 0; index < section->count; index++)
    {
        if (strcmp(section->keys[index], key) == 0)
            return section->values[index];
    }

    return NULL;
}

/*
 * Read a value that a platform may or may not have, preferring the
 * platform-qualified key and falling back to the unqualified one. This is what
 * lets a manifest write `url = ...` once when all three platforms share a value
 * and `windows.url = ...` only where they differ.
 */
static const char *
platform_value(const Section *section, const char *attribute)
{
    char qualified[RMS_MAX_ID];
    const char *value;

    if (snprintf(qualified, sizeof qualified, "%s.%s", host_platform(), attribute)
        < (int)sizeof qualified)
    {
        value = section_value(section, qualified);
        if (value != NULL)
            return value;
    }

    return section_value(section, attribute);
}

static int
manifest_load(const char *path, Manifest *manifest)
{
    FILE *handle = fopen(path, "r");
    char  line[RMS_MAX_LINE];
    int   line_number = 0;
    Section *current = NULL;

    memset(manifest, 0, sizeof *manifest);

    if (handle == NULL)
    {
        RMS_ERROR("cannot read the manifest %s: %s", path, strerror(errno));
        return -1;
    }

    /*
     * Skip a UTF-8 byte order mark.
     *
     * A manifest edited on Windows very often carries one, and a byte order mark
     * is not whitespace, so without this the first key of the file gains three
     * invisible bytes and fails to match anything. The failure would then point
     * at a line that looks perfectly correct in an editor.
     */
    {
        unsigned char marker[3];

        if (fread(marker, 1, sizeof marker, handle) != sizeof marker ||
            marker[0] != 0xEF || marker[1] != 0xBB || marker[2] != 0xBF)
        {
            rewind(handle);
        }
    }

    while (fgets(line, sizeof line, handle) != NULL)
    {
        char *cursor = line;
        char *separator;
        size_t length;

        line_number++;

        while (length = strlen(line), length > 0 &&
               (line[length - 1] == '\n' || line[length - 1] == '\r'))
            line[--length] = '\0';

        while (*cursor != '\0' && isspace((unsigned char)*cursor))
            cursor++;

        if (*cursor == '\0' || *cursor == '#' || *cursor == ';')
            continue;

        if (*cursor == '[')
        {
            char *closing = strchr(cursor, ']');

            if (closing == NULL)
            {
                RMS_ERROR("%s:%d: a section header is missing its closing bracket", path, line_number);
                fclose(handle);
                return -1;
            }

            *closing = '\0';
            cursor++;

            if (manifest->count >= RMS_MAX_SECTIONS)
            {
                RMS_ERROR("%s:%d: more than %d sections", path, line_number, RMS_MAX_SECTIONS);
                fclose(handle);
                return -1;
            }

            current = &manifest->sections[manifest->count];
            memset(current, 0, sizeof *current);
            current->line = line_number;

            if (rms_copy(current->name, sizeof current->name, cursor) != 0)
            {
                fclose(handle);
                return -1;
            }

            manifest->count++;
            continue;
        }

        if (current == NULL)
        {
            RMS_ERROR("%s:%d: a key appears before any section header", path, line_number);
            fclose(handle);
            return -1;
        }

        separator = strchr(cursor, '=');
        if (separator == NULL)
        {
            RMS_ERROR("%s:%d: expected 'key = value'", path, line_number);
            fclose(handle);
            return -1;
        }

        *separator = '\0';

        {
            char *key = cursor;
            char *value = separator + 1;
            char *end;
            size_t key_length;

            end = key + strlen(key);
            while (end > key && isspace((unsigned char)*(end - 1)))
                *--end = '\0';
            while (*key != '\0' && isspace((unsigned char)*key))
                key++;

            while (*value != '\0' && isspace((unsigned char)*value))
                value++;

            end = value + strlen(value);
            while (end > value && isspace((unsigned char)*(end - 1)))
                *--end = '\0';

            key_length = strlen(key);

            if (key_length == 0)
            {
                RMS_ERROR("%s:%d: an empty key is not allowed", path, line_number);
                fclose(handle);
                return -1;
            }

            if (current->count >= RMS_MAX_KEYS)
            {
                RMS_ERROR("%s:%d: more than %d keys in section [%s]",
                          path, line_number, RMS_MAX_KEYS, current->name);
                fclose(handle);
                return -1;
            }

            if (rms_copy(current->keys[current->count], sizeof current->keys[0], key) != 0 ||
                rms_copy(current->values[current->count], sizeof current->values[0], value) != 0)
            {
                fclose(handle);
                return -1;
            }

            current->count++;
        }
    }

    fclose(handle);
    return 0;
}

/*
 * Parse a manifest into a heap allocation.
 *
 * A parsed manifest is a little over two megabytes, because each section
 * reserves room for its keys and values up front rather than allocating per
 * entry. That is deliberate: it keeps the parser a single pass with no
 * allocation on any field's path, which is what makes it easy to reason about.
 * The consequence is that the structure cannot be a local variable. The default
 * thread stack on Windows is one megabyte, so a two megabyte local overflows the
 * stack on the very first call -- which is exactly what happened when this was
 * first written, and why the allocation lives here instead.
 */
static Manifest *
manifest_open(const char *path)
{
    Manifest *manifest = (Manifest *)malloc(sizeof *manifest);

    if (manifest == NULL)
    {
        RMS_ERROR("out of memory while reading %s", path);
        return NULL;
    }

    if (manifest_load(path, manifest) != 0)
    {
        free(manifest);
        return NULL;
    }

    return manifest;
}

/* True when a comma or space separated list contains the given token. */
static int
list_contains(const char *list, const char *token)
{
    const char *cursor = list;
    size_t      token_length = strlen(token);

    if (list == NULL || list[0] == '\0')
        return 1;   /* Absent means "every platform". */

    while (*cursor != '\0')
    {
        while (*cursor == ',' || *cursor == ' ' || *cursor == '\t')
            cursor++;

        if (strncmp(cursor, token, token_length) == 0)
        {
            const char *after = cursor + token_length;

            if (*after == '\0' || *after == ',' || *after == ' ' || *after == '\t')
                return 1;
        }

        while (*cursor != '\0' && *cursor != ',' && *cursor != ' ')
            cursor++;
    }

    return 0;
}

/* ==========================================================================
 * REQUIREMENTS
 * ========================================================================== */

typedef struct
{
    char id[RMS_MAX_ID];
    char kind[RMS_MAX_KIND];        /* dotnet | node | msbuild | archive | package */
    char version[RMS_MAX_VERSION];
    char url[RMS_MAX_VALUE];        /* Empty for the package kind. */
    char install_command[RMS_MAX_VALUE]; /* Used by the package kind instead of a URL. */
    char install_dir[RMS_MAX_PATH];
    char verify[RMS_MAX_VALUE];
    char verify_tool[RMS_MAX_KIND];     /* Presence decided by the resolved toolchain. */
    char verify_path[RMS_MAX_VALUE];    /* Presence decided by semicolon separated paths. */
    char provides[RMS_MAX_PATH];    /* Directory, relative to the root, added to PATH once installed. */
    char silent_args[RMS_MAX_VALUE];
    char platforms[RMS_MAX_VALUE];  /* Which platforms the requirement applies to. */
    int  strip;
} Requirement;

typedef struct
{
    Requirement *items;
    size_t       count;
    size_t       capacity;
} RequirementList;

static int
requirements_load_sections(const Manifest *manifest, const char *path, RequirementList *list)
{
    int index;

    memset(list, 0, sizeof *list);

    for (index = 0; index < manifest->count; index++)
    {
        const Section *section = &manifest->sections[index];
        Requirement    entry;
        const char    *value;
        Requirement   *grown;

        memset(&entry, 0, sizeof entry);

        if (rms_copy(entry.id, sizeof entry.id, section->name) != 0)
            return -1;

        value = section_value(section, "kind");
        if (value == NULL)
        {
            RMS_ERROR("%s:%d: [%s] has no kind", path, section->line, section->name);
            return -1;
        }

        if (rms_copy(entry.kind, sizeof entry.kind, value) != 0)
            return -1;

        {
            static const char *const known[] =
            {
                "dotnet", "node", "msbuild", "archive", "package", "installer", NULL
            };
            int known_index;
            int recognised = 0;

            for (known_index = 0; known[known_index] != NULL; known_index++)
            {
                if (strcmp(entry.kind, known[known_index]) == 0)
                {
                    recognised = 1;
                    break;
                }
            }

            if (!recognised)
            {
                RMS_ERROR("%s:%d: [%s] declares an unknown kind '%s'",
                          path, section->line, section->name, entry.kind);
                return -1;
            }
        }

        value = section_value(section, "version");
        if (value != NULL && rms_copy(entry.version, sizeof entry.version, value) != 0)
            return -1;

        value = platform_value(section, "url");
        if (value != NULL && rms_copy(entry.url, sizeof entry.url, value) != 0)
            return -1;

        value = platform_value(section, "install");
        if (value != NULL && rms_copy(entry.install_command, sizeof entry.install_command, value) != 0)
            return -1;

        value = platform_value(section, "install_dir");
        if (value != NULL && rms_copy(entry.install_dir, sizeof entry.install_dir, value) != 0)
            return -1;

        value = platform_value(section, "verify");
        if (value != NULL && rms_copy(entry.verify, sizeof entry.verify, value) != 0)
            return -1;

        value = platform_value(section, "verify_tool");
        if (value != NULL && rms_copy(entry.verify_tool, sizeof entry.verify_tool, value) != 0)
            return -1;

        value = platform_value(section, "verify_path");
        if (value != NULL && rms_copy(entry.verify_path, sizeof entry.verify_path, value) != 0)
            return -1;

        /*
         * `provides` is read before the block below, not after it.
         *
         * It used to be read afterwards, which meant the value was expanded and had its separators
         * normalised while it was still empty and then overwritten by the raw manifest text. The
         * consequence was that a `provides` naming %ROOT% was handed to the PATH as the literal
         * text "%ROOT%/.tools/dotnet", and one written with the other platform's separator was
         * handed over unconverted. Neither shows up while every value happens to be a relative path
         * written with forward slashes, which is exactly why it survived this long.
         */
        value = section_value(section, "provides");
        if (value != NULL && rms_copy(entry.provides, sizeof entry.provides, value) != 0)
            return -1;

        /* A path may name the repository root or an environment variable, the
         * same as any other manifest value. */
        if (expand_value(entry.verify_path, sizeof entry.verify_path) != 0 ||
            expand_value(entry.install_dir, sizeof entry.install_dir) != 0 ||
            expand_value(entry.provides, sizeof entry.provides) != 0)
            return -1;

        normalise_separators(entry.install_dir);
        normalise_separators(entry.provides);

        value = platform_value(section, "silent_args");
        if (value != NULL && rms_copy(entry.silent_args, sizeof entry.silent_args, value) != 0)
            return -1;

        value = section_value(section, "platforms");
        if (value != NULL && rms_copy(entry.platforms, sizeof entry.platforms, value) != 0)
            return -1;

        value = platform_value(section, "strip");
        entry.strip = value != NULL ? atoi(value) : 0;

        /*
         * A requirement with neither a URL nor an install command cannot be
         * satisfied automatically. That is a legitimate state, so it is
         * recorded as such rather than rejected: the toolchain detection may
         * still find the tool already present on the system path.
         */
        if (list->count == list->capacity)
        {
            size_t next = list->capacity == 0 ? 8 : list->capacity * 2;

            grown = (Requirement *)realloc(list->items, next * sizeof(Requirement));
            if (grown == NULL)
            {
                RMS_ERROR("out of memory while reading %s", path);
                return -1;
            }

            list->items = grown;
            list->capacity = next;
        }

        list->items[list->count++] = entry;
    }

    return 0;
}

/*
 * The public entry point. The manifest is opened and released around the parse,
 * so every early return inside the section loop is a plain `return -1` and the
 * allocation is released exactly once, on both paths.
 */
static int
requirements_load(const char *path, RequirementList *list)
{
    Manifest *manifest = manifest_open(path);
    int       result;

    if (manifest == NULL)
        return -1;

    result = requirements_load_sections(manifest, path, list);
    free(manifest);

    if (result != 0)
    {
        free(list->items);
        memset(list, 0, sizeof *list);
    }

    return result;
}

/* ==========================================================================
 * BUILD STEPS
 * ========================================================================== */

typedef struct
{
    char id[RMS_MAX_ID];
    char kind[RMS_MAX_KIND];
    char path[RMS_MAX_PATH];
    char configuration[RMS_MAX_KIND];
    char platforms[RMS_MAX_VALUE];
    char note[RMS_MAX_NOTE];
} BuildStep;

typedef struct
{
    BuildStep *items;
    size_t     count;
    size_t     capacity;
} BuildStepList;

static int
build_steps_load_sections(const Manifest *manifest, const char *path, BuildStepList *list)
{
    int index;

    memset(list, 0, sizeof *list);

    for (index = 0; index < manifest->count; index++)
    {
        const Section *section = &manifest->sections[index];
        BuildStep      entry;
        const char    *value;
        BuildStep     *grown;

        memset(&entry, 0, sizeof entry);

        if (rms_copy(entry.id, sizeof entry.id, section->name) != 0)
            return -1;

        value = section_value(section, "kind");
        if (value == NULL)
        {
            RMS_ERROR("%s:%d: [%s] has no kind", path, section->line, section->name);
            return -1;
        }

        if (rms_copy(entry.kind, sizeof entry.kind, value) != 0)
            return -1;

        value = section_value(section, "path");
        if (value != NULL && rms_copy(entry.path, sizeof entry.path, value) != 0)
            return -1;

        value = section_value(section, "config");
        if (value != NULL && rms_copy(entry.configuration, sizeof entry.configuration, value) != 0)
            return -1;

        value = section_value(section, "platforms");
        if (value != NULL && rms_copy(entry.platforms, sizeof entry.platforms, value) != 0)
            return -1;

        value = section_value(section, "note");
        if (value != NULL && rms_copy(entry.note, sizeof entry.note, value) != 0)
            return -1;

        if (list->count == list->capacity)
        {
            size_t next = list->capacity == 0 ? 8 : list->capacity * 2;

            grown = (BuildStep *)realloc(list->items, next * sizeof(BuildStep));
            if (grown == NULL)
            {
                RMS_ERROR("out of memory while reading %s", path);
                return -1;
            }

            list->items = grown;
            list->capacity = next;
        }

        list->items[list->count++] = entry;
    }

    return 0;
}

static int
build_steps_load(const char *path, BuildStepList *list)
{
    Manifest *manifest = manifest_open(path);
    int       result;

    if (manifest == NULL)
        return -1;

    result = build_steps_load_sections(manifest, path, list);
    free(manifest);

    if (result != 0)
    {
        free(list->items);
        memset(list, 0, sizeof *list);
    }

    return result;
}

/* ==========================================================================
 * SERVICE DEFINITIONS
 * ========================================================================== */

typedef struct
{
    char id[RMS_MAX_ID];
    char name[RMS_MAX_ID];
    char description[RMS_MAX_NOTE * 4];
    char executable[RMS_MAX_PATH];
    char arguments[RMS_MAX_VALUE];
    char working_directory[RMS_MAX_PATH];
    char restart[RMS_MAX_KIND];         /* always | on-failure | never; read by the supervisor. */
    char restart_delay[RMS_MAX_ID];     /* Seconds before the first restart, as text. */
    char max_restart_delay[RMS_MAX_ID]; /* Ceiling the delay grows to, as text. */
    char stop_timeout[RMS_MAX_ID];      /* Seconds between a stop request and a kill, as text. */
    char shutdown_line[RMS_MAX_VALUE];  /* Written to the child's input to ask it to stop. */
    char user[RMS_MAX_ID];
    char environment[RMS_MAX_VALUE];
    char platforms[RMS_MAX_VALUE];
    char website[RMS_MAX_PATH];         /* Set for the Web Forms sites, which the platform serves rather than runs. */
    char port[RMS_MAX_ID];
} ServiceDefinition;

typedef struct
{
    ServiceDefinition *items;
    size_t             count;
    size_t             capacity;
} ServiceList;

static int
services_load_sections(const Manifest *manifest, const char *path, ServiceList *list)
{
    int index;

    memset(list, 0, sizeof *list);

    for (index = 0; index < manifest->count; index++)
    {
        const Section *section = &manifest->sections[index];
        ServiceDefinition entry;
        const char *value;
        ServiceDefinition *grown;

        memset(&entry, 0, sizeof entry);

        if (rms_copy(entry.id, sizeof entry.id, section->name) != 0)
            return -1;

        value = section_value(section, "name");
        rms_copy(entry.name, sizeof entry.name, value != NULL ? value : section->name);

        value = section_value(section, "description");
        if (value != NULL && rms_copy(entry.description, sizeof entry.description, value) != 0)
            return -1;

        value = platform_value(section, "executable");
        if (value != NULL && rms_copy(entry.executable, sizeof entry.executable, value) != 0)
            return -1;

        value = platform_value(section, "arguments");
        if (value != NULL && rms_copy(entry.arguments, sizeof entry.arguments, value) != 0)
            return -1;

        value = platform_value(section, "working_dir");
        if (value != NULL && rms_copy(entry.working_directory, sizeof entry.working_directory, value) != 0)
            return -1;

        value = platform_value(section, "restart");
        if (value != NULL && rms_copy(entry.restart, sizeof entry.restart, value) != 0)
            return -1;

        value = platform_value(section, "restart_delay");
        if (value != NULL && rms_copy(entry.restart_delay, sizeof entry.restart_delay, value) != 0)
            return -1;

        value = platform_value(section, "max_restart_delay");
        if (value != NULL && rms_copy(entry.max_restart_delay, sizeof entry.max_restart_delay, value) != 0)
            return -1;

        value = platform_value(section, "stop_timeout");
        if (value != NULL && rms_copy(entry.stop_timeout, sizeof entry.stop_timeout, value) != 0)
            return -1;

        /* The shutdown line is expanded but not normalised: it is text written
         * to a program's input, not a path. */
        value = platform_value(section, "shutdown_line");
        if (value != NULL && rms_copy(entry.shutdown_line, sizeof entry.shutdown_line, value) != 0)
            return -1;

        value = section_value(section, "user");
        if (value != NULL && rms_copy(entry.user, sizeof entry.user, value) != 0)
            return -1;

        value = platform_value(section, "environment");
        if (value != NULL && rms_copy(entry.environment, sizeof entry.environment, value) != 0)
            return -1;

        value = section_value(section, "platforms");
        if (value != NULL && rms_copy(entry.platforms, sizeof entry.platforms, value) != 0)
            return -1;

        value = platform_value(section, "website");
        if (value != NULL && rms_copy(entry.website, sizeof entry.website, value) != 0)
            return -1;

        value = platform_value(section, "port");
        if (value != NULL && rms_copy(entry.port, sizeof entry.port, value) != 0)
            return -1;

        /*
         * Paths are resolved once, here, rather than at each use, so that every
         * consumer sees identical absolute values: the plan writer below, an
         * operator reading the generated plan, and the service installer that
         * turns it into a definition for this platform's own service manager.
         */
        if (expand_value(entry.executable, sizeof entry.executable) != 0 ||
            expand_value(entry.arguments, sizeof entry.arguments) != 0 ||
            expand_value(entry.environment, sizeof entry.environment) != 0 ||
            expand_value(entry.shutdown_line, sizeof entry.shutdown_line) != 0 ||
            expand_value(entry.website, sizeof entry.website) != 0)
            return -1;

        /* Only the fields that are filesystem paths get their separators
         * normalised; arguments and the environment list may contain URLs. */
        normalise_separators(entry.executable);
        normalise_separators(entry.website);

        if (entry.working_directory[0] == '\0')
        {
            /* A process started without a working directory inherits one the
             * operator did not choose, which is how a service ends up reading a
             * configuration file belonging to somebody else. */
            if (rms_copy(entry.working_directory, sizeof entry.working_directory, g_opts.root) != 0)
                return -1;
        }
        else if (expand_value(entry.working_directory, sizeof entry.working_directory) != 0)
        {
            return -1;
        }

        normalise_separators(entry.working_directory);

        if (list->count == list->capacity)
        {
            size_t next = list->capacity == 0 ? 8 : list->capacity * 2;

            grown = (ServiceDefinition *)realloc(list->items, next * sizeof(ServiceDefinition));
            if (grown == NULL)
            {
                RMS_ERROR("out of memory while reading %s", path);
                return -1;
            }

            list->items = grown;
            list->capacity = next;
        }

        list->items[list->count++] = entry;
    }

    return 0;
}

static int
services_load(const char *path, ServiceList *list)
{
    Manifest *manifest = manifest_open(path);
    int       result;

    if (manifest == NULL)
        return -1;

    result = services_load_sections(manifest, path, list);
    free(manifest);

    if (result != 0)
    {
        free(list->items);
        memset(list, 0, sizeof *list);
    }

    return result;
}

/* ==========================================================================
 * TOOLCHAIN RESOLUTION
 *
 * A tool is looked for in this order: the RMS tools directory, then the
 * well-known installation locations for the platform, then the system path.
 * Looking in the tools directory first is what makes a deployment reproducible:
 * once the bootstrapper has installed a toolchain, every later run uses that
 * copy rather than whatever happens to be on the machine.
 * ========================================================================== */

typedef struct
{
    char dotnet[RMS_MAX_PATH];
    char node[RMS_MAX_PATH];
    char npm[RMS_MAX_PATH];
    char msbuild[RMS_MAX_PATH];
    char vstools[RMS_MAX_PATH];
    char xsp[RMS_MAX_PATH];
    char dotnet_root[RMS_MAX_PATH];
} Toolchain;

/*
 * The resolved toolchain is process-wide rather than passed down the call
 * chain. Three commands need it, two of them only to forward it, and a single
 * global also lets the toolchain be re-resolved in place after a bootstrap
 * installs something, which is what stops the first run on a fresh machine from
 * installing MSBuild and then skipping the projects that need it.
 */
static Toolchain g_tools;

#if defined(RMS_WINDOWS)
static const char *const k_dotnet_names[]  = { "dotnet.exe", NULL };
static const char *const k_node_names[]    = { "node.exe", NULL };
static const char *const k_npm_names[]     = { "npm.cmd", "npm.bat", NULL };
static const char *const k_msbuild_names[] = { "MSBuild.exe", NULL };
static const char *const k_xsp_names[]     = { NULL };
#else
static const char *const k_dotnet_names[]  = { "dotnet", NULL };
static const char *const k_node_names[]    = { "node", NULL };
static const char *const k_npm_names[]     = { "npm", NULL };
static const char *const k_msbuild_names[] = { "msbuild", NULL };
static const char *const k_xsp_names[]     = { "xsp4", "xsp", NULL };
#endif

/* Absolute locations a toolchain is installed to by its own installer, which
 * the system path does not necessarily reflect at the time the deployer runs. */
#if defined(RMS_MACOS)
static const char *const k_dotnet_roots[] =
{
    "/usr/local/share/dotnet/dotnet", "/opt/homebrew/share/dotnet/dotnet", NULL
};
static const char *const k_mono_roots[] =
{
    "/Library/Frameworks/Mono.framework/Versions/Current/bin/msbuild", NULL
};
static const char *const k_xsp_roots[] =
{
    "/Library/Frameworks/Mono.framework/Versions/Current/bin/xsp4", NULL
};
#elif defined(RMS_POSIX)
static const char *const k_dotnet_roots[] =
{
    "/usr/share/dotnet/dotnet", "/usr/lib/dotnet/dotnet", "/usr/local/share/dotnet/dotnet", NULL
};
static const char *const k_mono_roots[] = { "/usr/bin/msbuild", "/usr/bin/xbuild", NULL };
static const char *const k_xsp_roots[]  = { "/usr/bin/xsp4", "/usr/lib/mono/xsp4/xsp4.exe", NULL };
#else
/* Windows installs neither Mono nor xsp, so only the fallback lists exist. */
static const char *const k_dotnet_roots[] = { NULL };
static const char *const k_xsp_roots[]    = { NULL };
#endif

/*
 * Locate a program on the system path.
 *
 * The verification commands in the requirements manifest run through a shell,
 * which resolves a bare program name against PATH for us. Nowhere else can do
 * that: every build step starts a program directly, and a bare name is only a
 * valid path when the file happens to be in the working directory. Resolving
 * PATH here is what keeps the two in agreement -- without it, a machine with a
 * perfectly good SDK on its path was reported as missing one, and then the build
 * steps refused to run for a tool that was installed.
 *
 * An empty path element is skipped rather than treated as the current
 * directory, because a toolchain that is only reachable by accident of the
 * working directory is not one to build with.
 */
static int
find_on_path(const char *name, char *result, size_t size)
{
    const char *path = getenv("PATH");
    const char *cursor;
    size_t      name_length = strlen(name);

    if (path == NULL)
        return 0;

    cursor = path;

    for (;;)
    {
        const char *separator = strchr(cursor, RMS_PATH_LIST);
        size_t      length = separator != NULL ? (size_t)(separator - cursor) : strlen(cursor);

        /* On Windows a path element conventionally ends with the separator, so
         * trim it before joining or the result carries a doubled one. */
        while (length > 0 && (cursor[length - 1] == RMS_SEP || cursor[length - 1] == '/'))
            length--;

        if (length > 0 && length + name_length + 2 <= RMS_MAX_PATH)
        {
            char candidate[RMS_MAX_PATH];

            memcpy(candidate, cursor, length);
            candidate[length] = RMS_SEP;
            memcpy(candidate + length + 1, name, name_length + 1);

            if (rms_file_exists(candidate))
            {
                rms_copy(result, size, candidate);
                return 1;
            }
        }

        if (separator == NULL)
            return 0;

        cursor = separator + 1;
    }
}

/*
 * Find the first existing program among a set of candidates.
 *
 * Three tiers, tried in order: the explicit directory if one was given, the
 * well-known absolute locations for this platform, and then the system path.
 * The caller uses the first tier to prefer a privately installed toolchain and
 * an empty directory to express "anywhere on this machine".
 */
static int
first_existing(const char *directory, const char *const *candidates, const char *const *absolute,
               char *result, size_t size)
{
    size_t index;

    if (directory != NULL && directory[0] != '\0')
    {
        for (index = 0; candidates[index] != NULL; index++)
        {
            char candidate[RMS_MAX_PATH];

            if (rms_join(candidate, sizeof candidate, directory, candidates[index]) != 0)
                continue;

            if (rms_file_exists(candidate))
            {
                rms_copy(result, size, candidate);
                return 1;
            }
        }

        return 0;
    }

    if (absolute != NULL)
    {
        for (index = 0; absolute[index] != NULL; index++)
        {
            if (rms_file_exists(absolute[index]))
            {
                rms_copy(result, size, absolute[index]);
                return 1;
            }
        }
    }

    for (index = 0; candidates[index] != NULL; index++)
    {
        if (find_on_path(candidates[index], result, size))
            return 1;
    }

    return 0;
}

#if defined(RMS_WINDOWS)

/*
 * Locate the MSBuild that can compile .NET Framework projects.
 *
 * A .NET Framework web project imports $(VSToolsPath)\WebApplications\Microsoft.WebApplication.targets,
 * and that directory exists only in an installation that carries the web build workload. Finding an
 * MSBuild is therefore not enough. The Build Tools installation without that workload compiles every
 * project in this stack except the four Web Forms sites and fails on those with MSB4226, an import of a
 * path that is not there -- so the search below records whether an installation can build a web project
 * at all, and prefers one that can. A machine with no such installation still builds the services, and
 * the warning says precisely which piece is missing.
 *
 * The version directory inside each installation, and the installations themselves, are discovered by
 * listing rather than by naming a year. The previous revision scanned for "2022" under each Program Files
 * root, which finds nothing at all on a machine whose only Visual Studio is a different release, even
 * though that release is perfectly able to build this stack. A list of known years goes stale in exactly
 * the same way, so nothing here enumerates one.
 */
static int
find_web_application_targets(const char *install, char *result, size_t size)
{
    WIN32_FIND_DATAA found;
    HANDLE  listing;
    char    pattern[RMS_MAX_PATH];
    char    directory[RMS_MAX_PATH];
    char    targets[RMS_MAX_PATH];

    if (snprintf(pattern, sizeof pattern, "%s\\MSBuild\\Microsoft\\VisualStudio\\*", install)
        >= (int)sizeof pattern)
        return 0;

    listing = FindFirstFileA(pattern, &found);
    if (listing == INVALID_HANDLE_VALUE)
        return 0;

    do
    {
        if (!(found.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY))
            continue;

        if (strcmp(found.cFileName, ".") == 0 || strcmp(found.cFileName, "..") == 0)
            continue;

        if (snprintf(directory, sizeof directory, "%s\\MSBuild\\Microsoft\\VisualStudio\\%s",
                     install, found.cFileName) >= (int)sizeof directory)
            continue;

        if (snprintf(targets, sizeof targets, "%s\\WebApplications\\Microsoft.WebApplication.targets",
                     directory) >= (int)sizeof targets)
            continue;

        if (!rms_file_exists(targets))
            continue;

        /* The directory holding WebApplications is what the import calls VSToolsPath. */
        FindClose(listing);
        return rms_copy(result, size, directory) == 0;
    } while (FindNextFileA(listing, &found));

    FindClose(listing);
    return 0;
}

static int
find_framework_msbuild(char *executable, size_t executable_size, char *vstools, size_t vstools_size)
{
    static const char *const editions[] =
    {
        "Enterprise", "Professional", "Community", "BuildTools", "Preview", NULL
    };
    const char *roots[2];
    int         root_index;
    int         edition_index;
    int         fallback_found = 0;
    char        fallback[RMS_MAX_PATH];

    roots[0] = getenv("ProgramFiles");
    roots[1] = getenv("ProgramFiles(x86)");

    vstools[0] = '\0';

    for (root_index = 0; root_index < 2; root_index++)
    {
        WIN32_FIND_DATAA found;
        HANDLE  listing;
        char    pattern[RMS_MAX_PATH];

        if (roots[root_index] == NULL || roots[root_index][0] == '\0')
            continue;

        if (snprintf(pattern, sizeof pattern, "%s\\Microsoft Visual Studio\\*", roots[root_index])
            >= (int)sizeof pattern)
            continue;

        listing = FindFirstFileA(pattern, &found);
        if (listing == INVALID_HANDLE_VALUE)
            continue;

        do
        {
            if (!(found.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY))
                continue;

            if (strcmp(found.cFileName, ".") == 0 || strcmp(found.cFileName, "..") == 0)
                continue;

            for (edition_index = 0; editions[edition_index] != NULL; edition_index++)
            {
                char candidate[RMS_MAX_PATH];
                char install[RMS_MAX_PATH];
                char targets[RMS_MAX_PATH];

                if (snprintf(install, sizeof install, "%s\\Microsoft Visual Studio\\%s\\%s",
                             roots[root_index], found.cFileName, editions[edition_index])
                    >= (int)sizeof install)
                    continue;

                if (snprintf(candidate, sizeof candidate, "%s\\MSBuild\\Current\\Bin\\MSBuild.exe", install)
                    >= (int)sizeof candidate)
                    continue;

                if (!rms_file_exists(candidate))
                    continue;

                if (find_web_application_targets(install, targets, sizeof targets))
                {
                    rms_copy(executable, executable_size, candidate);
                    rms_copy(vstools, vstools_size, targets);
                    FindClose(listing);
                    return 1;
                }

                /* Recorded, not returned. A later installation may turn out to be the one that can
                 * build a web project, and preferring the first MSBuild found would pick whichever
                 * release happened to sort first rather than the one that does the job. */
                if (!fallback_found)
                {
                    rms_copy(fallback, sizeof fallback, candidate);
                    fallback_found = 1;
                }
            }
        } while (FindNextFileA(listing, &found));

        FindClose(listing);
    }

    if (fallback_found)
    {
        rms_copy(executable, executable_size, fallback);
        return 1;
    }

    return first_existing(NULL, k_msbuild_names, NULL, executable, executable_size);
}

#else

/*
 * Locate the MSBuild that can compile .NET Framework projects, on Linux and macOS.
 *
 * Mono supplies it, and Mono's MSBuild compiles a Web Forms project without the separate targets that
 * Windows needs, so there is no equivalent of the capability check above: the installation either
 * includes MSBuild or it does not. The absolute locations in k_mono_roots cover the standard installs.
 */
static int
find_framework_msbuild(char *executable, size_t executable_size, char *vstools, size_t vstools_size)
{
    (void)vstools_size;

    vstools[0] = '\0';

    return first_existing(NULL, k_msbuild_names, k_mono_roots, executable, executable_size);
}

#endif

static void
resolve_toolchain(const RequirementList *requirements)
{
    size_t index;
    Toolchain *toolchain = &g_tools;

    memset(toolchain, 0, sizeof *toolchain);

    /* The tools directory first: an installed copy always beats a system one. */
    for (index = 0; index < requirements->count; index++)
    {
        const Requirement *requirement = &requirements->items[index];
        char directory[RMS_MAX_PATH];

        if (requirement->provides[0] == '\0')
            continue;

        if (rms_join(directory, sizeof directory, g_opts.root, requirement->provides) != 0)
            continue;

        if (strcmp(requirement->kind, "dotnet") == 0 && toolchain->dotnet[0] == '\0')
        {
            if (first_existing(directory, k_dotnet_names, NULL, toolchain->dotnet, sizeof toolchain->dotnet))
                rms_copy(toolchain->dotnet_root, sizeof toolchain->dotnet_root, directory);
        }
        else if (strcmp(requirement->kind, "node") == 0 && toolchain->node[0] == '\0')
        {
            first_existing(directory, k_node_names, NULL, toolchain->node, sizeof toolchain->node);
            first_existing(directory, k_npm_names, NULL, toolchain->npm, sizeof toolchain->npm);
        }
    }

    /* Then the well-known install locations, then the system path, so a machine
     * that already has a global SDK works without installing a second copy. */
    if (toolchain->dotnet[0] == '\0')
        first_existing(NULL, k_dotnet_names, k_dotnet_roots, toolchain->dotnet, sizeof toolchain->dotnet);

    if (toolchain->node[0] == '\0')
        first_existing(NULL, k_node_names, NULL, toolchain->node, sizeof toolchain->node);

    if (toolchain->npm[0] == '\0')
        first_existing(NULL, k_npm_names, NULL, toolchain->npm, sizeof toolchain->npm);

    if (toolchain->msbuild[0] == '\0')
        find_framework_msbuild(toolchain->msbuild, sizeof toolchain->msbuild,
                               toolchain->vstools, sizeof toolchain->vstools);

    if (toolchain->xsp[0] == '\0')
        first_existing(NULL, k_xsp_names, k_xsp_roots, toolchain->xsp, sizeof toolchain->xsp);

    if (toolchain->dotnet_root[0] == '\0' && toolchain->dotnet[0] != '\0')
    {
        /* Derive the root from the executable so DOTNET_ROOT is always set for
         * a child process, including when the SDK came from the system path. */
        char scratch[RMS_MAX_PATH];
        char *separator;

        rms_copy(scratch, sizeof scratch, toolchain->dotnet);
        separator = strrchr(scratch, RMS_SEP);

        if (separator != NULL)
        {
            *separator = '\0';
            rms_copy(toolchain->dotnet_root, sizeof toolchain->dotnet_root, scratch);
        }
    }
}

/* ==========================================================================
 * CHILD ENVIRONMENT
 *
 * Every child process is given an environment that makes the toolchain
 * deterministic: the tools directory is prepended to PATH, telemetry and the
 * first-run banner are disabled, and the NuGet cache is pinned inside the tools
 * directory so a deployment does not depend on a per-user cache that may not
 * exist for the service account that later runs the build.
 * ========================================================================== */

static int
set_environment(const char *name, const char *value)
{
    if (value == NULL || value[0] == '\0')
        return 0;

#if defined(RMS_WINDOWS)
    return _putenv_s(name, value);
#else
    return setenv(name, value, 1);
#endif
}

static int
prepend_to_path(const char *directory)
{
    const char *existing = getenv("PATH");
    char        combined[RMS_MAX_LINE * 2];

    if (directory == NULL || directory[0] == '\0')
        return 0;

    if (snprintf(combined, sizeof combined, "%s%c%s",
                 directory, RMS_PATH_LIST, existing != NULL ? existing : "") >= (int)sizeof combined)
        return -1;

    return set_environment("PATH", combined);
}

static int
prepare_environment(void)
{
    char node_bin[RMS_MAX_PATH];

    if (rms_make_dirs(g_opts.tools) != 0)
        return -1;

    set_environment("DOTNET_CLI_TELEMETRY_OPTOUT", "1");
    set_environment("DOTNET_NOLOGO", "1");
    set_environment("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1");
    set_environment("NUGET_PACKAGES", g_opts.tools);

    /*
     * A published .NET program finds its runtime through DOTNET_ROOT when that
     * runtime was installed by this deployer, because such an installation is
     * not registered with the operating system and cannot be found any other
     * way. Setting it here covers the installer this program launches; the
     * services it registers get the same value written into their plans.
     */
    set_environment("DOTNET_ROOT", g_tools.dotnet_root);

    prepend_to_path(g_tools.dotnet_root);
    prepend_to_path(g_opts.tools);

    if (rms_join(node_bin, sizeof node_bin, g_opts.tools, "node") == 0)
        prepend_to_path(node_bin);

    return 0;
}

/* ==========================================================================
 * DEPENDENCY INSTALLATION
 * ========================================================================== */

/*
 * Decide whether a requirement is already satisfied.
 *
 * Three kinds of check, in the order they are preferred:
 *
 *   verify_tool  Present when the deployer's own toolchain search has already
 *                resolved that tool. This is the strongest of the three because
 *                it is the very resolution the build steps will use, so a
 *                requirement can never be reported as satisfied at the same
 *                moment the step that needs it is told the tool is missing.
 *   verify_path  Present when any of the semicolon separated paths exists. For
 *                anything that installs into a known place but does not put
 *                itself on PATH, which is the normal behaviour of a toolchain
 *                installer and the reason a bare command name is a poor test.
 *   verify       A shell command, for anything with no marker file and no
 *                resolver entry.
 *
 * Neither of the first two starts a process, so an unsatisfied requirement is
 * discovered without any chance of a shell leaking an error message on a machine
 * where nothing is wrong.
 */
static int
requirement_satisfied(const Requirement *requirement)
{
    if (requirement->verify_tool[0] != '\0')
    {
        if (strcmp(requirement->verify_tool, "dotnet") == 0)
            return g_tools.dotnet[0] != '\0';
        if (strcmp(requirement->verify_tool, "node") == 0)
            return g_tools.node[0] != '\0';
        if (strcmp(requirement->verify_tool, "npm") == 0)
            return g_tools.npm[0] != '\0';
        if (strcmp(requirement->verify_tool, "msbuild") == 0)
        {
#if defined(RMS_WINDOWS)
            /*
             * On Windows the requirement is not "an MSBuild" but "an MSBuild that can compile a .NET
             * Framework web project", which is what the sites need and what the manifest's own comment
             * says. Checking only for the executable would report the requirement as satisfied on a
             * machine whose single installation lacks the web workload -- and then every site would fail
             * to build, with the failure blamed on the projects rather than on the missing workload. The
             * two answers come from the same search, so the requirement and the build cannot disagree.
             */
            return g_tools.msbuild[0] != '\0' && g_tools.vstools[0] != '\0';
#else
            /* Mono's MSBuild compiles a Web Forms project itself, so its presence is the whole test. */
            return g_tools.msbuild[0] != '\0';
#endif
        }
        if (strcmp(requirement->verify_tool, "xsp") == 0)
            return g_tools.xsp[0] != '\0';

        RMS_ERROR("%s declares an unknown verify_tool: %s",
                  requirement->id, requirement->verify_tool);
        return 0;
    }

    if (requirement->verify_path[0] != '\0')
    {
        const char *cursor = requirement->verify_path;

        for (;;)
        {
            const char *separator = strchr(cursor, ';');
            size_t      length = separator != NULL ? (size_t)(separator - cursor) : strlen(cursor);

            while (length > 0 && isspace((unsigned char)*cursor))
            {
                cursor++;
                length--;
            }

            while (length > 0 && isspace((unsigned char)cursor[length - 1]))
                length--;

            if (length > 0 && length < RMS_MAX_PATH)
            {
                char candidate[RMS_MAX_PATH];

                memcpy(candidate, cursor, length);
                candidate[length] = '\0';

                /* A directory counts as well as a file: a toolchain can be
                 * described by the executable or by the root it installs into. */
                if (rms_file_exists(candidate) || rms_dir_exists(candidate))
                    return 1;
            }

            if (separator == NULL)
                return 0;

            cursor = separator + 1;
        }
    }

    return requirement->verify[0] != '\0' && command_succeeds(requirement->verify);
}

static int
install_requirement(const Requirement *requirement)
{
    char        archive[RMS_MAX_PATH];
    char        install_path[RMS_MAX_PATH];
    char        cache_path[RMS_MAX_PATH];
    const char *file_name;

    if (requirement->url[0] == '\0' && requirement->install_command[0] == '\0')
    {
        RMS_WARN("%s has no automatic installation for this platform; "
                 "install it by hand and re-run", requirement->id);
        return RMS_OK;
    }

    /*
     * The package kind delegates to the platform's package manager. It exists
     * because Mono is not distributed as a portable archive on Linux, and
     * hand-rolling an apt or Homebrew invocation would also mean hand-rolling
     * the trust decisions that belong to those tools.
     */
    if (strcmp(requirement->kind, "package") == 0)
    {
        RMS_INFO("installing %s through the platform package manager", requirement->id);

        if (run_shell(requirement->install_command, 0) != 0)
        {
            RMS_ERROR("the package installation for %s failed", requirement->id);
            return RMS_ERR_TOOLCHAIN;
        }

        return RMS_OK;
    }

    if (rms_join(install_path, sizeof install_path, g_opts.root, requirement->install_dir) != 0)
        return -1;

    if (rms_join(cache_path, sizeof cache_path, g_opts.tools, "cache") != 0)
        return -1;

    if (rms_make_dirs(cache_path) != 0 || rms_make_dirs(install_path) != 0)
        return -1;

    file_name = strrchr(requirement->url, '/');
    file_name = file_name != NULL ? file_name + 1 : "download";

    if (rms_join(archive, sizeof archive, cache_path, file_name) != 0)
        return -1;

    RMS_INFO("downloading %s %s", requirement->id, requirement->version);

    if (http_download(requirement->url, archive) != 0)
    {
        /* A platform that publishes an install command as well as a URL gets
         * one more chance through the package manager. This is what lets the
         * same manifest work on a distribution whose archive layout differs. */
        if (requirement->install_command[0] != '\0')
        {
            RMS_WARN("the download failed; falling back to the platform package manager");

            if (run_shell(requirement->install_command, 0) == 0)
                return RMS_OK;
        }

        return RMS_ERR_DOWNLOAD;
    }

    /*
     * A self-installing executable is run rather than extracted. The arguments
     * come from the manifest so that an unattended install of a multi-gigabyte
     * toolchain is reviewable in the same place as everything else the deployer
     * does, instead of being hidden inside this program.
     */
    if (strcmp(requirement->kind, "installer") == 0)
    {
        char command[RMS_MAX_LINE * 2];
        int  exit_code;

        if (requirement->silent_args[0] == '\0')
        {
            RMS_ERROR("%s is an installer but declares no silent arguments", requirement->id);
            return RMS_ERR_USAGE;
        }

        if (snprintf(command, sizeof command, "\"%s\" %s", archive, requirement->silent_args)
            >= (int)sizeof command)
            return -1;

        RMS_INFO("running the %s installer; this can take several minutes", requirement->id);

        exit_code = run_shell(command, 0);

        /* Installers conventionally report 0 for success and 3010 for success
         * with a pending reboot. Both mean the tool is now on the machine. */
        if (exit_code != 0 && exit_code != 3010)
        {
            RMS_ERROR("%s installer exited with %d", requirement->id, exit_code);
            return RMS_ERR_TOOLCHAIN;
        }

        return RMS_OK;
    }

    RMS_INFO("extracting %s", requirement->id);

    if (extract_archive(archive, install_path, requirement->strip) != RMS_OK)
        return RMS_ERR_EXTRACT;

    if (!g_opts.keep_cache)
        remove(archive);

    return RMS_OK;
}

/* ==========================================================================
 * BUILD STEPS
 *
 * The kind decides how a step is executed:
 *   dotnet-solution  restore then build a solution with the SDK.
 *   dotnet-project   restore then build a single project with the SDK.
 *   framework        build a solution with the framework MSBuild, because the
 *                    project targets .NET Framework and the SDK cannot.
 *   node-package     install and build a package that emits static assets.
 *   shell            run the command verbatim, for the steps that do not fit
 *                    the other kinds. Deliberately explicit so that review can
 *                    see exactly which steps are not portable.
 * ========================================================================== */

static int
run_build_step(const BuildStep *step)
{
    char path[RMS_MAX_PATH];

    if (rms_join(path, sizeof path, g_opts.root, step->path) != 0)
        return -1;

    if (strcmp(step->kind, "dotnet-solution") == 0 || strcmp(step->kind, "dotnet-project") == 0)
    {
        /*
         * A step that does not name a configuration is built for release, which is the same rule the
         * framework path below applies. Passing an empty configuration on instead would let dotnet fall
         * back to its own default, which is Debug: a deployment would then ship unoptimised assemblies
         * under a bin/Debug path, differing from the very projects built beside them, with nothing in the
         * manifest to say so. A build tool's default should not decide what a deployment contains.
         */
        const char *configuration =
            step->configuration[0] != '\0' ? step->configuration : "Release";

        char *restore[] = { g_tools.dotnet, (char *)"restore", path, (char *)"--nologo", NULL };
        char *build[] =
        {
            g_tools.dotnet, (char *)"build", path,
            (char *)"-c", (char *)configuration,
            (char *)"--nologo", (char *)"--no-restore", NULL
        };

        if (g_tools.dotnet[0] == '\0')
        {
            RMS_ERROR("%s needs the .NET SDK, which was not found", step->id);
            return RMS_ERR_TOOLCHAIN;
        }

        if (run_argv(restore) != 0)
        {
            RMS_ERROR("%s: restore failed", step->id);
            return RMS_ERR_BUILD;
        }

        if (run_argv(build) != 0)
        {
            RMS_ERROR("%s: build failed", step->id);
            return RMS_ERR_BUILD;
        }

        return RMS_OK;
    }

    if (strcmp(step->kind, "framework") == 0)
    {
        char configuration[RMS_MAX_KIND + 32];
        char vstools_argument[RMS_MAX_PATH + 16];
        char *build[10];
        int   argument = 0;

        if (g_tools.msbuild[0] == '\0')
        {
#if defined(RMS_WINDOWS)
            RMS_WARN("%s targets .NET Framework and needs the Visual Studio Build Tools; skipping", step->id);
#else
            RMS_WARN("%s targets .NET Framework and needs Mono's MSBuild; skipping", step->id);
#endif
            return RMS_OK;
        }

        if (snprintf(configuration, sizeof configuration, "/p:Configuration=%s",
                     step->configuration[0] != '\0' ? step->configuration : "Release")
            >= (int)sizeof configuration)
            return -1;

        build[argument++] = g_tools.msbuild;
        build[argument++] = path;
        build[argument++] = (char *)"/nologo";
        build[argument++] = (char *)"/verbosity:minimal";
        build[argument++] = (char *)"/m";

        /*
         * Restore before building.
         *
         * The .NET Framework projects in this repository use PackageReference, which is how the SQLite provider
         * and its native asset reach the output directory without a checked-in binary. A package reference is
         * only resolved by a restore, and a framework build that skipped it would compile against whatever
         * happened to be in the package cache on the machine doing the build -- which is the difference
         * between a deployment that works on the first machine and one that only works on ours.
         */
        build[argument++] = (char *)"/restore";
        build[argument++] = configuration;

        /*
         * The web targets belong to one installation, and the import inside a site project reads them
         * through $(VSToolsPath), which MSBuild defaults to a path derived from the MSBuild it is running
         * as. That default is right when the MSBuild in use is the one that owns the targets and wrong
         * whenever it is not -- and "not" is common, because a machine can hold several installations and
         * the SDK's MSBuild resolves as readily as the Visual Studio one. Passing the discovered directory
         * removes the guess: the project is told where the targets are, so it builds the same way whoever
         * launched it.
         *
         * The switch goes on the end, after the executable. run_argv hands the array to the platform as
         * it stands and the first element is the program, so an option that arrived before the executable
         * would be started as though it were one.
         */
        if (g_tools.vstools[0] != '\0')
        {
            if (snprintf(vstools_argument, sizeof vstools_argument, "/p:VSToolsPath=%s", g_tools.vstools)
                >= (int)sizeof vstools_argument)
                return -1;

            build[argument++] = vstools_argument;
        }
#if defined(RMS_WINDOWS)
        else
        {
            RMS_WARN("%s: no Visual Studio installation with the web build workload was found; "
                     "the web project will only build if the one in use carries it (MSB4226 otherwise)",
                     step->id);
        }
#endif

        build[argument] = NULL;

        if (run_argv(build) != 0)
        {
            RMS_ERROR("%s: build failed", step->id);
            return RMS_ERR_BUILD;
        }

        return RMS_OK;
    }

    if (strcmp(step->kind, "node-package") == 0)
    {
        char *install[] = { g_tools.npm, (char *)"install", (char *)"--no-audit", (char *)"--no-fund", NULL };
        char *build[] = { g_tools.npm, (char *)"run", (char *)"build", NULL };

        if (g_tools.npm[0] == '\0')
        {
            RMS_WARN("%s needs Node.js, which was not found; "
                     "the site will run with the committed bundles", step->id);
            return RMS_OK;
        }

        if (run_argv_in(path, install) != 0 || run_argv_in(path, build) != 0)
        {
            RMS_ERROR("%s: the asset build failed", step->id);
            return RMS_ERR_BUILD;
        }

        return RMS_OK;
    }

    if (strcmp(step->kind, "shell") == 0)
    {
        /*
         * A shell step is the escape hatch for anything the other kinds do not
         * cover. On Linux and macOS it is run by /bin/sh, which is guaranteed to
         * exist and can be relied on to understand a portable one-liner. On
         * Windows there is no /bin/sh, so the same string is handed to cmd; a
         * manifest that uses this kind for a cross-platform step is expected to
         * declare the platforms it applies to.
         *
         * The conditional is confined to producing one exit status. Letting a
         * #if span the braces would compile a different block structure on each
         * platform, which is exactly how a brace imbalance stays hidden until
         * the other platform builds the file.
         */
        int status;

#if defined(RMS_WINDOWS)
        status = run_shell(step->path, 0);
#else
        {
            char *arguments[] = { (char *)"/bin/sh", (char *)"-c", (char *)step->path, NULL };

            status = run_argv_in(g_opts.root, arguments);
        }
#endif

        if (status != 0)
        {
            RMS_ERROR("%s: the command failed", step->id);
            return RMS_ERR_BUILD;
        }

        return RMS_OK;
    }

    RMS_ERROR("%s: unknown build kind '%s'", step->id, step->kind);
    return RMS_ERR_USAGE;
}

/* ==========================================================================
 * PUBLISH
 *
 * Publishing is separated from building because the two have different
 * lifetimes: a build is per-source-revision, while a publish output is what a
 * deployment copies to another machine. Removing the destination first means an
 * output directory never accumulates the stale files of a previous layout.
 * ========================================================================== */

/* The name the environment template is given in the published directory. The
 * operator copies it to .env, so the two names differ by one suffix rather than
 * being spelled out in two unrelated places. */
#define RMS_ENVIRONMENT_TEMPLATE_NAME ".env.template"

/*
 * Copy a file byte for byte.
 *
 * Used for the environment template that ships beside the scanner. That
 * template is a file in the repository rather than a string in this program
 * because it documents a set of settings, and a deployment should carry the same
 * text a reader of the repository sees rather than a copy of it that was
 * correct when this file was last edited.
 */
static int
copy_file(const char *source, const char *destination)
{
    FILE  *in;
    FILE  *out;
    char   buffer[16384];
    size_t read_now;
    int    failed = 0;

    in = fopen(source, "rb");

    if (in == NULL)
    {
        RMS_ERROR("cannot read %s: %s", source, strerror(errno));
        return -1;
    }

    out = fopen(destination, "wb");

    if (out == NULL)
    {
        RMS_ERROR("cannot write %s: %s", destination, strerror(errno));
        fclose(in);
        return -1;
    }

    while ((read_now = fread(buffer, 1, sizeof buffer, in)) > 0)
    {
        if (fwrite(buffer, 1, read_now, out) != read_now)
        {
            failed = 1;
            break;
        }
    }

    /* A short read is either the end of the file or a failure, and the stream is
     * the only thing that can tell the two apart. Asking after the loop is also
     * what keeps the loop itself free of an error check per block. */
    if (ferror(in))
        failed = 1;

    if (fclose(out) != 0)
        failed = 1;

    fclose(in);

    if (failed)
    {
        RMS_ERROR("cannot copy %s to %s", source, destination);
        return -1;
    }

    return 0;
}

/*
 * The publish list is held in code rather than in a manifest. It is short, it
 * changes only when a project is added, and keeping it here means the
 * deployment output layout is reviewable in one place instead of being
 * assembled from a file that can drift away from the projects.
 *
 * The outputs are relative to the publish root, which is why they do not begin
 * with it. The paths the service manifest names are these directories under
 * <out>, and service_host_executable and service_installer_executable below have
 * to agree with them: the supervisor and the installer are published on every
 * platform, because every platform has a supervisor and something that registers
 * it.
 *
 * The last column is an environment template to place beside the published
 * program, or NULL for a program that needs none. A program that reaches a
 * third party on the operator's behalf needs a credential, and a credential is
 * the one thing a deployment cannot supply: there is no value it could write
 * that would be correct. It writes a template instead, and names it so that a
 * reader of the output directory finds it before wondering why the service
 * cannot fetch anything. The scanner is the only such program today.
 *
 * The column after that is the tool that publishes the project. "dotnet" is the
 * .NET SDK, which publishes everything that targets .NET. "framework" is MSBuild
 * with the web publishing pipeline, which is the only way to publish a .NET
 * Framework web project: the SDK does not build such a project at all, so
 * sending one through the .NET path is how a deployment ends up with no web root
 * behind a site that is registered and answering 404.
 */
static const struct
{
    const char *project;
    const char *output;
    const char *platforms;
    const char *template_file;
    const char *kind;
} k_publish_targets[] =
{
    { "src/RobloxModelScanner/RobloxModelScanner.csproj",                 "scanner",              "windows", "deploy/templates/scanner.env", "dotnet" },
    { "src/Scanner.MicroApi/Scanner.MicroApi.csproj",                     "api",                  NULL,      NULL,                           "dotnet" },
    { "src/Scanner.Indexer/Scanner.Indexer.csproj",                       "indexer",              NULL,      NULL,                           "dotnet" },
    { "src/RMS.Devops.Service/RMS.Devops.Service.csproj",                 "devops-service",       NULL,      NULL,                           "dotnet" },
    { "src/RMS.Configuration.Service/RMS.Configuration.Service.csproj",   "configuration-service", NULL,     NULL,                           "dotnet" },
    { "src/RMS.Docs.Service/RMS.Docs.Service.csproj",                     "docs-service",         NULL,      NULL,                           "dotnet" },
    { "src/RMS.ServiceHost/RMS.ServiceHost.csproj",                       "service-host",         NULL,      NULL,                           "dotnet" },
    { "src/RMS.ServiceInstaller/RMS.ServiceInstaller.csproj",             "service-installer",    NULL,      NULL,                           "dotnet" },

    /*
     * The four web consoles.
     *
     * They are here because being built is not being deployable: the build steps
     * in the build manifest leave assemblies in each project's own bin
     * directory. What the plan then registers -- an IIS site on Windows, an xsp
     * service everywhere else -- is pointed at a directory under the publish
     * root, and this is the step that creates it.
     *
     * They are published on every platform rather than on Windows alone,
     * because the machine that has no IIS is the machine the xsp service runs
     * on, and that service is handed the same published directory. On Linux and
     * macOS the MSBuild in use is Mono's, which carries its own web targets;
     * whether it carries the target used here has not been checked on the
     * machine this was written on. The direction a failure takes is the good
     * one: MSBuild reports the missing target and the publish step fails, rather
     * than a site coming up in front of an empty directory.
     */
    { "src/Scanner.WebForms/Scanner.WebForms.csproj",                     "site",                 NULL,      NULL,                           "framework" },
    { "src/RMS.Devops.Site/RMS.Devops.Site.csproj",                       "devops-site",          NULL,      NULL,                           "framework" },
    { "src/RMS.Configuration.Site/RMS.Configuration.Site.csproj",         "configuration-site",   NULL,      NULL,                           "framework" },
    { "src/RMS.Docs.Site/RMS.Docs.Site.csproj",                           "docs-site",            NULL,      NULL,                           "framework" },
    { NULL, NULL, NULL, NULL, NULL }
};

/*
 * Publish one project with the .NET SDK.
 *
 * The SDK is handed the project and a directory, and everything that belongs in
 * a deployment of that project is what it writes. It replaces the directory
 * rather than merging into it, which is why the environment template below is
 * written afterwards: written first it would be removed by the step it was meant
 * to accompany.
 */
static int
publish_dotnet_project(const char *project, const char *output, const char *template_file)
{
    char project_path[RMS_MAX_PATH];
    char output_path[RMS_MAX_PATH];

    if (g_tools.dotnet[0] == '\0')
    {
        RMS_ERROR("publishing needs the .NET SDK, which was not found");
        return RMS_ERR_TOOLCHAIN;
    }

    if (rms_join(project_path, sizeof project_path, g_opts.root, project) != 0 ||
        rms_join(output_path, sizeof output_path, g_opts.out, output) != 0)
        return -1;

    if (!rms_file_exists(project_path))
    {
        /* A target that has not been written yet is skipped rather than
         * failed, so the deployer can be exercised while the stack is still
         * being assembled. */
        RMS_WARN("%s has not been created yet; skipping its publish", project);
        return RMS_OK;
    }

    if (rms_make_dirs(output_path) != 0)
        return -1;

    {
        char *publish[] =
        {
            g_tools.dotnet, (char *)"publish", project_path,
            (char *)"-c", (char *)"Release",
            (char *)"-o", output_path,
            (char *)"--nologo", NULL
        };

        if (run_argv(publish) != 0)
        {
            RMS_ERROR("%s: publish failed", project);
            return RMS_ERR_BUILD;
        }
    }

    /*
     * The template is written after the publish rather than before it, because a
     * publish replaces the output directory. Written first it would be removed
     * by the step it was meant to accompany, and the failure would be an absent
     * file on a machine that has already been told the deployment succeeded.
     */
    if (template_file != NULL && template_file[0] != '\0')
    {
        char source[RMS_MAX_PATH];
        char destination[RMS_MAX_PATH];

        if (rms_join(source, sizeof source, g_opts.root, template_file) != 0 ||
            rms_join(destination, sizeof destination, output_path, RMS_ENVIRONMENT_TEMPLATE_NAME) != 0)
            return -1;

        if (copy_file(source, destination) != 0)
        {
            RMS_ERROR("%s: the environment template could not be placed", project);
            return RMS_ERR_BUILD;
        }
    }

    return RMS_OK;
}

/*
 * Publish one .NET Framework web project with MSBuild.
 *
 * The published form of a web project is not its build output. Building it puts
 * assemblies in the project's own bin directory; what a server behind a site
 * needs is one directory holding those assemblies, the pages that call them, the
 * configuration and the static assets, and nothing else. Which files those are is
 * knowledge the web publishing pipeline has and this file does not -- that a .cs
 * file is source, that obj is scratch, that a .ashx is something the server
 * executes and a .config is not served -- so the properties below point its
 * output at the deployment directory and it works the rest out.
 *
 * The directory is emptied first, because that pipeline copies and never
 * deletes. Without it, a page renamed since the previous publish is still there
 * under its old name, and still answers.
 *
 * MSBuild arrives through the same discovery the build steps use, so a machine
 * that can build the sites can publish them, and a project that was skipped by
 * the build because the toolchain is absent is skipped here for the same reason
 * rather than failing the deployment twice.
 */
static int
publish_framework_project(const char *project, const char *output)
{
    char  project_path[RMS_MAX_PATH];
    char  output_path[RMS_MAX_PATH];
    char  configuration[RMS_MAX_KIND + 32];
    char  binary_argument[RMS_MAX_PATH + 24];
    char  site_argument[RMS_MAX_PATH + 24];
    char *publish[12];
    int   argument = 0;

    if (g_tools.msbuild[0] == '\0')
    {
#if defined(RMS_WINDOWS)
        RMS_WARN("%s targets .NET Framework and needs the Visual Studio Build Tools; skipping its publish", project);
#else
        RMS_WARN("%s targets .NET Framework and needs Mono's MSBuild; skipping its publish", project);
#endif
        return RMS_OK;
    }

    if (rms_join(project_path, sizeof project_path, g_opts.root, project) != 0 ||
        rms_join(output_path, sizeof output_path, g_opts.out, output) != 0)
        return -1;

    if (!rms_file_exists(project_path))
    {
        /* Matches the .NET path above: a project that is not in the tree is
         * skipped rather than failed, so the deployer can be exercised while
         * the stack is still being assembled. */
        RMS_WARN("%s has not been created yet; skipping its publish", project);
        return RMS_OK;
    }

    if (rms_remove_tree(output_path) != 0 || rms_make_dirs(output_path) != 0)
        return -1;

    /*
     * The site directory and the assembly directory are two properties rather
     * than one, because the pipeline reads them separately: the first is where
     * the content goes, the second is where the build puts the assemblies, and
     * the pipeline is what carries the second into the first. They are not the
     * same directory and it matters: told to build straight into the site root,
     * the pipeline then tries to copy each assembly from that root into bin, does
     * not find it, and stops with an error naming a file it has just written.
     * Naming bin as the assembly directory is also what a site expects when it
     * runs, since that is the one directory a web server treats as its own.
     *
     * Both are named with a trailing separator, because MSBuild reads OutDir as a
     * directory prefix and appends to it.
     */
    if (snprintf(configuration, sizeof configuration, "/p:Configuration=%s", "Release")
            >= (int)sizeof configuration ||
        snprintf(site_argument, sizeof site_argument, "/p:WebProjectOutputDir=%s", output_path)
            >= (int)sizeof site_argument ||
        snprintf(binary_argument, sizeof binary_argument, "/p:OutDir=%s%cbin%c",
                 output_path, RMS_SEP, RMS_SEP)
            >= (int)sizeof binary_argument)
        return -1;

    publish[argument++] = g_tools.msbuild;
    publish[argument++] = project_path;
    publish[argument++] = (char *)"/nologo";
    publish[argument++] = (char *)"/verbosity:minimal";
    publish[argument++] = (char *)"/restore";
    publish[argument++] = configuration;

    /*
     * The web targets belong to one installation and the site's import reads
     * them through $(VSToolsPath), whose default is derived from the MSBuild
     * running. That default is right only when that MSBuild owns the targets,
     * which on a machine with several installations it need not be. The
     * discovered directory is passed when there is one, and when there is not,
     * the build step in the build manifest has already said so.
     */
    if (g_tools.vstools[0] != '\0')
    {
        static char vstools_argument[RMS_MAX_PATH + 16];

        if (snprintf(vstools_argument, sizeof vstools_argument, "/p:VSToolsPath=%s", g_tools.vstools)
            >= (int)sizeof vstools_argument)
            return -1;

        publish[argument++] = vstools_argument;
    }

    publish[argument++] = site_argument;
    publish[argument++] = binary_argument;
    publish[argument++] = (char *)"/t:_WPPCopyWebApplication";
    publish[argument] = NULL;

    if (run_argv(publish) != 0)
    {
        RMS_ERROR("%s: publish failed", project);
        return RMS_ERR_BUILD;
    }

    return RMS_OK;
}

/*
 * Publish one entry of the publish plan.
 *
 * The kind is what decides the tool, and it is named in the table rather than
 * inferred from the project file, because the two things the table holds that
 * look like a clue -- a path ending in .csproj and a project that targets .NET
 * Framework -- are shared by projects that publish in completely different
 * ways.
 */
static int
publish_project(const char *project, const char *output, const char *template_file, const char *kind)
{
    if (kind != NULL && strcmp(kind, "framework") == 0)
        return publish_framework_project(project, output);

    return publish_dotnet_project(project, output, template_file);
}

/* ==========================================================================
 * SERVICE PLANS
 *
 * This is the whole of what the deployer knows about services: a manifest in,
 * one plan file out. It deliberately does not know how any platform registers a
 * service, because the definitions a platform's own manager needs -- a Windows
 * service, an IIS site, a systemd unit, a launchd property list -- are the part
 * that differs most between platforms and the part that can only be checked
 * where it is compiled. Rendering them here in C meant the POSIX branches were
 * never compiled on the machine this file is developed on, so a mistake in a
 * unit file would have been found by an operator on the machine being deployed,
 * at the moment the deployment ran. RMS.ServiceInstaller renders them instead,
 * from a plan that is just a list of values, and every branch of that renderer
 * is compiled on every platform.
 *
 * What is left here is translation: read a manifest, expand every value once
 * against this machine, and write the result. Because the plan is written
 * already expanded, the values a service actually receives are the values an
 * operator can read in the generated file -- not the result of a second
 * expansion performed somewhere else, in a different order.
 * ========================================================================== */

/* The plan file's name, matching ServicePlanSet.FileName in RMS.ServiceModel. */
#define RMS_PLAN_FILE_NAME "services.ini"

/* Where the plan lives. A directory of its own under the publish root, so that
 * pointing the installer at a deployment is pointing it at one directory. */
#define RMS_PLAN_DIRECTORY "services"

/* Where every service writes its run logs. One subdirectory per service and one
 * file per run: RMS.ServiceHost owns that layout, and the DevOps site reads the
 * same tree, so the name is decided in one place rather than spelled three
 * times. */
#define RMS_LOG_DIRECTORY "logs"

/* The published directory of the supervisor, matching the publish plan below. */
#define RMS_HOST_DIRECTORY "service-host"

/* The published directory of the installer, matching the publish plan below. */
#define RMS_INSTALLER_DIRECTORY "service-installer"

/* The most environment entries one service may declare. A ceiling rather than a
 * growing array, because this is a key a person writes by hand and every service
 * in this stack is well inside it. */
#define RMS_PLAN_MAX_ENVIRONMENT 32

/* The plan format version, matching ServicePlanSet.CurrentVersion. A reader
 * refuses a version it does not understand, which is what makes a drift between
 * the names written below and the names the C# reader expects show up as an
 * error rather than as a service that quietly starts with nothing set. */
#define RMS_PLAN_VERSION "1"

/*
 * The directory holding the plan file.
 */
static int
service_plan_directory(char *out, size_t size)
{
    return rms_join(out, size, g_opts.out, RMS_PLAN_DIRECTORY);
}

/*
 * The directory every service writes its run logs under.
 */
static int
service_log_directory(char *out, size_t size)
{
    return rms_join(out, size, g_opts.out, RMS_LOG_DIRECTORY);
}

/*
 * The supervisor every service is registered against.
 *
 * Not the service's own executable. The supervisor is what captures the output,
 * applies the restart policy and records the run, so registering it in place of
 * the program is the whole reason every program in the stack ends up with the
 * same log page and the same history.
 *
 * The apphost carries a .exe suffix on Windows and none elsewhere, because the
 * two are the same program published for two platforms.
 */
static int
service_host_executable(char *out, size_t size)
{
    char directory[RMS_MAX_PATH];

    if (rms_join(directory, sizeof directory, g_opts.out, RMS_HOST_DIRECTORY) != 0)
        return -1;

#if defined(RMS_WINDOWS)
    return rms_join(out, size, directory, "RMS.ServiceHost.exe");
#else
    return rms_join(out, size, directory, "RMS.ServiceHost");
#endif
}

/*
 * The installer that turns a plan into this platform's own service definitions.
 */
static int
service_installer_executable(char *out, size_t size)
{
    char directory[RMS_MAX_PATH];

    if (rms_join(directory, sizeof directory, g_opts.out, RMS_INSTALLER_DIRECTORY) != 0)
        return -1;

#if defined(RMS_WINDOWS)
    return rms_join(out, size, directory, "RMS.ServiceInstaller.exe");
#else
    return rms_join(out, size, directory, "RMS.ServiceInstaller");
#endif
}

/*
 * A writer for the plan file.
 *
 * The plan goes straight to the file rather than being assembled in a buffer
 * first. A plan is small, but a fixed buffer adds a "what happens when it fills
 * up" branch to a program that would then have to choose between truncating --
 * and producing a plan that is wrong in a way nobody can see -- and failing at
 * the end regardless. Writing as we go removes the question.
 *
 * A null path means standard output, which is what makes `--dry-run services
 * install` show the exact plan a real run would register.
 */
typedef struct
{
    FILE *handle;
    int   failed;
} PlanWriter;

static int
plan_open(PlanWriter *writer, const char *path)
{
    writer->failed = 0;

    if (path == NULL)
    {
        writer->handle = stdout;
        return 0;
    }

    writer->handle = fopen(path, "wb");

    if (writer->handle == NULL)
    {
        RMS_ERROR("cannot write the service plan to %s: %s", path, strerror(errno));
        return -1;
    }

    return 0;
}

/*
 * Append one formatted line.
 *
 * A failed write is remembered rather than reported where it happened, so the
 * rendering below is not a repeated error check on every one of a hundred lines;
 * the caller asks once, at the end, whether the file came out whole.
 */
static void
plan_printf(PlanWriter *writer, const char *format, ...)
{
    va_list args;
    int     written;

    if (writer->failed)
        return;

    va_start(args, format);
    written = vfprintf(writer->handle, format, args);
    va_end(args);

    if (written < 0)
        writer->failed = 1;
}

/*
 * Append one key.
 *
 * The value is passed as data and never as part of the format, so a path or a
 * URL containing a percent sign cannot be reinterpreted on the way out.
 */
static void
plan_key(PlanWriter *writer, const char *key, const char *value)
{
    plan_printf(writer, "%s = %s\n", key, value != NULL ? value : "");
}

static int
plan_close(PlanWriter *writer)
{
    int failed = writer->failed;

    /* Standard output is flushed rather than closed: closing it would take the
     * rest of the run's output with it. */
    if (writer->handle == stdout)
    {
        if (fflush(stdout) != 0)
            failed = 1;
    }
    else if (fclose(writer->handle) != 0)
    {
        failed = 1;
    }

    if (failed)
    {
        RMS_ERROR("the service plan could not be written in full");
        return -1;
    }

    return 0;
}

typedef struct
{
    char key[RMS_MAX_ID];
    char value[RMS_MAX_VALUE];
} PlanVariable;

/*
 * Split a semicolon separated environment list into named entries.
 *
 * Semicolons rather than spaces are the separator in the manifest because a
 * value is frequently a path or a URL, and a space would make the end of one
 * assignment and the start of the next ambiguous. An entry with no name is
 * dropped rather than guessed at: a variable the deployment cannot name is a
 * variable nobody can look up later.
 */
static int
plan_collect_environment(const char *environment, PlanVariable *variables, size_t capacity,
                         size_t *count)
{
    const char *cursor = environment;

    *count = 0;

    while (cursor != NULL && *cursor != '\0')
    {
        const char *separator;
        const char *equals;
        size_t      length;

        while (*cursor == ';' || isspace((unsigned char)*cursor))
            cursor++;

        if (*cursor == '\0')
            break;

        separator = strchr(cursor, ';');
        length = separator != NULL ? (size_t)(separator - cursor) : strlen(cursor);

        while (length > 0 && isspace((unsigned char)cursor[length - 1]))
            length--;

        equals = length > 0 ? (const char *)memchr(cursor, '=', length) : NULL;

        if (equals != NULL && equals > cursor)
        {
            size_t key_length = (size_t)(equals - cursor);

            if (*count == capacity)
            {
                RMS_ERROR("a service declares more than %d environment variables", (int)capacity);
                return -1;
            }

            if (rms_copy_n(variables[*count].key, sizeof variables[*count].key,
                           cursor, key_length) != 0 ||
                rms_copy_n(variables[*count].value, sizeof variables[*count].value,
                           equals + 1, length - key_length - 1) != 0)
                return -1;

            (*count)++;
        }
        else if (equals == cursor)
        {
            RMS_WARN("ignoring an environment entry with an empty name: %.*s", (int)length, cursor);
        }

        if (separator == NULL)
            break;

        cursor = separator + 1;
    }

    return 0;
}

/*
 * Sort the environment entries by name.
 *
 * The plan is generated output, and regenerating an unchanged manifest should
 * produce an unchanged file: an operator diffing two deployments should see what
 * really moved, not the order a hash table happened to iterate in.
 */
static void
plan_sort_environment(PlanVariable *variables, size_t count)
{
    size_t index;

    for (index = 1; index < count; index++)
    {
        PlanVariable entry = variables[index];
        size_t       position = index;

        while (position > 0 && strcmp(variables[position - 1].key, entry.key) > 0)
        {
            variables[position] = variables[position - 1];
            position--;
        }

        variables[position] = entry;
    }
}

/* True when the environment already names a variable. */
static int
plan_has_variable(const PlanVariable *variables, size_t count, const char *key)
{
    size_t index;

    for (index = 0; index < count; index++)
    {
        if (strcmp(variables[index].key, key) == 0)
            return 1;
    }

    return 0;
}

/*
 * Rewrite the separators in a plan value that may be a path.
 *
 * The manifest is written with forward slashes because that reads the same on
 * all three platforms, and this turns them into the separator of the machine the
 * plan is being written for. A URL is left alone: its separators are part of its
 * syntax, and rewriting them would produce "http:\\host", which is not a URL at
 * all. The two cases are told apart by the scheme rather than by guessing at the
 * shape of the value, because guessing wrong breaks a working value either way.
 */
static void
plan_normalise_value(char *value)
{
    if (strncmp(value, "http://", 7) == 0 ||
        strncmp(value, "https://", 8) == 0 ||
        strncmp(value, "file://", 7) == 0)
    {
        return;
    }

    normalise_separators(value);
}

/*
 * Write one argument.N key per argument.
 *
 * Not one joined command line: a deployment path can contain a space, and two
 * consumers splitting a command line on spaces would disagree about where the
 * path ends. One key per argument is what means no consumer has to guess, and it
 * is why the manifest separates arguments with semicolons rather than spaces.
 */
static void
plan_write_arguments(PlanWriter *writer, const char *arguments)
{
    const char *cursor = arguments;
    int         index = 0;

    while (cursor != NULL && *cursor != '\0')
    {
        const char *separator;
        size_t      length;

        while (*cursor == ';' || isspace((unsigned char)*cursor))
            cursor++;

        if (*cursor == '\0')
            break;

        separator = strchr(cursor, ';');
        length = separator != NULL ? (size_t)(separator - cursor) : strlen(cursor);

        while (length > 0 && isspace((unsigned char)cursor[length - 1]))
            length--;

        if (length > 0)
        {
            char key[RMS_MAX_ID];
            char value[RMS_MAX_VALUE];

            if (snprintf(key, sizeof key, "argument.%d", index) >= (int)sizeof key ||
                rms_copy_n(value, sizeof value, cursor, length) != 0)
            {
                writer->failed = 1;
                return;
            }

            plan_normalise_value(value);

            plan_key(writer, key, value);
            index++;
        }

        if (separator == NULL)
            break;

        cursor = separator + 1;
    }
}

/*
 * Write one service's section.
 *
 * The key names are the ones RMS.ServiceModel.PlanFile reads. They are repeated
 * here rather than shared, because a C program and a C# assembly have no header
 * in common; the version above is what turns a drift between the two into a
 * refusal to load rather than a service that starts with nothing set.
 *
 * The restart figures are written even when the manifest says nothing about
 * them, so the file states the whole definition rather than leaving defaults to
 * be applied by whoever reads it.
 */
static int
plan_write_service(PlanWriter *writer, const ServiceDefinition *service)
{
    PlanVariable variables[RMS_PLAN_MAX_ENVIRONMENT];
    size_t       count = 0;
    size_t       index;

    if (plan_collect_environment(service->environment, variables, RMS_PLAN_MAX_ENVIRONMENT,
                                 &count) != 0)
        return -1;

    /*
     * A published .NET program finds its runtime through DOTNET_ROOT when the
     * runtime was installed by this deployer, because such an installation is not
     * registered with the operating system and cannot be located any other way.
     * The value written is the root this run actually resolved, so it is right
     * whichever dotnet was found -- the one this deployer installed, or one that
     * was already on the machine -- and the manifest does not have to know where
     * that is.
     *
     * A web site has no process of its own here -- IIS owns it on Windows -- so
     * there is no environment to give.
     */
    if (service->executable[0] != '\0' &&
        g_tools.dotnet_root[0] != '\0' &&
        !plan_has_variable(variables, count, "DOTNET_ROOT"))
    {
        if (count == RMS_PLAN_MAX_ENVIRONMENT)
            return -1;

        if (rms_copy(variables[count].key, sizeof variables[count].key, "DOTNET_ROOT") != 0 ||
            rms_copy(variables[count].value, sizeof variables[count].value, g_tools.dotnet_root) != 0)
            return -1;

        count++;
    }

    plan_sort_environment(variables, count);

    for (index = 0; index < count; index++)
        plan_normalise_value(variables[index].value);

    plan_printf(writer, "\n[%s]\n", service->id);
    plan_key(writer, "name", service->name);
    plan_key(writer, "description", service->description);
    plan_key(writer, "executable", service->executable);
    plan_key(writer, "working_dir", service->working_directory);

    plan_write_arguments(writer, service->arguments);

    for (index = 0; index < count; index++)
    {
        char key[RMS_MAX_ID];

        if (snprintf(key, sizeof key, "environment.%s", variables[index].key) >= (int)sizeof key)
        {
            writer->failed = 1;
            return -1;
        }

        plan_key(writer, key, variables[index].value);
    }

    plan_key(writer, "restart",
             service->restart[0] != '\0' ? service->restart : "always");
    plan_key(writer, "restart_delay",
             service->restart_delay[0] != '\0' ? service->restart_delay : "5");
    plan_key(writer, "max_restart_delay",
             service->max_restart_delay[0] != '\0' ? service->max_restart_delay : "60");
    plan_key(writer, "stop_timeout",
             service->stop_timeout[0] != '\0' ? service->stop_timeout : "30");
    plan_key(writer, "shutdown_line", service->shutdown_line);
    plan_key(writer, "website", service->website);
    plan_key(writer, "port", service->port);
    plan_key(writer, "platforms", service->platforms);
    plan_key(writer, "user", service->user);

    return 0;
}

/*
 * Write the plan.
 *
 * Two things are deliberately not done here. The plan is not merged with one
 * already on disk: it is generated output, and making it idempotent by editing
 * would let the definitions a machine is running from drift away from the
 * manifest that is supposed to describe them. And it carries no service that
 * belongs to another platform: the platform-specific keys were already resolved
 * against this machine during loading, so a section for a different platform
 * would be a definition this deployer could not have expanded correctly.
 */
static int
write_service_plan(const ServiceList *services, const char *path, const char *log_directory,
                   size_t *written)
{
    char       host[RMS_MAX_PATH];
    PlanWriter writer;
    size_t     index;

    *written = 0;

    if (service_host_executable(host, sizeof host) != 0)
        return -1;

    if (plan_open(&writer, path) != 0)
        return -1;

    plan_printf(&writer,
                "# Generated by rmsdeploy from deploy/rmsdeploy.services.\n"
                "# Edits are overwritten on the next deployment.\n"
                "\n");

    plan_printf(&writer, "[settings]\n");
    plan_key(&writer, "version", RMS_PLAN_VERSION);
    plan_key(&writer, "platform", host_platform());
    plan_key(&writer, "root", g_opts.root);
    plan_key(&writer, "out", g_opts.out);
    plan_key(&writer, "log_dir", log_directory);
    plan_key(&writer, "host", host);

    for (index = 0; index < services->count; index++)
    {
        if (!list_contains(services->items[index].platforms, host_platform()))
            continue;

        if (plan_write_service(&writer, &services->items[index]) != 0)
        {
            plan_close(&writer);
            return -1;
        }

        (*written)++;
    }

    return plan_close(&writer);
}

/* ==========================================================================
 * CREDENTIALS
 *
 * A deployment has secrets the deployer must not invent: the key that
 * authenticates a caller, the token that guards a control plane. The plan names
 * every one of them through an environment variable whose name ends in _FILE,
 * and this pass turns that statement into something an operator can act on --
 * the directory the files belong in, and a document beside them saying which are
 * still missing and how to generate each one.
 *
 * It writes no value, and it writes nothing that could be mistaken for one. A
 * file of prose placed where a credential is expected would be read as the
 * credential the moment an operator copied it into place, and prose long enough
 * to satisfy the length a service checks for would switch a surface on with a
 * value that is not a secret at all. The document therefore has a name of its
 * own, sits beside the files rather than among them, and is read by nothing.
 * ========================================================================== */

/* The directory the plan's credential files belong in, under the publish root.
 * Every credential lives here rather than beside the program that reads it,
 * because one of them is deliberately shared: the key the API checks is the key
 * the scanner's own site presents. One directory also means one place to secure
 * and one place to look. */
#define RMS_CREDENTIALS_DIRECTORY "credentials"

/* The document that explains what remains to be done by hand. Named so that it
 * cannot be mistaken for a credential, and so that an editor opens it. */
#define RMS_CREDENTIALS_DOCUMENT "README.txt"

/* The suffixes that mark an environment variable as naming a credential file.
 * Recognizing a credential by the name of the variable carrying its path, rather
 * than by a list of known variables, means the manifest states where a secret
 * lives exactly once: the program that reads it and this document are both
 * derived from that one statement, so they cannot come to disagree.
 *
 * Two suffixes rather than one because the stack names a guard after what it
 * guards: a control plane is protected by a token, the caller-facing API by a
 * key. Matching on a bare _FILE would have caught the settings files too -- the
 * DevOps service and the Configuration service both take a plan file, and a plan
 * file is not a secret. */
#define RMS_CREDENTIAL_SUFFIX_TOKEN "_TOKEN_FILE"
#define RMS_CREDENTIAL_SUFFIX_KEY   "_KEY_FILE"

/* The shortest credential every surface in this stack accepts. Repeated here so
 * that the document can state the rule an operator has to satisfy without
 * anyone having to read three services to find it. */
#define RMS_CREDENTIAL_MINIMUM 32

/* The most distinct credential files one plan may name. A ceiling rather than a
 * limit: it is here so that a manifest mistake cannot make the deployer allocate
 * without bound. Sized against what a deployment of this shape needs, which is
 * one file per service and one shared between a service and its site. */
#define RMS_MAX_CREDENTIALS 16

/* One credential file the plan names. */
typedef struct
{
    char variable[RMS_MAX_ID];   /* The environment variable carrying the path.  */
    char path[RMS_MAX_PATH];     /* The expanded absolute path of the file.      */
    char readers[RMS_MAX_NOTE];  /* The services that read it, comma separated.  */
} Credential;

/*
 * True when a name ends with a suffix.
 */
static int
ends_with(const char *text, const char *suffix)
{
    size_t text_length;
    size_t suffix_length;

    if (text == NULL || suffix == NULL)
        return 0;

    text_length = strlen(text);
    suffix_length = strlen(suffix);

    if (suffix_length > text_length)
        return 0;

    return strcmp(text + (text_length - suffix_length), suffix) == 0;
}

/*
 * Collect every credential file this platform's plan names.
 *
 * Deduplicated by path, because one secret is deliberately read by two programs:
 * the key the API checks is the key its own site presents. Listing it twice
 * would suggest two files to create, and an operator who created only one half
 * of a matched pair would have a deployment where every call is refused for a
 * reason that neither end reports.
 */
static int
collect_credentials(const ServiceList *services, Credential *items, size_t capacity,
                    size_t *count)
{
    size_t index;

    *count = 0;

    for (index = 0; index < services->count; index++)
    {
        const ServiceDefinition *service = &services->items[index];
        PlanVariable variables[RMS_PLAN_MAX_ENVIRONMENT];
        size_t       variable_count = 0;
        size_t       variable_index;

        if (!list_contains(service->platforms, host_platform()))
            continue;

        if (plan_collect_environment(service->environment, variables,
                                     RMS_PLAN_MAX_ENVIRONMENT, &variable_count) != 0)
            return -1;

        for (variable_index = 0; variable_index < variable_count; variable_index++)
        {
            const char *variable = variables[variable_index].key;
            size_t      search;
            size_t      found = 0;

            if (!ends_with(variable, RMS_CREDENTIAL_SUFFIX_TOKEN) &&
                !ends_with(variable, RMS_CREDENTIAL_SUFFIX_KEY))
                continue;

            /* The value was expanded when the manifest was loaded but has not
             * been normalised: the plan writer normalises each value as it
             * writes it, and this pass runs on the same values before that.
             * Normalising here too is what keeps the path in the document
             * identical to the path the plan carries, so an operator moving the
             * value from one to the other cannot be misled by a separator. */
            plan_normalise_value(variables[variable_index].value);

            if (variables[variable_index].value[0] == '\0')
                continue;

            for (search = 0; search < *count; search++)
            {
                if (strcmp(items[search].path, variables[variable_index].value) == 0)
                {
                    found = 1;
                    break;
                }
            }

            if (found)
            {
                Credential *existing = &items[search];
                size_t      used = strlen(existing->readers);
                size_t      needed = strlen(service->name);

                /* A name that would not fit is dropped rather than truncated: a
                 * truncated reader is a name an operator would not find, and the
                 * list is an aid to reading rather than the record itself. */
                if (used > 0 && used + 2 + needed < sizeof existing->readers)
                {
                    existing->readers[used] = ',';
                    existing->readers[used + 1] = ' ';
                    existing->readers[used + 2] = '\0';
                    rms_copy(existing->readers + used + 2,
                             sizeof existing->readers - used - 2, service->name);
                }

                continue;
            }

            if (*count == capacity)
            {
                RMS_ERROR("the plan names more than %d credential files", (int)capacity);
                return -1;
            }

            if (rms_copy(items[*count].variable, sizeof items[*count].variable, variable) != 0 ||
                rms_copy(items[*count].path, sizeof items[*count].path,
                         variables[variable_index].value) != 0 ||
                rms_copy(items[*count].readers, sizeof items[*count].readers,
                         service->name) != 0)
                return -1;

            (*count)++;
        }
    }

    return 0;
}

/*
 * Write the document that says which credentials are still to be created.
 *
 * Rewritten on every run, so it describes the deployment the plan describes. A
 * document left from an earlier run still claiming a credential was missing
 * would be worse than no document at all, because an operator's first act is to
 * believe it.
 */
static int
write_credentials_document(const Credential *items, size_t count, const char *path)
{
    FILE  *file;
    size_t index;
    size_t missing = 0;

    for (index = 0; index < count; index++)
    {
        if (!rms_file_exists(items[index].path))
            missing++;
    }

    file = fopen(path, "wb");

    if (file == NULL)
    {
        RMS_ERROR("the credentials document could not be written to %s", path);
        return -1;
    }

    fprintf(file,
            "RMS DEPLOYMENT CREDENTIALS\n"
            "==========================\n"
            "\n"
            "Generated by rmsdeploy from deploy/rmsdeploy.services. Nothing reads this\n"
            "file. It exists so that a machine can be finished in one pass.\n"
            "\n"
            "%zu credential file%s named by this deployment: %zu present, %zu to create.\n"
            "\n"
            "Each credential is a file. The program that needs it is told the file's path\n"
            "by an environment variable, so no secret is written into a service definition\n"
            "and none is written by the deployer.\n"
            "\n"
            "THE FILE HOLDS THE VALUE AND NOTHING ELSE\n"
            "  No comment, no quotes, no label. A reader takes the whole contents of the\n"
            "  file with the surrounding whitespace removed, so anything else in it becomes\n"
            "  part of the secret. The comparison then fails for a reason that appears\n"
            "  nowhere in the log, and the failure looks exactly like a wrong password.\n"
            "\n"
            "GENERATE THE VALUE, DO NOT INVENT ONE\n"
            "  openssl rand -base64 48    Linux, macOS, and Git Bash on Windows\n"
            "  [guid]::NewGuid()          PowerShell, for the API key\n"
            "\n"
            "  Every surface here refuses a credential shorter than %d characters, so a\n"
            "  value that is too short presents as a surface that is still switched off\n"
            "  rather than as one that is refusing callers.\n"
            "\n"
            "THE API KEY IS A GUID, OR THE API REFUSES EVERY CALL\n"
            "  Not because a GUID is a stronger secret, but because the API compares the\n"
            "  key as one: a value that is not a GUID is not a key it can hold, and it\n"
            "  answers every caller that way. The scanner's own site presents the same key\n"
            "  in the apiKey query parameter, which is the contract the API has always\n"
            "  had, so this one file is deliberately read by two programs.\n"
            "\n"
            "KEEP IT PRIVATE\n"
            "  chmod 600 <file>                                        Linux and macOS\n"
            "  icacls <file> /inheritance:r /grant:r \"%%USERNAME%%:R\"      Windows\n"
            "\n"
            "  Own it by the account the service runs as. These services run as the account\n"
            "  the service manager was told to use, which is the local system account unless\n"
            "  a deployment says otherwise.\n"
            "\n",
            count, count == 1 ? "" : "s", count - missing, missing, RMS_CREDENTIAL_MINIMUM);

    for (index = 0; index < count; index++)
    {
        fprintf(file,
                "---------------------------------------------------------------------------\n"
                "%s\n"
                "  variable  %s\n"
                "  path      %s\n"
                "  read by   %s\n",
                rms_file_exists(items[index].path) ? "PRESENT" : "TO CREATE",
                items[index].variable,
                items[index].path,
                items[index].readers);
    }

    fprintf(file,
            "---------------------------------------------------------------------------\n"
            "\n"
            "A service whose credential is absent still runs: it answers the discovery\n"
            "endpoint and refuses every protected one, which is the safe direction to fail\n"
            "in. Nothing else in the deployment depends on these files.\n");

    if (fclose(file) != 0)
    {
        RMS_ERROR("the credentials document could not be closed");
        return -1;
    }

    return 0;
}

/*
 * Materialise the credentials directory and say what an operator has left to do.
 *
 * Run whenever the plan is written, because the plan is the statement of what a
 * deployment needs and it is regenerated by every verb that touches the
 * deployment's definitions. Nothing is carried between runs: what is missing is
 * derived by looking at the disk, so the report cannot disagree with it.
 */
static int
report_credentials(const ServiceList *services, int write_document)
{
    Credential items[RMS_MAX_CREDENTIALS];
    char       directory[RMS_MAX_PATH];
    char       document[RMS_MAX_PATH];
    size_t     count = 0;
    size_t     index;
    size_t     missing = 0;

    if (collect_credentials(services, items, RMS_MAX_CREDENTIALS, &count) != 0)
        return -1;

    if (count == 0)
        return 0;

    if (rms_join(directory, sizeof directory, g_opts.out, RMS_CREDENTIALS_DIRECTORY) != 0 ||
        rms_join(document, sizeof document, directory, RMS_CREDENTIALS_DOCUMENT) != 0)
        return -1;

    for (index = 0; index < count; index++)
    {
        if (!rms_file_exists(items[index].path))
            missing++;
    }

    rms_step("Credentials");

    for (index = 0; index < count; index++)
    {
        printf("  %-11s %s\n",
               rms_file_exists(items[index].path) ? "present" : "to create",
               items[index].path);
    }

    if (write_document)
    {
        if (rms_make_dirs(directory) != 0 ||
            write_credentials_document(items, count, document) != 0)
            return -1;
    }

    if (missing == 0)
    {
        printf("  every credential the plan names is present\n");
        return 0;
    }

    printf("  %zu of %zu are not there yet; each holds a generated value and nothing else\n",
           missing, count);

    if (write_document)
        printf("  see %s\n", document);

    return 0;
}

/* ==========================================================================
 * SITE KEYS
 *
 * A web site under IIS is a process the deployer does not start, so it cannot
 * be handed an environment the way a service can. The one channel both hosts
 * have is the site's own configuration, and that is what this pass uses: the
 * published Web.config declares where the key file is, and the deployer writes
 * the path into it.
 *
 * Only the path is written, never the value. A path in a file inside the web
 * root is not a disclosure, and the file the path names lives outside every
 * directory IIS serves -- so the arrangement is what keeps the secret out of
 * the web root rather than what puts it there.
 * ========================================================================== */

/* The largest Web.config this deployer will rewrite. A published site's
 * configuration is a few kilobytes; the ceiling is here so that a file which is
 * not one cannot make the deployer read without bound, and so the buffers can be
 * fixed rather than allocated. */
#define RMS_MAX_WEB_CONFIG (256 * 1024)

/* The text that precedes the key file's path in a published Web.config. */
#define RMS_SITE_KEY_MARKER "<add key=\"SiteServiceKeyFile\" value=\""

/* The name given to the rewritten configuration while it is being written. It
 * sits beside the file it will replace, so that the swap does not cross a volume
 * and cannot fail for a reason an operator would have to work out. */
#define RMS_SITE_KEY_TEMP_NAME "Web.config.rmsnew"

/*
 * Point a published site's configuration at the file that holds its key.
 *
 * The edit is confined to one attribute: the key is found by name and the value
 * after it is replaced. Everything else -- including settings an operator may
 * have changed by hand -- is copied through untouched, because this is a
 * deployment writing one path into output it generated, not a configuration
 * manager taking ownership of the file.
 *
 * The result is written beside the original and then swapped in, so a failure
 * partway through leaves the site's configuration as it was rather than
 * truncated. On Windows that swap is a delete followed by a rename, which is not
 * atomic; the window is a few microseconds long and contains one file that holds
 * no secret, and it is still better than the alternative of a config that a
 * crash left half written.
 *
 * Returns 1 when the configuration was rewritten -- or, when <c>write</c> is zero,
 * when it would have been -- 0 when there was nothing to rewrite because the site
 * does not declare the setting, and -1 on a failure.
 *
 * When write is zero nothing is touched. The file is still read and the marker is
 * still looked for, so a dry run reports the warnings a real run would report and
 * then stops short of the write.
 */
static int
apply_site_key_path(const char *website, const char *key_path, int write)
{
    static char buffer[RMS_MAX_WEB_CONFIG];
    static char rewritten[RMS_MAX_WEB_CONFIG];
    char        configuration[RMS_MAX_PATH];
    char        temporary[RMS_MAX_PATH];
    const char *marker;
    const char *value_end;
    size_t      prefix;
    size_t      path_length;
    size_t      used;
    FILE       *file;

    if (rms_join(configuration, sizeof configuration, website, "Web.config") != 0 ||
        rms_join(temporary, sizeof temporary, website, RMS_SITE_KEY_TEMP_NAME) != 0)
        return -1;

    if (!rms_file_exists(configuration))
    {
        RMS_WARN("%s has no Web.config yet; it was not pointed at a key file", website);
        return 0;
    }

    file = fopen(configuration, "rb");

    if (file == NULL)
    {
        RMS_ERROR("the site configuration %s could not be opened", configuration);
        return -1;
    }

    used = fread(buffer, 1, sizeof buffer, file);

    if (ferror(file))
    {
        fclose(file);
        RMS_ERROR("the site configuration %s could not be read", configuration);
        return -1;
    }

    if (used == sizeof buffer)
    {
        fclose(file);
        RMS_ERROR("the site configuration %s is larger than %d bytes",
                  configuration, (int)sizeof buffer);
        return -1;
    }

    fclose(file);
    buffer[used] = '\0';

    marker = strstr(buffer, RMS_SITE_KEY_MARKER);

    if (marker == NULL)
    {
        /* A site built from a Web.config that does not carry the setting cannot
         * read a key file at all, so saying so is more useful than editing
         * something else in the hope that it was meant. */
        RMS_WARN("%s does not declare SiteServiceKeyFile; it cannot be told about a key file",
                 configuration);
        return 0;
    }

    prefix = (size_t)(marker - buffer) + strlen(RMS_SITE_KEY_MARKER);
    value_end = strchr(buffer + prefix, '"');

    if (value_end == NULL)
    {
        RMS_ERROR("the SiteServiceKeyFile entry in %s is not closed", configuration);
        return -1;
    }

    path_length = strlen(key_path);

    if (prefix + path_length + strlen(value_end) + 1 > sizeof rewritten)
    {
        RMS_ERROR("the rewritten site configuration would be larger than %d bytes",
                  (int)sizeof rewritten);
        return -1;
    }

    if (!write)
        return 1;

    memcpy(rewritten, buffer, prefix);
    memcpy(rewritten + prefix, key_path, path_length);
    strcpy(rewritten + prefix + path_length, value_end);

    file = fopen(temporary, "wb");

    if (file == NULL)
    {
        RMS_ERROR("the rewritten site configuration could not be written to %s", temporary);
        return -1;
    }

    if (fwrite(rewritten, 1, strlen(rewritten), file) != strlen(rewritten) || fclose(file) != 0)
    {
        RMS_ERROR("the rewritten site configuration could not be written to %s", temporary);
        return -1;
    }

#ifdef _WIN32
    /* The C rename will not replace an existing file on Windows, so the original
     * goes first. On POSIX the rename below replaces it in one step. */
    remove(configuration);
#endif

    if (rename(temporary, configuration) != 0)
    {
        RMS_ERROR("the rewritten site configuration could not be put in place at %s", configuration);
        return -1;
    }

    return 1;
}

/*
 * Write every web site's key file path into the configuration it was published
 * with.
 *
 * Only the sites: a service is handed its path in its environment and needs
 * nothing written. Only a site that declares the setting: the manifest names the
 * file through a _KEY_FILE variable, which is the same convention the credentials
 * pass uses, so a site and its credential are described once.
 *
 * When write is zero the sites are reported and nothing is touched, which is what
 * lets a dry run show this phase rather than hiding it.
 */
static int
apply_site_key_paths(const ServiceList *services, int write, size_t *applied, size_t *declared)
{
    size_t index;

    *applied = 0;
    *declared = 0;

    for (index = 0; index < services->count; index++)
    {
        const ServiceDefinition *service = &services->items[index];
        PlanVariable variables[RMS_PLAN_MAX_ENVIRONMENT];
        char         website[RMS_MAX_PATH];
        size_t       count = 0;
        size_t       variable_index;
        int          published;

        if (!list_contains(service->platforms, host_platform()))
            continue;

        if (service->website[0] == '\0')
            continue;

        if (plan_collect_environment(service->environment, variables,
                                     RMS_PLAN_MAX_ENVIRONMENT, &count) != 0)
            return -1;

        if (rms_copy(website, sizeof website, service->website) != 0)
            return -1;

        plan_normalise_value(website);
        published = rms_dir_exists(website);

        for (variable_index = 0; variable_index < count; variable_index++)
        {
            int written;

            if (!ends_with(variables[variable_index].key, RMS_CREDENTIAL_SUFFIX_KEY))
                continue;

            plan_normalise_value(variables[variable_index].value);

            if (variables[variable_index].value[0] == '\0')
                continue;

            (*declared)++;

            if (!published)
            {
                RMS_WARN("%s has not been published yet; no key file path was written", website);
                continue;
            }

            written = apply_site_key_path(website, variables[variable_index].value, write);

            if (written < 0)
                return -1;

            if (written > 0)
            {
                printf("  %-16s %s\n", service->name, variables[variable_index].value);
                (*applied)++;
            }
        }
    }

    /*
     * Three different situations read the same way if they share one message, and
     * they call for different things: nothing to do, a build that has not run yet,
     * and a write that a dry run skipped. Each says which it is.
     */
    if (*declared == 0)
        printf("  no web site in the plan declares a key file\n");
    else if (*applied == 0)
        printf("  none of the %zu site(s) that declare a key file has been published yet\n",
               *declared);
    else if (!write)
        printf("  nothing was written: this is a dry run\n");

    return 0;
}

/*
 * Turn the manifest into a plan and hand it to the installer.
 *
 * Every action goes through the same two steps, `status` included. The answer to
 * "what is running" comes from the platform's own service manager, and the
 * service manager can only be asked about definitions it was given, so the plan
 * has to exist before the question can be asked. That is also what makes the
 * file an operator opens and the file the machine is running from the same file.
 */
static int
manage_services(const ServiceList *services, const char *action)
{
    char   plan_directory[RMS_MAX_PATH];
    char   plan_path[RMS_MAX_PATH];
    char   log_directory[RMS_MAX_PATH];
    char   installer[RMS_MAX_PATH];
    size_t applicable = 0;
    size_t written = 0;
    size_t index;

    for (index = 0; index < services->count; index++)
    {
        const ServiceDefinition *service = &services->items[index];

        if (!list_contains(service->platforms, host_platform()))
            continue;

        /*
         * A definition with neither a program nor a web site is one nothing can
         * be done with. Refused here, naming the section, rather than left for
         * the installer to report as a service that no mechanism accepts.
         */
        if (service->executable[0] == '\0' && service->website[0] == '\0')
        {
            RMS_ERROR("[%s] names neither an executable nor a website", service->id);
            return RMS_ERR_USAGE;
        }

        applicable++;
    }

    if (applicable == 0)
    {
        RMS_WARN("no service in the manifest applies to %s", host_platform());
        return RMS_OK;
    }

    if (service_plan_directory(plan_directory, sizeof plan_directory) != 0 ||
        service_log_directory(log_directory, sizeof log_directory) != 0 ||
        service_installer_executable(installer, sizeof installer) != 0 ||
        rms_join(plan_path, sizeof plan_path, plan_directory, RMS_PLAN_FILE_NAME) != 0)
        return RMS_ERR_IO;

    rms_step("Service plan");
    printf("  platform  %s\n", host_platform());
    printf("  plan      %s\n", g_opts.dry_run ? "(standard output)" : plan_path);
    printf("  log root  %s\n", log_directory);

    if (!g_opts.dry_run)
    {
        if (rms_make_dirs(plan_directory) != 0 || rms_make_dirs(log_directory) != 0)
            return RMS_ERR_IO;
    }

    if (write_service_plan(services, g_opts.dry_run ? NULL : plan_path, log_directory,
                           &written) != 0)
        return RMS_ERR_IO;

    printf("  services  %zu\n", written);

    /*
     * The key file's path goes into the published configuration before the site
     * is registered, because a site that had started without it would serve
     * refusals until something recycled it. Only an install writes it: the edit
     * changes a file a running site watches, and that restarts the site, which is
     * the right thing during an install and the wrong thing when an operator has
     * only asked what state the machine is in.
     */
    if (strcmp(action, "install") == 0)
    {
        size_t applied = 0;
        size_t declared = 0;

        rms_step("Site keys");

        if (apply_site_key_paths(services, !g_opts.dry_run, &applied, &declared) != 0)
            return RMS_ERR_IO;

        if (applied > 0)
            printf("  sites     %zu\n", applied);
    }

    if (!g_opts.dry_run && !rms_file_exists(installer))
    {
        RMS_ERROR("the service installer is not at %s; run 'rmsdeploy publish' first", installer);
        return RMS_ERR_USAGE;
    }

    if (g_opts.dry_run)
    {
        /*
         * A dry run stops here. The installer is what changes the machine, and in
         * a dry run the plan was written to standard output rather than to disk,
         * so running it would apply whatever plan was left on disk by an earlier
         * deployment -- the opposite of what asking for a dry run is for.
         */
        printf("  would run %s %s --directory %s\n", installer, action, plan_directory);
        printf("  (not run: this is a dry run)\n");
    }
    else
    {
        char *arguments[] =
        {
            installer, (char *)action, (char *)"--directory", plan_directory, NULL
        };

        if (run_argv(arguments) != 0)
        {
            RMS_ERROR("the service installer reported a failure for '%s'", action);
            return RMS_ERR_BUILD;
        }
    }

    /*
     * The credential list belongs to the verbs that describe the deployment, not
     * to the ones that change what is running. An install is when a machine is
     * being finished and a status is when an operator is asking what is left; a
     * stop or a start would be interrupted by a list that had not changed.
     */
    if (strcmp(action, "install") == 0 || strcmp(action, "status") == 0)
    {
        if (report_credentials(services, !g_opts.dry_run) != 0)
            return RMS_ERR_IO;
    }

    return RMS_OK;
}

/* ==========================================================================
 * COMMANDS
 * ========================================================================== */

static int
command_probe(const RequirementList *requirements)
{
    size_t index;
    int    missing = 0;

    rms_step("Platform");
    printf("  os            %s\n", host_platform());
    printf("  architecture  %s bit\n", host_architecture());

    rms_step("Toolchain");
    printf("  dotnet        %s\n", g_tools.dotnet[0] ? g_tools.dotnet : "(not found)");
    printf("  node          %s\n", g_tools.node[0] ? g_tools.node : "(not found)");
    printf("  npm           %s\n", g_tools.npm[0] ? g_tools.npm : "(not found)");
    printf("  framework msbuild  %s\n", g_tools.msbuild[0] ? g_tools.msbuild : "(not found)");
    printf("  web build targets  %s\n", g_tools.vstools[0] ? g_tools.vstools
                                                          : "(not found: the Web Forms sites will not build)");
    printf("  xsp host      %s\n", g_tools.xsp[0] ? g_tools.xsp : "(not found)");

    rms_step("Requirements");
    for (index = 0; index < requirements->count; index++)
    {
        const Requirement *requirement = &requirements->items[index];
        int applicable = list_contains(requirement->platforms, host_platform());
        int present;

        /* A requirement that does not apply here is reported as such rather than
         * as missing: Mono is not absent on Windows, it is simply not part of a
         * Windows deployment, and calling it missing would send an operator
         * looking for a problem that does not exist. */
        if (!applicable)
        {
            printf("  %-16s %-10s %-12s not used on %s\n",
                   requirement->id,
                   requirement->kind,
                   requirement->version[0] != '\0' ? requirement->version : "-",
                   host_platform());
            continue;
        }

        present = requirement_satisfied(requirement);

        printf("  %-16s %-10s %-12s %s\n",
               requirement->id,
               requirement->kind,
               requirement->version[0] != '\0' ? requirement->version : "-",
               present ? "present" : "missing");

        if (!present)
            missing++;
    }

    rms_step("Summary");
    if (missing == 0)
        printf("  every requirement is satisfied\n");
    else
        printf("  %d requirement(s) must be installed; run 'rmsdeploy bootstrap'\n", missing);

    return RMS_OK;
}

static int
command_bootstrap(const RequirementList *requirements)
{
    size_t index;
    int    installed = 0;

    rms_step("Bootstrapping prerequisites");

    for (index = 0; index < requirements->count; index++)
    {
        const Requirement *requirement = &requirements->items[index];

        if (!list_contains(requirement->platforms, host_platform()))
        {
            printf("  %-16s not used on %s\n", requirement->id, host_platform());
            continue;
        }

        if (requirement_satisfied(requirement))
        {
            printf("  %-16s present\n", requirement->id);
            continue;
        }

        /*
         * A resolved toolchain also satisfies a requirement that declares no
         * check of its own. Installing a second copy of a compiler that is
         * already on the path would waste time and would leave the deployment
         * depending on which copy the supervisor happened to find.
         */
        if ((strcmp(requirement->kind, "dotnet") == 0 && g_tools.dotnet[0] != '\0') ||
            (strcmp(requirement->kind, "node") == 0 && g_tools.node[0] != '\0') ||
            (strcmp(requirement->kind, "msbuild") == 0 && g_tools.msbuild[0] != '\0'))
        {
            printf("  %-16s satisfied by the system installation\n", requirement->id);
            continue;
        }

        printf("  %-16s installing\n", requirement->id);

        if (install_requirement(requirement) != RMS_OK)
            return RMS_ERR_TOOLCHAIN;

        installed++;
    }

    rms_step("Bootstrap complete");
    printf("  %d prerequisite(s) installed\n", installed);

    return RMS_OK;
}

static int
command_build(const BuildStepList *steps, const char *only)
{
    size_t index;
    int    built = 0;
    int    failed = 0;
    int    skipped = 0;

    rms_step("Build");

    for (index = 0; index < steps->count; index++)
    {
        const BuildStep *step = &steps->items[index];
        int result;

        if (only != NULL && strcmp(only, step->id) != 0)
            continue;

        if (!list_contains(step->platforms, host_platform()))
        {
            printf("  %-32s %-18s skipped on %s\n", step->id, step->kind, host_platform());
            skipped++;
            continue;
        }

        /*
         * A step whose source does not exist yet is skipped rather than failed.
         * The stack is described in full before it is finished, so the deployer
         * has to be usable while some projects are still being written; the
         * alternative is a bootstrap tool that cannot be run at all until the
         * last project lands.
         *
         * A node package is identified by its directory rather than by a file,
         * which is the one place this check has to look for a directory. Without
         * that, npm would be started in a directory that does not exist and the
         * resulting failure would say nothing about the real cause.
         */
        {
            char probe[RMS_MAX_PATH];
            int  isDirectory = strcmp(step->kind, "node-package") == 0;
            int  present;

            if (strcmp(step->kind, "shell") == 0)
            {
                present = 1;
            }
            else if (rms_join(probe, sizeof probe, g_opts.root, step->path) != 0)
            {
                present = 0;
            }
            else
            {
                present = isDirectory ? rms_dir_exists(probe) : rms_file_exists(probe);
            }

            if (!present)
            {
                printf("  %-32s %-18s not present yet\n", step->id, step->kind);
                skipped++;
                continue;
            }
        }

        printf("  %-32s %-18s %s\n", step->id, step->kind,
               step->note[0] != '\0' ? step->note : "");

        result = run_build_step(step);

        if (result != RMS_OK)
        {
            failed++;

            if (g_opts.stop_on_error)
            {
                RMS_ERROR("stopping after %s failed", step->id);
                return result;
            }

            continue;
        }

        built++;
    }

    rms_step(g_opts.dry_run ? "Build plan" : "Build complete");

    /*
     * A dry run reports a plan rather than a result. Every step reports success without having run, so
     * counting them as successes and printing that would tell an operator that a build had worked when
     * nothing was compiled at all -- and the operator most likely to run a dry run is the one checking
     * whether a change to the manifests is right before letting it touch a machine.
     */
    if (g_opts.dry_run)
    {
        printf("  %d step(s) would run, %d skipped\n", built, skipped);
    }
    else
    {
        printf("  %d succeeded, %d failed, %d skipped\n", built, failed, skipped);
    }

    return failed == 0 ? RMS_OK : RMS_ERR_BUILD;
}

static int
command_publish(void)
{
    int index;

    rms_step("Publish");

    for (index = 0; k_publish_targets[index].project != NULL; index++)
    {
        if (!list_contains(k_publish_targets[index].platforms, host_platform()))
            continue;

        if (publish_project(k_publish_targets[index].project,
                            k_publish_targets[index].output,
                            k_publish_targets[index].template_file,
                            k_publish_targets[index].kind) != RMS_OK)
            return RMS_ERR_BUILD;

        printf("  %-52s -> %s\n",
               k_publish_targets[index].project,
               k_publish_targets[index].output);

        /*
         * The template is named in its own line rather than folded into the line
         * above, because it is the one file in the output that an operator has to
         * do something about. A line that mentions it is a line that gets read.
         */
        if (k_publish_targets[index].template_file != NULL)
            printf("  %-52s    %s (copy to .env and fill in)\n",
                   "",
                   RMS_ENVIRONMENT_TEMPLATE_NAME);
    }

    return RMS_OK;
}

static int
command_deploy(const RequirementList *requirements, const BuildStepList *steps,
               const ServiceList *services)
{
    int result;

    /*
     * Every phase, in the order a machine survives them.
     *
     * The previous deployment is brought down first. A running service holds its
     * own assemblies open, and Windows will not overwrite an open file, so a
     * build that followed without stopping would fail on the first project whose
     * output is in use -- a compiler error that says nothing about the service
     * that caused it.
     *
     * The stop is best effort. It fails for two reasons that are not problems:
     * this is a fresh machine, where nothing is registered and the state asked
     * for already holds, or this process cannot reach the service control
     * manager, in which case the build will fail later and say so in its own
     * words. Both are reported, and neither ends the run.
     *
     * The services are started again at the end, and only once the plan has been
     * applied, so that what runs is what the plan now says. That start is not
     * best effort: a deployment whose files are all correct and whose services
     * answer nothing is not a deployment.
     */
    rms_step("Deploy: stop the previous deployment");

    if (manage_services(services, "stop") != RMS_OK)
    {
        RMS_WARN("the services could not be stopped; continuing, because a machine with nothing "
                 "installed reports the same failure, and an assembly genuinely held open will fail "
                 "the build that follows with a message of its own");
    }

    result = command_bootstrap(requirements);
    if (result != RMS_OK)
        return result;

    /*
     * The toolchain is re-resolved after the bootstrap, because a bootstrap
     * that installed something has just made a tool available that was not
     * there when the run started. Without this, the first deployment on a fresh
     * machine installs MSBuild and then skips every project that needs it.
     */
    resolve_toolchain(requirements);
    if (prepare_environment() != 0)
        return RMS_ERR_IO;

    result = command_build(steps, NULL);
    if (result != RMS_OK)
        return result;

    result = command_publish();
    if (result != RMS_OK)
        return result;

    result = manage_services(services, "install");
    if (result != RMS_OK)
    {
        RMS_WARN("everything was built and published, but the services could not be registered");
        return result;
    }

    rms_step("Deploy: start");

    result = manage_services(services, "start");
    if (result != RMS_OK)
    {
        RMS_WARN("the services are registered but did not start; this machine is not serving");
        return result;
    }

    rms_step("Deployment complete");
    printf("  artifacts  %s\n", g_opts.out);
    printf("  services   running for %s\n", host_platform());
    printf("  status     rmsdeploy services status\n");

    return RMS_OK;
}

static void
print_usage(void)
{
    printf(
        "rmsdeploy -- bootstrapper for the RMS stack\n"
        "\n"
        "  rmsdeploy <command> [options]\n"
        "\n"
        "Commands:\n"
        "  probe                 Report the platform and which requirements are missing.\n"
        "  bootstrap             Download and install only the missing requirements.\n"
        "  build [step-id]       Build every step, or a single named step.\n"
        "  publish               Build and collect publish output under artifacts/.\n"
        "  services <action>     Write the service plan and apply it to this machine:\n"
        "                        install | uninstall | start | stop | restart | status.\n"
        "  deploy                bootstrap, build, publish, then install the services.\n"
        "  help                  Show this text.\n"
        "\n"
        "Options:\n"
        "  --root <dir>          Repository root. Defaults to the directory holding the manifests.\n"
        "  --tools <dir>         Where prerequisites are installed. Defaults to <root>/.tools.\n"
        "  --out <dir>           Publish destination root. Defaults to <root>/artifacts.\n"
        "  --dry-run             Print what would happen without changing the machine.\n"
        "  --verbose             Echo every command before it runs.\n"
        "  --keep-cache          Keep downloaded archives after extracting them.\n"
        "  --continue-on-error   Keep building after a step fails.\n");
}

/* ==========================================================================
 * ENTRY POINT
 * ========================================================================== */

/*
 * Resolve the repository root.
 *
 * The manifests sit beside the executable in a checked-out tree, but during a
 * deployment the executable is often copied to a temporary directory and run
 * from there. Walking up from the executable's own location until a directory
 * containing the requirements manifest is found handles both arrangements
 * without the caller having to pass a flag.
 */
static int
resolve_root(const char *executable, char *root, size_t size)
{
    char directory[RMS_MAX_PATH];
    char candidate[RMS_MAX_PATH];
    int  depth;

    if (g_opts.root[0] != '\0')
    {
        rms_copy(root, size, g_opts.root);
        return 0;
    }

    if (rms_copy(directory, sizeof directory, executable) != 0)
        return -1;

    for (depth = 0; depth < 8; depth++)
    {
        char *separator = strrchr(directory, RMS_SEP);

        if (separator == NULL)
            break;

        *separator = '\0';

        /* The executable may sit inside deploy/, so check the directory itself
         * before its deploy/ child. */
        if (snprintf(candidate, sizeof candidate, "%s%c%s", directory, RMS_SEP,
                     "rmsdeploy.requirements") >= (int)sizeof candidate)
            continue;

        if (rms_file_exists(candidate))
        {
            rms_copy(root, size, directory);
            return 0;
        }

        if (snprintf(candidate, sizeof candidate, "%s%cdeploy%c%s", directory, RMS_SEP,
                     RMS_SEP, "rmsdeploy.requirements") >= (int)sizeof candidate)
            continue;

        if (rms_file_exists(candidate))
        {
            rms_copy(root, size, directory);
            return 0;
        }
    }

    /* Fall back to the working directory, which is where a developer running
     * the program by hand will be. */
#if defined(RMS_WINDOWS)
    if (RMS_GETCWD(root, size) == NULL)
#else
    if (RMS_GETCWD(root, size) == NULL)
#endif
        return -1;

    return 0;
}

int
main(int argc, char *argv[])
{
    char             root[RMS_MAX_PATH];
    char             executable[RMS_MAX_PATH];
    char             manifest_path[RMS_MAX_PATH];
    const char      *command = "help";
    const char      *argument = NULL;
    RequirementList  requirements;
    BuildStepList    steps;
    ServiceList      services;
    int              result = RMS_OK;
    int              index;

    memset(&g_opts, 0, sizeof g_opts);
    memset(&requirements, 0, sizeof requirements);
    memset(&steps, 0, sizeof steps);
    memset(&services, 0, sizeof services);

    g_opts.stop_on_error = 1;

    /* The executable path is captured before anything else so the root can be
     * derived even when the working directory is unrelated to the tree. */
#if defined(RMS_WINDOWS)
    if (GetModuleFileNameA(NULL, executable, (DWORD)sizeof executable) == 0)
        rms_copy(executable, sizeof executable, ".");
#elif defined(RMS_MACOS)
    {
        /* macOS has no /proc, so the path is taken from the first argument when
         * it is absolute, and from the working directory otherwise. */
        if (argc > 0 && argv[0][0] == '/')
            rms_copy(executable, sizeof executable, argv[0]);
        else if (RMS_GETCWD(executable, sizeof executable) == NULL)
            rms_copy(executable, sizeof executable, ".");
    }
#else
    if (readlink("/proc/self/exe", executable, sizeof executable - 1) <= 0)
        rms_copy(executable, sizeof executable, ".");
    else
        executable[sizeof executable - 1] = '\0';
#endif

    if (argc >= 2 && argv[1][0] != '-')
        command = argv[1];

    for (index = 2; index < argc; index++)
    {
        if (strcmp(argv[index], "--root") == 0 && index + 1 < argc)
        {
            rms_copy(g_opts.root, sizeof g_opts.root, argv[++index]);
        }
        else if (strcmp(argv[index], "--tools") == 0 && index + 1 < argc)
        {
            rms_copy(g_opts.tools, sizeof g_opts.tools, argv[++index]);
        }
        else if (strcmp(argv[index], "--out") == 0 && index + 1 < argc)
        {
            rms_copy(g_opts.out, sizeof g_opts.out, argv[++index]);
        }
        else if (strcmp(argv[index], "--dry-run") == 0)
        {
            g_opts.dry_run = 1;
        }
        else if (strcmp(argv[index], "--verbose") == 0)
        {
            g_opts.verbose = 1;
        }
        else if (strcmp(argv[index], "--keep-cache") == 0)
        {
            g_opts.keep_cache = 1;
        }
        else if (strcmp(argv[index], "--continue-on-error") == 0)
        {
            g_opts.stop_on_error = 0;
        }
        else if (argv[index][0] != '-')
        {
            argument = argv[index];
        }
        else
        {
            RMS_ERROR("unknown option: %s", argv[index]);
            print_usage();
            return RMS_ERR_USAGE;
        }
    }

    if (strcmp(command, "help") == 0 || strcmp(command, "--help") == 0)
    {
        print_usage();
        return RMS_OK;
    }

    if (resolve_root(executable, root, sizeof root) != 0)
    {
        RMS_ERROR("the repository root could not be determined; pass --root");
        return RMS_ERR_IO;
    }

    rms_copy(g_opts.root, sizeof g_opts.root, root);

    /*
     * Everything below is derived from these three paths -- the manifests, the
     * publish destinations, the plan file, the log directory and the paths
     * inside the service definitions -- so they are made absolute once, here,
     * before anything is derived from them. A relative --root was the common
     * case, because resolve_root falls back to walking up from the executable,
     * and without this the plan would name a log directory that only resolves
     * from wherever the deployer happened to be run.
     */
    if (rms_absolute(root, sizeof root, g_opts.root) != 0)
        return RMS_ERR_IO;

    rms_copy(g_opts.root, sizeof g_opts.root, root);

    if (g_opts.tools[0] == '\0' && rms_join(g_opts.tools, sizeof g_opts.tools, root, ".tools") != 0)
        return RMS_ERR_IO;

    if (g_opts.out[0] == '\0' && rms_join(g_opts.out, sizeof g_opts.out, root, "artifacts") != 0)
        return RMS_ERR_IO;

    if (rms_absolute(g_opts.tools, sizeof g_opts.tools, g_opts.tools) != 0 ||
        rms_absolute(g_opts.out, sizeof g_opts.out, g_opts.out) != 0)
        return RMS_ERR_IO;

    rms_step("RMS deployer");
    printf("  root    %s\n  tools   %s\n  output  %s\n", g_opts.root, g_opts.tools, g_opts.out);

    if (rms_join(manifest_path, sizeof manifest_path, root, "deploy") != 0)
        return RMS_ERR_IO;

    {
        char requirements_manifest[RMS_MAX_PATH];
        char build_manifest[RMS_MAX_PATH];
        char services_manifest[RMS_MAX_PATH];

        if (rms_join(requirements_manifest, sizeof requirements_manifest, manifest_path,
                     "rmsdeploy.requirements") != 0 ||
            rms_join(build_manifest, sizeof build_manifest, manifest_path,
                     "rmsdeploy.build") != 0 ||
            rms_join(services_manifest, sizeof services_manifest, manifest_path,
                     "rmsdeploy.services") != 0)
            return RMS_ERR_IO;

        if (requirements_load(requirements_manifest, &requirements) != 0)
            return RMS_ERR_IO;

        if (build_steps_load(build_manifest, &steps) != 0)
            return RMS_ERR_IO;

        /* A missing services manifest is tolerated so that the build and
         * publish commands work before anything is runnable. */
        if (rms_file_exists(services_manifest))
        {
            if (services_load(services_manifest, &services) != 0)
                return RMS_ERR_IO;
        }
    }

    resolve_toolchain(&requirements);
    if (prepare_environment() != 0)
        return RMS_ERR_IO;

    if (strcmp(command, "probe") == 0)
        result = command_probe(&requirements);
    else if (strcmp(command, "bootstrap") == 0)
        result = command_bootstrap(&requirements);
    else if (strcmp(command, "build") == 0)
        result = command_build(&steps, argument);
    else if (strcmp(command, "publish") == 0)
        result = command_publish();
    else if (strcmp(command, "services") == 0)
    {
        if (argument == NULL)
        {
            RMS_ERROR("the services command needs an action");
            print_usage();
            result = RMS_ERR_USAGE;
        }
        else
        {
            result = manage_services(&services, argument);
        }
    }
    else if (strcmp(command, "deploy") == 0)
    {
        result = command_deploy(&requirements, &steps, &services);
    }
    else
    {
        RMS_ERROR("unknown command: %s", command);
        print_usage();
        result = RMS_ERR_USAGE;
    }

    free(requirements.items);
    free(steps.items);
    free(services.items);

    return result;
}
