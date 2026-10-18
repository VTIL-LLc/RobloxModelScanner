@echo off
rem ===========================================================================
rem  RMS SERVICES -- THE MACHINE HALF OF THE DEPLOYMENT
rem ===========================================================================
rem  Run this from the repository root, or double-click it. It hands the stack
rem  to Windows: one service per long-running program, one IIS site per web
rem  site, so that everything answering a public name is started by the machine
rem  at boot instead of by a console window somebody left open.
rem
rem    services.bat              register what is built, then start it
rem    services.bat deploy       rebuild and republish first, then register and
rem                              start -- the whole path in one word
rem    services.bat install      write the plan and register it, no build
rem    services.bat start        start what is registered
rem    services.bat stop         stop what is registered, and anything left over
rem    services.bat restart      ask the supervisor to restart what is running
rem    services.bat status       what is registered, and what reads its key
rem    services.bat uninstall    remove every entry this stack created
rem    services.bat help         this text
rem
rem  Everything except help, status and the refusals needs elevation, because
rem  registering a service and binding an IIS site are administrator operations.
rem  The script asks for it itself: Windows shows its consent prompt once, and
rem  the work happens in the window it opens. This first window exits at once.
rem
rem  WHY A SCRIPT AND NOT A SERVICE WRAPPER
rem    Wrapping a program in a third-party service shim would put a process
rem    between the service control manager and the program, and then every
rem    question worth asking -- is it running, why did it stop, what did it
rem    print -- would be answered by the shim rather than by the program. The
rem    deployer writes the plan and RMS.ServiceInstaller applies it with the
rem    native mechanism on each platform: the service control manager here,
rem    systemd on Linux, launchd on macOS. This script is the operator's hand on
rem    that switch, nothing more.
rem
rem  WHAT IT STOPS FIRST
rem    The programs it is about to register may already be running, started by
rem    hand from artifacts\local for a first look at the site. Two copies of a
rem    program cannot hold the same port, and a service that cannot bind is
rem    reported as failed when it starts rather than when a request arrives, so
rem    this script stops what the supervisor knows about, then any process of
rem    ours still holding one of these ports, before it starts anything.
rem
rem    It never kills an IIS Express it did not start: the one it stops is
rem    identified by the configuration file it was given, so a debug session in
rem    Visual Studio, which uses its own configuration, is left alone.
rem
rem  IIS
rem    The four web sites are IIS sites, not processes. A Windows machine
rem    without IIS can run every service in this stack and still not serve a
rem    single page of it, so the script checks for IIS before it begins and
rem    offers the one command that adds it. Declining changes nothing.
rem
rem  WHAT `deploy` REPLACES
rem    A publish replaces each web site's published directory. The site's own
rem    data -- accounts, sessions -- lives under App_Data inside that directory,
rem    so a deploy starts the sites with an empty first-run state and writes a
rem    new initial-admin.txt beside the database. `install` does not touch the
rem    published trees at all, which is why it is what this script does when it
rem    is given no verb.
rem
rem  WHAT IT DOES NOT DO
rem    It does not create a secret, write a setting, or edit a Web.config. Those
rem    are configure.bat's work and the deployer's: the sites' key paths are
rem    written into the published Web.config by the deployer at install time,
rem    which is the only thing that knows where this deployment put its
rem    credentials.
rem
rem  EXIT CODES
rem    0  everything asked for happened
rem    2  this is not the repository root
rem    3  the deployer has not been built yet
rem    4  the verb is not one of the ones above
rem    5  elevation was declined
rem    7  IIS is missing and was not installed
rem    N  otherwise the deployer's own code, which is what it reported
rem ===========================================================================

setlocal EnableExtensions DisableDelayedExpansion
title RMS services

rem ---------------------------------------------------------------------------
rem  Where everything is. %~dp0 ends in a separator, which is removed so that
rem  every path below is built the way the deployer builds its own.
rem ---------------------------------------------------------------------------
set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"

set "DEPLOYER=%ROOT%\deploy\rmsdeploy.exe"
set "MANIFEST=%ROOT%\deploy\rmsdeploy.services"
set "APPCMD=%SystemRoot%\system32\inetsrv\appcmd.exe"

if not exist "%MANIFEST%" goto :not_a_repository
if not exist "%DEPLOYER%" goto :no_deployer

rem The one command that turns a Windows install into one that can host these
rem sites. Named once, printed wherever it is needed.
set "IIS_CMD=dism /online /enable-feature /featurename:IIS-WebServerRole /featurename:IIS-WebServer /featurename:IIS-CommonHttpFeatures /featurename:IIS-HttpErrors /featurename:IIS-StaticContent /featurename:IIS-DefaultDocument /featurename:IIS-HttpLogging /featurename:IIS-RequestFiltering /featurename:IIS-NetFxExtensibility45 /featurename:IIS-ASPNET45 /featurename:IIS-ISAPIExtensions /featurename:IIS-ISAPIFilter /featurename:IIS-WebServerManagementTools /featurename:IIS-ManagementConsole /featurename:IIS-ManagementScriptingTools /all /norestart"

rem ---------------------------------------------------------------------------
rem  The verb. Defaulted before anything else, because the elevated copy of this
rem  script is started with it as its only argument and receives nothing else
rem  from this window.
rem ---------------------------------------------------------------------------
set "VERB=%~1"
if not defined VERB set "VERB=install"

for %%v in (install deploy start stop restart status uninstall help) do if /i "%VERB%"=="%%v" goto :verb_known
goto :unknown_verb

:verb_known
if /i "%VERB%"=="help" goto :usage
if /i "%VERB%"=="status" goto :elevated

rem ---------------------------------------------------------------------------
rem  Everything below changes the machine, so it runs as an administrator or it
rem  does not run. Two probes rather than one: `net session` needs the Server
rem  service to answer, and `fltmc` needs the filter manager, and a machine can
rem  be missing either one while being perfectly able to register a service.
rem ---------------------------------------------------------------------------
net session >nul 2>&1
if not errorlevel 1 goto :elevated
fltmc >nul 2>&1
if not errorlevel 1 goto :elevated

echo.
echo   Registering a service, binding an IIS site and writing a key path into a
echo   published Web.config all need administrator rights, and this window does
echo   not have them.
echo.
echo   Windows is about to ask for consent. The work happens in the window it
echo   opens, which stays open at the end so its report can be read. This window
echo   closes as soon as that window is on its way.
echo.
set "RMS_SELF=%~f0"
set "RMS_VERB=%VERB%"
set "RMS_WORKDIR=%ROOT%"
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { Start-Process -FilePath $env:RMS_SELF -ArgumentList $env:RMS_VERB -WorkingDirectory $env:RMS_WORKDIR -Verb RunAs -ErrorAction Stop } catch { exit 1 }"
if errorlevel 1 goto :elevation_refused
exit /b 0

rem ---------------------------------------------------------------------------
rem  IIS, for the verbs that need the sites to exist. A site is not a service,
rem  and the deployer refuses a plan it cannot apply whole rather than leaving
rem  half of it registered, so this is checked before anything is touched.
rem ---------------------------------------------------------------------------
:elevated
set "NEEDS_IIS=0"
if /i "%VERB%"=="install" set "NEEDS_IIS=1"
if /i "%VERB%"=="deploy" set "NEEDS_IIS=1"
if /i "%VERB%"=="start" set "NEEDS_IIS=1"
if /i "%VERB%"=="restart" set "NEEDS_IIS=1"
if "%NEEDS_IIS%"=="0" goto :iis_ready
if exist "%APPCMD%" goto :iis_ready

echo.
echo --------------------------------------------------------------------------
echo   IIS IS NOT INSTALLED
echo --------------------------------------------------------------------------
echo.
echo   This machine has no IIS, and the four web sites in the plan are IIS
echo   sites: the analytics site, the DevOps console, the Configuration console
echo   and the documentation site. Without it the services could be registered
echo   and started, and not one page would answer.
echo.
echo   Adding it is one command, and it is what the Installer feature in Windows
echo   runs:
echo.
echo     %IIS_CMD%
echo.
choice /c YN /n /m "  Install IIS now and continue? [Y/N] "
if errorlevel 2 goto :declined_iis

echo.
echo   Adding IIS. This takes a few minutes and needs nothing from the network
echo   beyond the Windows component store.
echo.
%IIS_CMD%
if errorlevel 1 goto :iis_failed
if not exist "%APPCMD%" goto :iis_failed
echo.
echo   IIS is in place.
echo.

:iis_ready
rem ---------------------------------------------------------------------------
rem  Which verbs do which work. A verb sets the steps, and the steps run in the
rem  order they must run in: nothing starts before what it needs is stopped, and
rem  nothing is registered before the tree it points at exists.
rem ---------------------------------------------------------------------------
set "DO_DEPLOY=0"
set "DO_INSTALL=0"
set "DO_START=0"
set "DO_STOP=0"
set "DO_RESTART=0"
set "DO_UNINSTALL=0"

if /i "%VERB%"=="deploy" (
    set "DO_DEPLOY=1"
)
if /i "%VERB%"=="install" (
    set "DO_STOP=1"
    set "DO_INSTALL=1"
    set "DO_START=1"
)
if /i "%VERB%"=="start" (
    set "DO_START=1"
)
if /i "%VERB%"=="stop" (
    set "DO_STOP=1"
)
if /i "%VERB%"=="restart" (
    set "DO_RESTART=1"
)
if /i "%VERB%"=="uninstall" (
    set "DO_STOP=1"
    set "DO_UNINSTALL=1"
)

if not "%DO_DEPLOY%"=="1" goto :after_deploy
echo.
echo ==========================================================================
echo   DEPLOY
echo ==========================================================================
call :run_deployer deploy
if not "%CODE%"=="0" goto :failed
:after_deploy

if not "%DO_UNINSTALL%"=="1" goto :after_uninstall
call :run_deployer services uninstall
if not "%CODE%"=="0" goto :failed
:after_uninstall

if not "%DO_STOP%"=="1" goto :after_stop
call :stop_stack
:after_stop

if not "%DO_INSTALL%"=="1" goto :after_install
call :run_deployer services install
if not "%CODE%"=="0" goto :failed
:after_install

if not "%DO_START%"=="1" goto :after_start
rem  A start with nothing registered is not an error worth stopping for: it
rem  starts what the plan says should be running, and a plan the installer just
rem  applied is the plan it starts.
call :run_deployer services start
if not "%CODE%"=="0" goto :failed
:after_start

if not "%DO_RESTART%"=="1" goto :after_restart
call :run_deployer services restart
if not "%CODE%"=="0" goto :failed
:after_restart

rem ---------------------------------------------------------------------------
rem  Evidence rather than a claim: what the supervisor holds, and what is
rem  actually accepting connections. A service that is registered and dead looks
rem  identical to a working one in a report that only repeats the plan back.
rem ---------------------------------------------------------------------------
echo.
echo ==========================================================================
echo   WHAT THE SUPERVISOR HOLDS
echo ==========================================================================
call :run_deployer services status

echo.
echo ==========================================================================
echo   WHAT IS LISTENING
echo ==========================================================================
call :report_port 5000 api
call :report_port 5201 devops
call :report_port 5202 configuration
call :report_port 5203 docs
call :report_port 30449 site
call :report_port 8082 devops-site
call :report_port 8083 configuration-site
call :report_port 8084 docs-site

echo.
echo --------------------------------------------------------------------------
echo   DONE
echo --------------------------------------------------------------------------
echo.
if /i "%VERB%"=="status" goto :note_status
if /i "%VERB%"=="stop" goto :note_stop
if /i "%VERB%"=="uninstall" goto :note_uninstall
echo   Every name in the plan is now started by Windows at boot. The programs
echo   that answer them write a log for each run under:
echo.
echo     "%ROOT%\artifacts\logs\<name>\<run id>.log"
echo.
echo   A service that is registered and not listening has said why in the newest
echo   log beside its name. `services.bat status` is the quickest way to see
echo   which of them is in that state.
echo.
echo   The site's first-run account is in the site's own data directory, written
echo   once when the site starts for the first time:
echo.
echo     "%ROOT%\artifacts\site\App_Data\initial-admin.txt"
echo.
goto :end_ok

rem ---------------------------------------------------------------------------
rem  The end blocks for the verbs that did not start anything. A report that says
rem  "started" after a verb that only looked is a report an operator learns not to
rem  trust, so each verb says what it actually did.
rem ---------------------------------------------------------------------------
:note_status
echo   Nothing was changed. What is above is what this machine holds right now,
echo   read from the service control manager and from IIS rather than from the
echo   plan: a service the plan names and the machine does not have is reported
echo   as absent, which is the difference worth knowing about.
echo.
goto :end_ok

:note_stop
echo   Stopped. Anything above that still says listening belongs to a program
echo   this stack does not own -- the programs it does own, and the one IIS
echo   Express it started, were stopped before this report was taken.
echo.
goto :end_ok

:note_uninstall
echo   Removed. Every service, site and unit this stack created is gone; the
echo   published trees, the credentials and the logs are untouched, which is what
echo   makes `services.bat install` able to put the whole thing back without
echo   rebuilding anything.
echo.
goto :end_ok

rem ---------------------------------------------------------------------------
rem  :run_deployer  ARGUMENTS...
rem
rem  Runs one deployer command against this repository and leaves its exit code
rem  in CODE, which is what every caller checks. The command is echoed before it
rem  runs, because the deployer's own report is long and the line above it is
rem  what tells the operator which of them is being read.
rem ---------------------------------------------------------------------------
:run_deployer
echo.
echo   rmsdeploy %*
echo.
"%DEPLOYER%" %* --root "%ROOT%"
set "CODE=%ERRORLEVEL%"
goto :eof

rem ---------------------------------------------------------------------------
rem  :stop_stack
rem
rem  Stops what the supervisor knows about, then anything of ours still standing.
rem  The supervisor's answer is deliberately not checked: a stop that finds
rem  nothing registered has done its job, and a machine that has never been
rem  installed is exactly the machine this script is often first run on.
rem ---------------------------------------------------------------------------
:stop_stack
call :run_deployer services stop
call :kill_leftovers
goto :eof

rem ---------------------------------------------------------------------------
rem  :kill_leftovers
rem
rem  The programs this stack owns, by image name, and the one IIS Express this
rem  repository started. The IIS Express Match is on the configuration file it
rem  was given: a debug session belonging to another configuration is a different
rem  process as far as this script is concerned, and is left running.
rem ---------------------------------------------------------------------------
:kill_leftovers
echo.
echo   stopping anything this repository started by hand
for %%p in (Scanner.MicroApi.exe RMS.Devops.Service.exe RMS.Configuration.Service.exe RMS.Docs.Service.exe RobloxModelScanner.exe Scanner.Indexer.exe) do call :kill_one %%p
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'iisexpress.exe' -and $_.CommandLine -like '*artifacts\local\applicationhost.config*' } | ForEach-Object { Write-Host ('    stopped iisexpress (pid ' + $_.ProcessId + ')'); Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }"
goto :eof

:kill_one
taskkill /F /IM %~1 >nul 2>&1
if not errorlevel 1 echo     stopped %~1
goto :eof

rem ---------------------------------------------------------------------------
rem  :report_port  PORT  NAME
rem
rem  Whether something is accepting connections on a port, asked of the machine
rem  rather than of the plan. A port with no listener is reported as nothing
rem  rather than skipped, because the absence is the useful part.
rem ---------------------------------------------------------------------------
:report_port
set "OPEN=nothing listening"
for /f "tokens=*" %%l in ('netstat -an ^| findstr /i /c:"LISTENING" ^| findstr /c:":%~1 "') do set "OPEN=listening"
echo     port %~1  %~2  -  %OPEN%
goto :eof

rem ---------------------------------------------------------------------------
rem  The ways out that are not the end.
rem ---------------------------------------------------------------------------
:usage
echo.
echo   rms services -- what this machine runs, and where it listens
echo.
echo     services.bat              register what is built, then start it
echo     services.bat deploy       bootstrap, build, publish, install, start
echo     services.bat install      write the plan and register it, no build
echo     services.bat start        start what is registered
echo     services.bat stop         stop what is registered, and anything left over
echo     services.bat restart      restart what is running
echo     services.bat status       what is registered, and what reads its key
echo     services.bat uninstall    remove every entry this stack created
echo     services.bat help         this text
echo.
echo   The deployer itself takes more options than this script passes through --
echo   --dry-run, --tools, --out, --continue-on-error. Call it directly when one
echo   of them is what is wanted:
echo.
echo     "%DEPLOYER%" services install --root "%ROOT%" --dry-run
echo.
goto :end_ok

:not_a_repository
echo.
echo   This script belongs beside the deploy directory, in the repository root.
echo   It is being run from "%ROOT%", where deploy\rmsdeploy.services is not.
echo.
pause
exit /b 2

:no_deployer
echo.
echo   The deployer has not been built, so there is nothing here that knows how
echo   to write a service plan:
echo.
echo     "%DEPLOYER%"
echo.
echo   Build it once, from the repository root:
echo.
echo     deploy\build.cmd
echo.
pause
exit /b 3

:unknown_verb
echo.
echo   "%VERB%" is not a verb this script knows. The ones it does are:
echo.
echo     install  deploy  start  stop  restart  status  uninstall  help
echo.
echo   Run `services.bat help` for what each of them does.
echo.
pause
exit /b 4

:elevation_refused
echo.
echo   Windows did not give this script administrator rights. Nothing was
echo   registered, nothing was started, and nothing on the machine changed.
echo.
echo   Run it again and accept the consent prompt.
echo.
pause
exit /b 5

:declined_iis
echo.
echo   Nothing was changed. IIS is still absent, and the four web sites cannot
echo   be bound without it.
echo.
echo   When the machine is ready for it, add IIS with:
echo.
echo     %IIS_CMD%
echo.
echo   then run this script again.
echo.
exit /b 7

:iis_failed
echo.
echo   IIS was not installed, or the command that installs it reported failure
echo   above. Nothing was registered: a plan applied without its sites would be
echo   half a deployment, and the deployer would have rolled the rest back.
echo.
echo   The full command, to run by hand and read at leisure:
echo.
echo     %IIS_CMD%
echo.
pause
exit /b 7

:failed
if not defined CODE set "CODE=1"
echo.
echo --------------------------------------------------------------------------
echo   STOPPED
echo --------------------------------------------------------------------------
echo.
echo   The deployer reported failure with code %CODE%. Nothing after that step
echo   was run, and what it said is above this block.
echo.
echo   An install that fails leaves nothing registered: the installer rolls back
echo   what it applied rather than leaving a half-built service list. An install
echo   that fails at the service control manager is usually a matter of rights,
echo   and an install that fails at IIS is usually a missing feature.
echo.
pause
exit /b %CODE%

:end_ok
echo.
pause
exit /b 0
