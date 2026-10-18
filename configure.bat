@echo off
rem ===========================================================================
rem  RMS CONFIGURATION -- THE FORM
rem ===========================================================================
rem  Run this from the repository root. It asks for the values a deployment
rem  cannot fill in for itself, and hands them to configure.ps1, which writes
rem  them:
rem
rem    artifacts\credentials\    one file per secret, holding the value and
rem                              nothing else
rem      api-key                 the API's key, which is also the key the
rem                              scanner's own site presents
rem      indexer.token           the indexer's control token
rem      devops.token            the DevOps service, and its site
rem      configuration.token     the Configuration service, and its site
rem      docs.token              the documentation service, and its site
rem
rem    artifacts\scanner\.env    the scanner's settings, in the environment file
rem                              the scanner reads from its working directory.
rem                              The deployed scanner's working directory is
rem                              artifacts\scanner, so that is where its copy
rem                              belongs.
rem
rem    .env                      the same cookie, webhooks and start id, mirrored
rem                              to the repository root, which is what a scanner
rem                              started by hand reads. Asked for, not assumed.
rem
rem  WHY THIS IS A SCRIPT AND NOT PART OF THE DEPLOYER
rem    The deployer creates the shape of a deployment and never invents a secret:
rem    a value it wrote would be a value nobody chose. Only the operator holds a
rem    session cookie, and only the operator owns a Discord server. This script is
rem    the operator's half of that split. It can be run before the first deploy
rem    or after it, and re-run whenever a value changes.
rem
rem  WHY THE WRITING GOES THROUGH POWERSHELL
rem    A session cookie contains a pipe character. cmd splits a line on a pipe
rem    that is not inside quotes, so no value here is ever carried through an
rem    echo or an expansion: every value is placed in the environment, where cmd
rem    does not read it, and configure.ps1 writes it from there.
rem
rem  WHAT THIS SCRIPT DELIBERATELY DOES NOT TOUCH
rem    The four sites' Web.config. It is a publish artifact: MSBuild rewrites it
rem    on every publish, and the one value in it that must survive that -- the
rem    path of the site's key file -- is written by the deployer at install time,
rem    which is the only thing that knows where the deployment put its
rem    credentials. An edit made here would be an edit the next publish erases.
rem
rem  NOTHING ALREADY SET IS EVER PRINTED BACK, and no value is echoed, including
rem  the one just written. Pressing Enter keeps what a field already holds.
rem ===========================================================================

setlocal EnableExtensions DisableDelayedExpansion
title RMS configuration

rem ---------------------------------------------------------------------------
rem  Where everything is. The script lives in the repository root, so %~dp0 is
rem  that directory with a trailing separator, removed so that every path below
rem  is built the way the deployer builds its own.
rem ---------------------------------------------------------------------------
set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"

if not exist "%ROOT%\deploy\templates\scanner.env" goto :not_a_repository

set "CREDDIR=%ROOT%\artifacts\credentials"
set "SCANENV=%ROOT%\artifacts\scanner\.env"
set "SCANTMPL=%ROOT%\artifacts\scanner\.env.template"
set "REPOTMPL=%ROOT%\deploy\templates\scanner.env"
set "ROOTENV=%ROOT%\.env"
set "ROOTEXA=%ROOT%\.env.example"
set "WRITER=%ROOT%\configure.ps1"

rem  The prefix configure.ps1 looks for, the settings it is allowed to replace,
rem  and the shortest credential every surface in this stack accepts.
rem
rem  The settings are listed rather than inferred from which of them happen to
rem  hold something. A variable set to nothing is a variable cmd removes from
rem  the environment, so "not present" cannot be told apart from "present and
rem  empty" -- and that distinction is the whole of what the word clear does.
rem  A setting on this list and absent from the environment is therefore written
rem  as an empty value, which is what every one of them documents as meaning
rem  "use the program's own default".
set "PREFIX=RMSVALUE_"
set "MANAGED=MAIN_WEBHOOK;AUTODOWNLOAD_WEBHOOK;CATALOGUE_PATH;MODEL_DIRECTORY;CONTROL_PATH;SCAN_MODE;START_ID;BATCH_SIZE;SCAN_CONCURRENCY;ASSET_CONCURRENCY;REQUEST_INTERVAL_MS;FLUSH_SECONDS;FLUSH_ROWS;KEEP_SCORE;KEEP_MODELS;IGNORED_CREATORS;VERBOSE"
set "MINIMUM=32"

set "GENLIST="
set "SKIPTUNING="
set "SKIPPATHS="
set "MIRROR="
set "HARDEN="

echo.
echo ===========================================================================
echo   RMS CONFIGURATION
echo ===========================================================================
echo.
echo   Repository    "%ROOT%"
echo.
echo   Credentials   "%CREDDIR%"
echo   Scanner env   "%SCANENV%"
echo.
echo   Enter keeps whatever a field already holds. Type  clear  to empty a
echo   value. What you type is visible on screen as you type it, which is what a
echo   console prompt is; a value that is already set is never shown.
echo.

rem ---------------------------------------------------------------------------
rem  What the fields already hold.
rem
rem  The scanner's own environment file is read first. A scanner that has never
rem  been configured has none, so its published template is read instead, and
rem  failing that the template in the repository -- the same file, in the same
rem  order, so that a first run fills in the file the deployment expects rather
rem  than one this script invented.
rem
rem  The session cookie is deliberately not among the values read here, and not
rem  among the ones handed back to the writer below. A Roblox cookie begins with a
rem  vertical bar -- "_|WARNING:..." -- and a vertical bar is a pipe to cmd: a for
rem  loop that assigns one hands back the single character before the bar, and a
rem  line that expands one is read as two commands. A wizard that read and rewrote
rem  that value would replace a working cookie with a stub, and the only visible
rem  difference would be that nothing downloads any more -- the same symptom as a
rem  cookie that has simply expired. So it is left alone: the line already in the
rem  file is carried through untouched, and a new cookie is pasted into the file.
rem ---------------------------------------------------------------------------
set "C_MAIN_WEBHOOK="
set "C_AUTODOWNLOAD_WEBHOOK="
set "C_CATALOGUE_PATH="
set "C_MODEL_DIRECTORY="
set "C_CONTROL_PATH="
set "C_SCAN_MODE="
set "C_START_ID="
set "C_BATCH_SIZE="
set "C_SCAN_CONCURRENCY="
set "C_ASSET_CONCURRENCY="
set "C_REQUEST_INTERVAL_MS="
set "C_FLUSH_SECONDS="
set "C_FLUSH_ROWS="
set "C_KEEP_SCORE="
set "C_KEEP_MODELS="
set "C_IGNORED_CREATORS="
set "C_VERBOSE="

set "ENVBASE="
if exist "%SCANENV%" set "ENVBASE=%SCANENV%"
if not defined ENVBASE if exist "%SCANTMPL%" set "ENVBASE=%SCANTMPL%"
if not defined ENVBASE if exist "%REPOTMPL%" set "ENVBASE=%REPOTMPL%"
if not defined ENVBASE goto :no_template

if not exist "%SCANENV%" goto :scanenv_read_done
for /f "usebackq eol=# tokens=1,* delims==" %%A in ("%SCANENV%") do (
    if /i "%%A"=="MAIN_WEBHOOK" set "C_MAIN_WEBHOOK=%%B"
    if /i "%%A"=="AUTODOWNLOAD_WEBHOOK" set "C_AUTODOWNLOAD_WEBHOOK=%%B"
    if /i "%%A"=="CATALOGUE_PATH" set "C_CATALOGUE_PATH=%%B"
    if /i "%%A"=="MODEL_DIRECTORY" set "C_MODEL_DIRECTORY=%%B"
    if /i "%%A"=="CONTROL_PATH" set "C_CONTROL_PATH=%%B"
    if /i "%%A"=="SCAN_MODE" set "C_SCAN_MODE=%%B"
    if /i "%%A"=="START_ID" set "C_START_ID=%%B"
    if /i "%%A"=="BATCH_SIZE" set "C_BATCH_SIZE=%%B"
    if /i "%%A"=="SCAN_CONCURRENCY" set "C_SCAN_CONCURRENCY=%%B"
    if /i "%%A"=="ASSET_CONCURRENCY" set "C_ASSET_CONCURRENCY=%%B"
    if /i "%%A"=="REQUEST_INTERVAL_MS" set "C_REQUEST_INTERVAL_MS=%%B"
    if /i "%%A"=="FLUSH_SECONDS" set "C_FLUSH_SECONDS=%%B"
    if /i "%%A"=="FLUSH_ROWS" set "C_FLUSH_ROWS=%%B"
    if /i "%%A"=="KEEP_SCORE" set "C_KEEP_SCORE=%%B"
    if /i "%%A"=="KEEP_MODELS" set "C_KEEP_MODELS=%%B"
    if /i "%%A"=="IGNORED_CREATORS" set "C_IGNORED_CREATORS=%%B"
    if /i "%%A"=="VERBOSE" set "C_VERBOSE=%%B"
)
:scanenv_read_done

rem  The four values only the operator has are also mirrored at the repository
rem  root, so a field that has not been filled in there yet is offered the value
rem  from the other file rather than an empty prompt.
if exist "%ROOTENV%" goto :consider_root_values
if exist "%ROOTEXA%" goto :consider_root_values
goto :credentials_read

:consider_root_values
set "ROOTBASE=%ROOTENV%"
if not exist "%ROOTBASE%" set "ROOTBASE=%ROOTEXA%"
for /f "usebackq eol=# tokens=1,* delims==" %%A in ("%ROOTBASE%") do (
    if /i "%%A"=="MAIN_WEBHOOK" if not defined C_MAIN_WEBHOOK set "C_MAIN_WEBHOOK=%%B"
    if /i "%%A"=="AUTODOWNLOAD_WEBHOOK" if not defined C_AUTODOWNLOAD_WEBHOOK set "C_AUTODOWNLOAD_WEBHOOK=%%B"
    if /i "%%A"=="START_ID" if not defined C_START_ID set "C_START_ID=%%B"
)

:credentials_read
set "C_APIKEY="
set "C_INDEXER="
set "C_DEVOPS="
set "C_CONFIGURATION="
set "C_DOCS="
call :readfile "%CREDDIR%\api-key" C_APIKEY
call :readfile "%CREDDIR%\indexer.token" C_INDEXER
call :readfile "%CREDDIR%\devops.token" C_DEVOPS
call :readfile "%CREDDIR%\configuration.token" C_CONFIGURATION
call :readfile "%CREDDIR%\docs.token" C_DOCS

rem ---------------------------------------------------------------------------
rem  The credentials.
rem ---------------------------------------------------------------------------
echo --------------------------------------------------------------------------
echo   CREDENTIALS
echo --------------------------------------------------------------------------
echo.
echo   Five files, one value each. The API's key is also the key the scanner's
echo   own site presents; each token is one service's, and that service's site's.
echo   A surface refuses a credential shorter than %MINIMUM% characters and
echo   answers nothing rather than answering openly, so a value shorter than
echo   that disables the surface it was meant to open.
echo.

call :field "1/5  api-key  -  the API, and the scanner site that reads it" "%C_APIKEY%" "Enter=keep, g=generate a new one, or paste one" gen-guid
if /i "%ANS_DECISION%"=="keep" set "ANS=%C_APIKEY%"
set "C_APIKEY=%ANS%"
set "S_APIKEY=leave the file as it is"
if defined ANS set "S_APIKEY=keep the value on disk"
if /i "%ANS_DECISION%"=="generate" set "C_APIKEY="
if /i "%ANS_DECISION%"=="generate" set "S_APIKEY=write a newly generated value"
if /i "%ANS_DECISION%"=="value" set "S_APIKEY=write the value that was entered"
if /i "%ANS_DECISION%"=="generate" set "GENLIST=%GENLIST%;api-key"

call :field "2/5  indexer.token  -  the indexer's control surface" "%C_INDEXER%" "Enter=keep, g=generate a new one, or paste one" gen-token
if /i "%ANS_DECISION%"=="keep" set "ANS=%C_INDEXER%"
set "C_INDEXER=%ANS%"
set "S_INDEXER=leave the file as it is"
if defined ANS set "S_INDEXER=keep the value on disk"
if /i "%ANS_DECISION%"=="generate" set "C_INDEXER="
if /i "%ANS_DECISION%"=="generate" set "S_INDEXER=write a newly generated value"
if /i "%ANS_DECISION%"=="value" set "S_INDEXER=write the value that was entered"
if /i "%ANS_DECISION%"=="generate" set "GENLIST=%GENLIST%;indexer.token"

call :field "3/5  devops.token  -  the DevOps service, and its site" "%C_DEVOPS%" "Enter=keep, g=generate a new one, or paste one" gen-token
if /i "%ANS_DECISION%"=="keep" set "ANS=%C_DEVOPS%"
set "C_DEVOPS=%ANS%"
set "S_DEVOPS=leave the file as it is"
if defined ANS set "S_DEVOPS=keep the value on disk"
if /i "%ANS_DECISION%"=="generate" set "C_DEVOPS="
if /i "%ANS_DECISION%"=="generate" set "S_DEVOPS=write a newly generated value"
if /i "%ANS_DECISION%"=="value" set "S_DEVOPS=write the value that was entered"
if /i "%ANS_DECISION%"=="generate" set "GENLIST=%GENLIST%;devops.token"

call :field "4/5  configuration.token  -  the Configuration service, and its site" "%C_CONFIGURATION%" "Enter=keep, g=generate a new one, or paste one" gen-token
if /i "%ANS_DECISION%"=="keep" set "ANS=%C_CONFIGURATION%"
set "C_CONFIGURATION=%ANS%"
set "S_CONFIGURATION=leave the file as it is"
if defined ANS set "S_CONFIGURATION=keep the value on disk"
if /i "%ANS_DECISION%"=="generate" set "C_CONFIGURATION="
if /i "%ANS_DECISION%"=="generate" set "S_CONFIGURATION=write a newly generated value"
if /i "%ANS_DECISION%"=="value" set "S_CONFIGURATION=write the value that was entered"
if /i "%ANS_DECISION%"=="generate" set "GENLIST=%GENLIST%;configuration.token"

call :field "5/5  docs.token  -  the documentation service, and its site" "%C_DOCS%" "Enter=keep, g=generate a new one, or paste one" gen-token
if /i "%ANS_DECISION%"=="keep" set "ANS=%C_DOCS%"
set "C_DOCS=%ANS%"
set "S_DOCS=leave the file as it is"
if defined ANS set "S_DOCS=keep the value on disk"
if /i "%ANS_DECISION%"=="generate" set "C_DOCS="
if /i "%ANS_DECISION%"=="generate" set "S_DOCS=write a newly generated value"
if /i "%ANS_DECISION%"=="value" set "S_DOCS=write the value that was entered"
if /i "%ANS_DECISION%"=="generate" set "GENLIST=%GENLIST%;docs.token"

rem ---------------------------------------------------------------------------
rem  The scanner's identity: the two things only the operator has, and the place
rem  a sweep begins.
rem ---------------------------------------------------------------------------
echo.
echo --------------------------------------------------------------------------
echo   SCANNER IDENTITY
echo --------------------------------------------------------------------------
echo.
echo   The session cookie is what makes asset delivery answer. Without it the
echo   scanner still discovers models and reads the catalogue, but no payload is
echo   fetched, so a detection is recorded without the script that caused it.
echo   Nothing is kept on disk either.
echo.
echo   The cookie is not asked for here, and this script does not write it. Its
echo   value begins with a vertical bar, which a command shell reads as a pipe, so
echo   a wizard that handled it would cut it in half and save the half -- leaving a
echo   cookie that looks present and works as though it were absent. The line in
echo.
echo   %SCANENV%
echo.
echo   is carried through untouched. Paste a new value into that file directly,
echo   after the "=" with nothing else on the line, and restart the scanner and the
echo   API. The value is the .ROBLOSECURITY entry from a signed-in browser.
echo.

call :field "MAIN_WEBHOOK  -  where a run summary is posted" "%C_MAIN_WEBHOOK%" "Enter=keep, clear, or paste the URL" plain
if /i "%ANS_DECISION%"=="keep" set "ANS=%C_MAIN_WEBHOOK%"
set "C_MAIN_WEBHOOK=%ANS%"

call :field "AUTODOWNLOAD_WEBHOOK  -  where each kept payload is posted" "%C_AUTODOWNLOAD_WEBHOOK%" "Enter=keep, clear, or paste the URL" plain
if /i "%ANS_DECISION%"=="keep" set "ANS=%C_AUTODOWNLOAD_WEBHOOK%"
set "C_AUTODOWNLOAD_WEBHOOK=%ANS%"

call :field "START_ID  -  the asset a sweep begins at. Empty uses 16848923941" "%C_START_ID%" "Enter=keep, clear, or type an asset id" plain
if /i "%ANS_DECISION%"=="keep" set "ANS=%C_START_ID%"
set "C_START_ID=%ANS%"

rem ---------------------------------------------------------------------------
rem  The pass. Every one of these has a default the scanner already applies when
rem  the setting is empty, so answering nothing here leaves the program's own
rem  behaviour in place rather than a value this script guessed.
rem ---------------------------------------------------------------------------
echo.
set /p "SKIPTUNING=  Ask how the scan itself should run? [y/N] "
if /i not "%SKIPTUNING%"=="y" goto :tuning_done

echo.
call :field "SCAN_MODE  -  sweep walks forward, backfill repairs what the catalogue could not describe" "%C_SCAN_MODE%" "Enter=keep, clear, or type sweep or backfill" plain
if /i "%ANS_DECISION%"=="keep" set "ANS=%C_SCAN_MODE%"
set "C_SCAN_MODE=%ANS%"

call :field "BATCH_SIZE  -  identifiers per catalogue request. Empty uses 99, and the endpoint refuses more than 250" "%C_BATCH_SIZE%" "Enter=keep, clear, or type a number" plain
if /i "%ANS_DECISION%"=="keep" set "ANS=%C_BATCH_SIZE%"
set "C_BATCH_SIZE=%ANS%"

call :field "SCAN_CONCURRENCY  -  models inspected at once. Empty uses 16" "%C_SCAN_CONCURRENCY%" "Enter=keep, clear, or type a number" plain
if /i "%ANS_DECISION%"=="keep" set "ANS=%C_SCAN_CONCURRENCY%"
set "C_SCAN_CONCURRENCY=%ANS%"

call :field "ASSET_CONCURRENCY  -  payloads fetched at once. Empty uses 64" "%C_ASSET_CONCURRENCY%" "Enter=keep, clear, or type a number" plain
if /i "%ANS_DECISION%"=="keep" set "ANS=%C_ASSET_CONCURRENCY%"
set "C_ASSET_CONCURRENCY=%ANS%"

call :field "REQUEST_INTERVAL_MS  -  least time between two requests. Empty is unpaced; the live endpoint throttles near two a second" "%C_REQUEST_INTERVAL_MS%" "Enter=keep, clear, or type a number of milliseconds" plain
if /i "%ANS_DECISION%"=="keep" set "ANS=%C_REQUEST_INTERVAL_MS%"
set "C_REQUEST_INTERVAL_MS=%ANS%"

call :field "FLUSH_SECONDS  -  longest a change may sit unsaved. Empty uses 15" "%C_FLUSH_SECONDS%" "Enter=keep, clear, or type a number of seconds" plain
if /i "%ANS_DECISION%"=="keep" set "ANS=%C_FLUSH_SECONDS%"
set "C_FLUSH_SECONDS=%ANS%"

call :field "FLUSH_ROWS  -  unsaved rows that force a write early. Empty uses 250" "%C_FLUSH_ROWS%" "Enter=keep, clear, or type a number" plain
if /i "%ANS_DECISION%"=="keep" set "ANS=%C_FLUSH_ROWS%"
set "C_FLUSH_ROWS=%ANS%"

call :field "KEEP_SCORE  -  the lowest score whose payload is kept. Empty uses 3" "%C_KEEP_SCORE%" "Enter=keep, clear, or type a score" plain
if /i "%ANS_DECISION%"=="keep" set "ANS=%C_KEEP_SCORE%"
set "C_KEEP_SCORE=%ANS%"

call :field "KEEP_MODELS  -  whether payloads are written to disk at all. Empty uses true" "%C_KEEP_MODELS%" "Enter=keep, clear, or type true or false" plain
if /i "%ANS_DECISION%"=="keep" set "ANS=%C_KEEP_MODELS%"
set "C_KEEP_MODELS=%ANS%"

call :field "IGNORED_CREATORS  -  creators to skip, comma separated. Empty uses the built-in set" "%C_IGNORED_CREATORS%" "Enter=keep, clear, or type a comma separated list" plain
if /i "%ANS_DECISION%"=="keep" set "ANS=%C_IGNORED_CREATORS%"
set "C_IGNORED_CREATORS=%ANS%"

call :field "VERBOSE  -  log every inspection rather than only the matches. Empty uses false" "%C_VERBOSE%" "Enter=keep, clear, or type true or false" plain
if /i "%ANS_DECISION%"=="keep" set "ANS=%C_VERBOSE%"
set "C_VERBOSE=%ANS%"

:tuning_done

rem ---------------------------------------------------------------------------
rem  The paths. A deployment sets all three in the service definition and those
rem  values win, so these are for a scanner run by hand: they are what makes a
rem  hand-run scanner and a deployed one look at the same catalogue.
rem ---------------------------------------------------------------------------
echo.
set /p "SKIPPATHS=  Set the catalogue, payload and control paths for a scanner run by hand? [y/N] "
if /i not "%SKIPPATHS%"=="y" goto :paths_done

echo.
call :field "CATALOGUE_PATH  -  where discovered models are recorded" "%C_CATALOGUE_PATH%" "Enter=keep, clear, or type a path" plain
if /i "%ANS_DECISION%"=="keep" set "ANS=%C_CATALOGUE_PATH%"
set "C_CATALOGUE_PATH=%ANS%"

call :field "MODEL_DIRECTORY  -  where kept payloads are written" "%C_MODEL_DIRECTORY%" "Enter=keep, clear, or type a path" plain
if /i "%ANS_DECISION%"=="keep" set "ANS=%C_MODEL_DIRECTORY%"
set "C_MODEL_DIRECTORY=%ANS%"

call :field "CONTROL_PATH  -  the file the control surface reads" "%C_CONTROL_PATH%" "Enter=keep, clear, or type a path" plain
if /i "%ANS_DECISION%"=="keep" set "ANS=%C_CONTROL_PATH%"
set "C_CONTROL_PATH=%ANS%"

:paths_done

rem ---------------------------------------------------------------------------
rem  The two questions that decide how far this reaches. Both are asked rather
rem  than assumed, because both change a machine.
rem ---------------------------------------------------------------------------
echo.
echo --------------------------------------------------------------------------
echo   TWO CHOICES
echo --------------------------------------------------------------------------
echo.

rem  The repository copy. It exists already on most machines and holds the same
rem  cookie; a scanner started by hand from the repository root reads it, so
rem  leaving it behind is how the two copies drift apart. The prompt carries no
rem  quotes around the path, because a quote inside a set /p prompt ends it.
rem
rem  An empty answer is not the default here: pressing Enter at a set /p prompt
rem  leaves the variable unset, so the yes-branch has to be the fallthrough
rem  rather than a comparison against the character that was meant to stand for
rem  it. That is also why the prompt spells the default out in words.
echo   Mirror the cookie, the webhooks and START_ID into the repository root?
echo   That is the file a scanner started by hand reads:
echo.
echo     "%ROOTENV%"
echo.
set "MIRROR="
set /p "MIRROR=  Mirror them? [Y/n] "
if /i "%MIRROR%"=="n" (set "MIRROR=n") else (set "MIRROR=y")
if /i not "%MIRROR%"=="y" set "ROOTBASE="
if /i not "%MIRROR%"=="y" set "ROOTENV="

rem  The permissions. Inherited access is removed and three grants are made: the
rem  operator, SYSTEM, and IIS's worker identities, which is what the four sites
rem  read their keys as. Anything broader is what this undoes.
echo.
echo   Restricting permissions removes inherited access from each file written,
echo   and leaves the operator, SYSTEM and IIS's worker identities. A site hosted
echo   by IIS reads its key as an application pool identity, so a narrower set of
echo   grants would break the site rather than tighten it.
set "HARDEN=n"
set /p "HARDEN=  Restrict what can read the files this writes? [y/N] "
if /i not "%HARDEN%"=="y" set "HARDEN=n"

rem ---------------------------------------------------------------------------
rem  What that adds up to, before anything is written.
rem ---------------------------------------------------------------------------
echo.
echo --------------------------------------------------------------------------
echo   WHAT WILL BE WRITTEN
echo --------------------------------------------------------------------------
echo.
echo   artifacts\credentials\api-key              %S_APIKEY%
echo   artifacts\credentials\indexer.token        %S_INDEXER%
echo   artifacts\credentials\devops.token         %S_DEVOPS%
echo   artifacts\credentials\configuration.token  %S_CONFIGURATION%
echo   artifacts\credentials\docs.token           %S_DOCS%
echo.
echo   artifacts\scanner\.env                     fill in the settings above, keeping the file's comments, order and line ending
if defined ROOTENV echo   .env                                       fill in the same cookie, webhooks and start id
if not defined ROOTENV echo   .env                                       left as it is
if /i "%SKIPTUNING%"=="y" echo   pass tuning                                filled in
if /i not "%SKIPTUNING%"=="y" echo   pass tuning                                left as it is
if /i "%SKIPPATHS%"=="y" echo   hand-run paths                             filled in
if /i not "%SKIPPATHS%"=="y" echo   hand-run paths                             left as it is
if /i "%HARDEN%"=="y" echo   permissions                                restricted on everything written
if /i not "%HARDEN%"=="y" echo   permissions                                left as they are
echo.

set "CONFIRM="
set /p "CONFIRM=  Write this? [Y/n] "
if /i "%CONFIRM%"=="n" goto :declined

rem ---------------------------------------------------------------------------
rem  Hand over. Every value travels in the environment, which is the only channel
rem  cmd passes through without reading what is in it.
rem ---------------------------------------------------------------------------
echo.
echo --------------------------------------------------------------------------
echo   WRITING
echo --------------------------------------------------------------------------
echo.

set "RMS_CRED_DIR=%CREDDIR%"
set "RMS_CRED_NAMES=api-key;indexer.token;devops.token;configuration.token;docs.token"
set "RMS_CRED_VARS=RMSCRED_APIKEY;RMSCRED_INDEXER;RMSCRED_DEVOPS;RMSCRED_CONFIGURATION;RMSCRED_DOCS"
set "RMS_CRED_GENERATE=%GENLIST%"
set "RMS_CRED_MINIMUM=%MINIMUM%"
set "RMS_ENV_PREFIX=%PREFIX%"
set "RMS_ENV_KEYS=%MANAGED%"
set "RMS_ENV_BASE=%ENVBASE%"
set "RMS_ENV_TARGET=%SCANENV%"
set "RMS_ROOT_BASE=%ROOTBASE%"
set "RMS_ROOT_TARGET=%ROOTENV%"
set "RMS_HARDEN=0"
if /i "%HARDEN%"=="y" set "RMS_HARDEN=1"

set "RMSCRED_APIKEY=%C_APIKEY%"
set "RMSCRED_INDEXER=%C_INDEXER%"
set "RMSCRED_DEVOPS=%C_DEVOPS%"
set "RMSCRED_CONFIGURATION=%C_CONFIGURATION%"
set "RMSCRED_DOCS=%C_DOCS%"

set "%PREFIX%MAIN_WEBHOOK=%C_MAIN_WEBHOOK%"
set "%PREFIX%AUTODOWNLOAD_WEBHOOK=%C_AUTODOWNLOAD_WEBHOOK%"
set "%PREFIX%CATALOGUE_PATH=%C_CATALOGUE_PATH%"
set "%PREFIX%MODEL_DIRECTORY=%C_MODEL_DIRECTORY%"
set "%PREFIX%CONTROL_PATH=%C_CONTROL_PATH%"
set "%PREFIX%SCAN_MODE=%C_SCAN_MODE%"
set "%PREFIX%START_ID=%C_START_ID%"
set "%PREFIX%BATCH_SIZE=%C_BATCH_SIZE%"
set "%PREFIX%SCAN_CONCURRENCY=%C_SCAN_CONCURRENCY%"
set "%PREFIX%ASSET_CONCURRENCY=%C_ASSET_CONCURRENCY%"
set "%PREFIX%REQUEST_INTERVAL_MS=%C_REQUEST_INTERVAL_MS%"
set "%PREFIX%FLUSH_SECONDS=%C_FLUSH_SECONDS%"
set "%PREFIX%FLUSH_ROWS=%C_FLUSH_ROWS%"
set "%PREFIX%KEEP_SCORE=%C_KEEP_SCORE%"
set "%PREFIX%KEEP_MODELS=%C_KEEP_MODELS%"
set "%PREFIX%IGNORED_CREATORS=%C_IGNORED_CREATORS%"
set "%PREFIX%VERBOSE=%C_VERBOSE%"

if not exist "%WRITER%" goto :no_writer
powershell -NoProfile -ExecutionPolicy Bypass -File "%WRITER%"
if not "%ERRORLEVEL%"=="0" goto :write_failed

echo.
echo --------------------------------------------------------------------------
echo   DONE
echo --------------------------------------------------------------------------
echo.
echo   Nothing else has to happen if this machine was deployed by rmsdeploy: every
echo   service and every site is handed the path of its credential, and reads it
echo   the next time it answers. A service picks up a changed value when it is
echo   restarted, and a site when its application pool recycles.
echo.
echo   If the deployment has not been installed yet, the paths in the published
echo   Web.config files are written by the deployer, not here:
echo.
echo     rmsdeploy deploy                bootstrap, build, publish, install, start
echo     rmsdeploy services install      writes the key paths into Web.config
echo     rmsdeploy services status       reports which credential files are present
echo.
echo   The scanner's .env survives a republish. The deployer replaces each web
echo   site's published directory and merges into the scanner's, so only the
echo   published .env.template is rewritten. The file this script writes is .env,
echo   which is the one the scanner reads.
echo.
goto :end_ok

rem ---------------------------------------------------------------------------
rem  :readfile  PATH  VARIABLE
rem
rem  Puts the contents of a file into a named variable. One line is read, which
rem  is what a credential file holds; a file with more lines keeps only the first.
rem  A file that is not there leaves the variable undefined rather than empty,
rem  and the caller treats both as nothing set.
rem ---------------------------------------------------------------------------
:readfile
set "%~2="
if not exist "%~1" goto :eof
set /p "%~2="<"%~1"
goto :eof

rem ---------------------------------------------------------------------------
rem  :field  LABEL  CURRENT  PROMPT  GENERATOR
rem
rem  Asks for one value. The result arrives in ANS and ANS_DECISION, which is one
rem  of keep, value, generate or clear. GENERATOR is gen-guid or gen-token when a
rem  generated value may stand in for an absent one, plain when not, and long
rem  when the value may also be read from a file, which is what makes a session
rem  cookie practical to enter.
rem
rem  The hint is echoed rather than handed to set /p as its prompt, because cmd
rem  drops the leading whitespace of a prompt: a prompt would print flush against
rem  the left margin, under text that is indented. The prompt that is left is a
rem  chevron, which is also what makes the line the operator types on obvious.
rem
rem  Nothing typed here is validated. A value is written as it was given, and the
rem  report afterwards says how long it is, because a value that is wrong is the
rem  operator's to correct and a value this script refused to write would be one
rem  nobody could explain.
rem ---------------------------------------------------------------------------
:field
set "ANS="
set "ANS_DECISION=keep"
echo.
echo    %~1
if "%~2"=="" (echo       current: empty) else (echo       current: set)
echo       %~3
set /p "ANS=> "
if not defined ANS goto :eof
if /i "%ANS%"=="g" if "%~4"=="gen-guid" goto :field_generate
if /i "%ANS%"=="g" if "%~4"=="gen-token" goto :field_generate
if /i "%ANS%"=="f" if "%~4"=="long" goto :field_from_file
if /i "%ANS%"=="clear" goto :field_clear
set "ANS_DECISION=value"
goto :eof

:field_generate
set "ANS="
set "ANS_DECISION=generate"
goto :eof

:field_clear
set "ANS="
set "ANS_DECISION=clear"
goto :eof

:field_from_file
set "FROMFILE="
set /p "FROMFILE=       path of the file that holds the value: "
if not defined FROMFILE goto :eof
call :readfile "%FROMFILE%" ANS
if not defined ANS goto :eof
set "ANS_DECISION=value"
goto :eof

rem ---------------------------------------------------------------------------
rem  The ways out that are not the end.
rem ---------------------------------------------------------------------------
:not_a_repository
echo.
echo   This script belongs beside the deploy directory, in the repository root.
echo   It is being run from "%ROOT%", where deploy\templates\scanner.env is not.
echo.
pause
exit /b 2

:no_template
echo   No environment template was found, so there is nothing to build the
echo   scanner's file from. Looked for:
echo.
echo     "%SCANENV%"
echo     "%SCANTMPL%"
echo     "%REPOTMPL%"
echo.
pause
exit /b 3

:no_writer
echo.
echo   The write path this script hands its values to is not beside it:
echo.
echo     "%WRITER%"
echo.
pause
exit /b 4

:write_failed
echo.
echo   Nothing further was attempted. The values were not written: see the
echo   message above for the file it stopped at.
echo.
pause
exit /b 4

:declined
echo.
echo   Nothing was written. The values that were entered are gone, because a
echo   value that was kept rather than written is one the file already has.
echo.
exit /b 0

:end_ok
echo.
pause
exit /b 0
