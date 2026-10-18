#!/bin/sh
# =============================================================================
# Build the RMS deployer on Linux and macOS.
# =============================================================================
# The deployer is one translation unit with no third-party dependency, so there
# is nothing to configure and nothing to link beyond the C library. On Windows
# the equivalent script is build.cmd, which has more work to do because the
# compiler does not live somewhere predictable there.
#
# The program downloads archives through curl or wget at run time rather than
# being linked against libcurl, which is why no library appears on this command
# line: a bootstrapper that needed a library would need the library bootstrapped
# first.
#
# Usage:  sh deploy/build.sh
# =============================================================================
set -eu

here=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)

# Prefer an explicitly chosen compiler, then the platform's usual name, then the
# other one. Every supported platform has at least one of these.
if [ -n "${CC:-}" ]; then
    compiler=$CC
elif command -v cc >/dev/null 2>&1; then
    compiler=cc
elif command -v clang >/dev/null 2>&1; then
    compiler=clang
elif command -v gcc >/dev/null 2>&1; then
    compiler=gcc
else
    echo "build.sh: no C compiler found; install cc, clang or gcc" >&2
    exit 1
fi

output="$here/rmsdeploy"

# -Wall -Wextra is the bar the source is written to: it builds free of warnings,
# and the strict flags are what keep it that way. The warnings are not a style
# preference here, because a silent conversion or an unused result in a program
# that installs and starts software is a correctness problem.
echo "build.sh: compiling with $compiler"
"$compiler" \
    -std=c11 \
    -O2 \
    -Wall \
    -Wextra \
    -o "$output" \
    "$here/rmsdeploy.c"

echo "build.sh: built $output"
"$output" help | head -n 3
