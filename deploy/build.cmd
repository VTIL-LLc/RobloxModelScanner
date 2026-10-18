@echo off
rem ============================================================================
rem Build the RMS deployer on Windows.
rem ============================================================================
rem Unlike the POSIX side, a Windows machine has no guaranteed C compiler, and
rem when one is present its environment is not set up by default. This script
rem therefore looks for a compiler in this order:
rem
rem   1. A compiler already on PATH -- the case for a MinGW or Clang install.
rem   2. cl.exe through the Visual Studio environment, located with vswhere.
rem      This is the same toolchain the requirements manifest installs when the
rem      machine has nothing, so the two agree on what a built machine looks like.
rem   3. clang.exe from an LLVM install, linked with the Windows SDK.
rem
rem Usage:  deploy\build.cmd
rem ============================================================================
setlocal EnableExtensions

set "HERE=%~dp0"
if "%HERE:~-1%"=="\" set "HERE=%HERE:~0,-1%"
set "SOURCE=%HERE%\rmsdeploy.c"
set "OUTPUT=%HERE%\rmsdeploy.exe"

if not exist "%SOURCE%" (
    echo build.cmd: cannot find "%SOURCE%" 1>&2
    exit /b 1
)

rem --------------------------------------------------------------------------
rem 1. A compiler that is already on PATH.
rem
rem MinGW's gcc needs the WinHTTP import library named explicitly. Clang, when
rem it is on PATH, is presumed to have been set up by a developer prompt and is
rem invoked the same way.
rem --------------------------------------------------------------------------
where cl.exe >nul 2>&1
if not errorlevel 1 (
    echo build.cmd: using cl.exe from PATH
    cl /nologo /std:c11 /utf-8 /O2 /W4 /D_CRT_SECURE_NO_WARNINGS /Fe:"%OUTPUT%" "%SOURCE%" winhttp.lib
    if errorlevel 1 exit /b 1
    goto built
)

where gcc.exe >nul 2>&1
if not errorlevel 1 (
    echo build.cmd: using gcc.exe from PATH
    gcc -std=c11 -O2 -Wall -Wextra -o "%OUTPUT%" "%SOURCE%" -lwinhttp
    if errorlevel 1 exit /b 1
    goto built
)

rem --------------------------------------------------------------------------
rem 2. cl.exe through the Visual Studio build environment.
rem
rem vswhere is the supported way to find a Visual Studio installation. The
rem -products * flag matters because the Build Tools are a separate product from
rem the IDE, and looking only for the IDE would miss them.
rem --------------------------------------------------------------------------
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" set "VSWHERE=%ProgramFiles%\Microsoft Visual Studio\Installer\vswhere.exe"

if exist "%VSWHERE%" (
    set "VSPATH="
    for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSPATH=%%i"

    if defined VSPATH (
        if exist "%VSPATH%\VC\Auxiliary\Build\vcvars64.bat" (
            echo build.cmd: using cl.exe from "%VSPATH%"
            call "%VSPATH%\VC\Auxiliary\Build\vcvars64.bat" >nul
            cl /nologo /std:c11 /utf-8 /O2 /W4 /D_CRT_SECURE_NO_WARNINGS /Fe:"%OUTPUT%" "%SOURCE%" winhttp.lib
            if errorlevel 1 exit /b 1
            goto built
        )
    )
)

rem --------------------------------------------------------------------------
rem 3. clang.exe from an LLVM install.
rem
rem Clang can compile the source here, but linking requires the MSVC toolchain's
rem C runtime, which an LLVM-only machine does not have. It is tried because on a
rem machine that has both, clang is a perfectly good compiler; on a machine that
rem has only clang the link fails and the error names the missing library.
rem --------------------------------------------------------------------------
set "CLANG="
if exist "%ProgramFiles%\LLVM\bin\clang.exe" set "CLANG=%ProgramFiles%\LLVM\bin\clang.exe"
if not defined CLANG if exist "%ProgramFiles(x86)%\LLVM\bin\clang.exe" set "CLANG=%ProgramFiles(x86)%\LLVM\bin\clang.exe"

if defined CLANG (
    echo build.cmd: using "%CLANG%"
    "%CLANG%" -std=c11 -O2 -Wall -Wextra -o "%OUTPUT%" "%SOURCE%" -lwinhttp
    if errorlevel 1 (
        echo build.cmd: clang could not link the program. 1>&2
        echo build.cmd: install the Visual Studio Build Tools and re-run. 1>&2
        exit /b 1
    )
    goto built
)

echo build.cmd: no C compiler found. 1>&2
echo build.cmd: run "rmsdeploy bootstrap" to install the build tools, or install 1>&2
echo build.cmd: the Visual Studio Build Tools by hand. 1>&2
exit /b 1

:built
if not exist "%OUTPUT%" (
    echo build.cmd: the compiler reported success but "%OUTPUT%" is missing 1>&2
    exit /b 1
)
echo build.cmd: built %OUTPUT%
"%OUTPUT%" help
exit /b 0
