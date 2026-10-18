---
title: The deployer
order: 10
---

# The deployer

`deploy/rmsdeploy.c` is one C translation unit with no third-party dependency. It takes a machine that has
nothing and makes the stack run on it, and it does that on Windows, Linux and macOS.

## Why C

The machine it has to run on is by definition the machine that has nothing installed yet. A .NET or Go
bootstrapper would arrive as a binary per platform and would need a runtime that is not there. C is already
there, and every target this project supports has a compiler or a package for one.

`deploy/build.cmd` and `deploy/build.sh` build it. The Windows script looks for a compiler on `PATH`, then for
`cl.exe` through `vswhere`, then for `clang.exe` — the same toolchain the requirements manifest installs when the
machine has none.

## The three manifests

The manifests are the operational source of truth. Adding a project to the build is a manifest edit, not a C
edit.

| File | What it describes |
| --- | --- |
| `rmsdeploy.requirements` | Prerequisites: what to look for, and what to install when it is missing. |
| `rmsdeploy.build` | The projects, in dependency order, and how each is built. |
| `rmsdeploy.services` | The services to register, and the environment to register them with. |

They are INI-shaped. A section is one item; keys are `name = value`. Comments are `#`.

### Per-platform values

Every key may be written twice: once unprefixed for all platforms, and once with a `windows.`, `linux.` or
`macos.` prefix to override it. That is how a service names a different executable per platform without a second
section:

```ini
executable            = %ROOT%/artifacts/api/Scanner.MicroApi
windows.executable    = %ROOT%/artifacts/api/Scanner.MicroApi.exe
```

### Placeholders

`%ROOT%` expands to the repository root. Any other `%NAME%` expands from the environment of the machine the
deployer is running on. That is what lets one services manifest describe a deployment on a machine whose paths
nobody knew when it was written.

## Verbs

```text
rmsdeploy probe                 report the platform and which requirements are satisfied
rmsdeploy bootstrap             install only what is missing
rmsdeploy build [step-id]       build every step, or one of them
rmsdeploy publish              build, then collect publish output under --out
rmsdeploy services <verb>       install | uninstall | start | stop | restart | status
rmsdeploy deploy                stop, bootstrap, build, publish, install, start
```

`deploy` runs the whole sequence, in the order a machine survives it. The previous deployment is stopped
first, because a running service holds its own assemblies open and the build that follows could not replace
them. Then the requirements are bootstrapped, the tree is built and published, the plan is applied, and the
services are started.

A stop that fails is reported and the run continues, because a machine with nothing installed reports the
same failure and an assembly that is genuinely locked will fail the build with a message of its own. A start
that fails ends the run: a deployment whose files are all correct and whose services answer nothing is not a
deployment.

`--dry-run` on `deploy` prints that whole sequence and runs none of it, site keys included. That phase
reports the path it would write and writes nothing, which is the only way an operator finds out it exists
before a real deploy writes into published directories.

Global options: `--root`, `--tools`, `--out`, `--dry-run`, `--verbose`, `--keep-cache`,
`--continue-on-error`. The command comes first; options are parsed from the second argument onward.

`--dry-run` prints every command it would run and reports a plan — `N step(s) would run` — rather than a result.
That distinction is deliberate: a dry run that printed "17 succeeded" would be telling an operator that a build
had worked when nothing was compiled at all, and the operator most likely to run one is checking a manifest
change before letting it touch a machine.

## Requirements

Each section names a `kind`:

| Kind | Meaning |
| --- | --- |
| `dotnet` | The .NET SDK. |
| `node` | Node and npm, for the asset build. |
| `msbuild` | The framework MSBuild: Visual Studio on Windows, Mono elsewhere. |
| `archive` | A toolchain archive to download and unpack; `strip` says how many leading path components the archive wraps its payload in. |
| `package` | A platform package, installed with its own package manager. |
| `installer` | A platform installer, with the extra components to include. |

Presence is decided by `verify_tool` first — the deployer's own search for the tool — then `verify_path`, then a
`verify` command. Using the deployer's own search is preferred: it means the requirement check and the build ask
the same question, so they cannot disagree about whether a tool is there.

Only `https://` URLs are accepted. `platforms` on a requirement lets `probe` and `bootstrap` skip it where it does
not apply, which is how the Windows-only entries stay out of the way on Linux.

### The MSBuild the sites need

The four Web Forms sites import `$(VSToolsPath)\WebApplications\Microsoft.WebApplication.targets`, which exists
only in a Visual Studio installation that carries the web build workload. Finding *an* MSBuild is therefore not
enough: the Build Tools installation without that workload compiles every project in the stack except the sites
and fails on those with `MSB4226`.

So the deployer enumerates the installations it finds, prefers one that carries the targets, passes the discovered
directory as `/p:VSToolsPath=`, and reports it in `probe`:

```text
framework msbuild  C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe
web build targets  C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Microsoft\VisualStudio\v18.0
```

The version directory is discovered by listing rather than by naming a year. A machine whose only Visual Studio is
a different release still deploys; a list of known years would go stale the same way the previous hard-coded
`2022` did.

## Build steps

| Kind | What it runs |
| --- | --- |
| `dotnet-project` | `dotnet restore`, then `dotnet build -c <configuration>`. |
| `dotnet-solution` | The same, for a solution. |
| `framework` | The framework MSBuild, with `/restore` and the discovered `VSToolsPath`. |
| `node-package` | `npm ci` and `npm run build` in a package directory. |
| `shell` | The command verbatim, for the steps that do not fit. |

A step that does not name a configuration is built **Release**, on every kind. The net48 projects use
`PackageReference`, which is why the framework kind restores: a package reference that skipped restore would
compile against whatever happened to be in the cache on the machine doing the build.

Steps are built in the order the manifest lists them. Project order lives there rather than in the solution,
because the solution's entries carry no ordering and a reader looking for "what has to be built before what"
would not find it there.

## Publishing

`rmsdeploy publish` walks a fixed list of targets held in the C file. It is held there rather than in a manifest
because the published layout and the service definitions have to agree, and the service definitions are generated
from the same knowledge: a published directory that moved without its service entry moving would be a deployment
that half-works.

Each target names the tool that publishes it.

| kind | tool | what it is for |
| --- | --- | --- |
| `dotnet` | `dotnet publish -c Release -o <out>/<name>` | everything targeting .NET |
| `framework` | `msbuild /t:_WPPCopyWebApplication` | the four .NET Framework web consoles |

The second kind exists because a .NET Framework web project is not something the SDK builds at all: sending one
through the `dotnet` path fails immediately, and a site that was only ever *built* is a site registered against a
directory nothing created. Building a web project leaves assemblies in the project's own `bin`; what the plan
registers — an IIS site, or an xsp service where IIS is absent — needs one directory holding those assemblies, the
pages, the configuration and the static assets. That is what the web publishing pipeline assembles, and it is the
only thing in either toolchain that knows a `.cs` is source, an `obj` is scratch and an `.ashx` is executable.

The published directory is **emptied** before it is filled. The pipeline copies and never deletes, so without that
a page renamed since the last publish is still answering under its old name — and a retired page is precisely the
one nobody is looking at any more. The deployer's own removal never follows a symbolic link.

Two consequences worth knowing:

- Every `publish` therefore resets the `SiteServiceKeyFile` value in each site's `Web.config`, because that file is
  produced by the publish and the value is written by `install`. Run `publish` alone and the sites come back with an
  empty key path; run `deploy`, which publishes and then installs, and the order is correct.
- `App_Data` is not published and does not need to be. Each site creates it on start-up, and the account store
  creates it before it opens the database, so a fresh web root has no empty directory to copy.

Adding a *build* step needs no C change. Adding a *publish target* does — one line in `k_publish_targets`.

## Platforms

**Windows.** MSVC's MSBuild for the sites, the SDK for everything else, and the SCM for services.

**Linux.** Mono's MSBuild for the sites, `xsp4` to serve them, systemd to supervise.

**macOS.** Mono from Homebrew, `xsp4`, and launchd plists under `/Library/LaunchDaemons` with a `com.rms` label
prefix.

The two projects that genuinely cannot run elsewhere — the scanner and its tray indicator — are declared
`platforms = windows`, so a Linux or macOS deployment builds everything else and skips them rather than failing on
a framework it cannot target.

## Credentials

A deployment runs on five secrets, and the deployer never invents one. It creates the directory they
belong in and a document beside them saying which are still missing and how to generate each:

```text
artifacts/credentials/README.txt          regenerated on every run; read by nothing
artifacts/credentials/api-key             the API and the scanner's own site
artifacts/credentials/indexer.token       the indexer
artifacts/credentials/devops.token        the DevOps service and its site
artifacts/credentials/configuration.token the Configuration service and its site
artifacts/credentials/docs.token          the documentation service and its site
```

A credential file holds the value and nothing else. No comment, no quotes, no label. Every reader takes
the whole file with the surrounding whitespace removed, so anything else in it becomes part of the secret
and the comparison then fails for a reason that appears in no log. Generate the value rather than
inventing one, and give it an owner:

```text
openssl rand -base64 48                                 Linux, macOS, Git Bash on Windows
[guid]::NewGuid()                                       PowerShell, for the API key
chmod 600 <file>                                        Linux, macOS
icacls <file> /inheritance:r /grant:r "%USERNAME%:R"     Windows
```

Two of the files are shared, which is why they live in one directory rather than beside the program that
reads them. The API's key is also the scanner's site's key, presented through the `apiKey` query
parameter, which is the contract the API has always had. Each service's token is also its site's key.
Both ends read one file, so the two halves cannot drift apart.

Which files exist is not a list anybody maintains. An environment variable that ends in `_TOKEN_FILE` or
`_KEY_FILE` names a credential file, and the deployer derives the document from the same statement the
plan is built from. Adding a secret to a service is therefore one line in `rmsdeploy.services`.

### How each host is told where the file is

A service is told in its own environment, and the manifest says so once:

```text
environment = SCANNER_API_KEY_FILE=%ROOT%/artifacts/credentials/api-key
```

A web site under IIS is a process the deployer does not start, so it cannot be handed an environment.
There the deployer writes the path into the site's published `Web.config`, into a setting the site
already declares:

```xml
<add key="SiteServiceKeyFile" value="C:\...\artifacts\credentials\docs.token" />
```

Only the path is written, never the value. A path inside the web root is not a disclosure, and the file it
names lives outside every directory IIS serves. Under systemd and launchd the same site is handed the path
in its unit's environment instead, because there the process is the deployer's to start.

A site whose key file is absent still starts and refuses its own service: the page reports that nothing is
listening rather than showing data it could not fetch. That is the safe direction for the failure to
travel in, and it is what a fresh deployment looks like before the files are filled in.

The page also names the file. A site that was told to read a key file and cannot read it says so, and
says which path it was given — the one fact that turns the outage into a fix. A site whose key file is
unreadable never falls back to a key it found elsewhere, so the failure is never hidden behind a
credential the deployment did not name; naming the file is what keeps that strictness diagnosable rather
than merely silent.

## What the deployer does not do

It does not render a service definition. It expands a manifest into one platform-neutral plan file,
`artifacts/services/services.ini`, and hands that to `RMS.ServiceInstaller`, which is the only thing that knows
what a service looks like on each platform. That split is why the deployer has no `if windows` in its service
code, and why a fourth platform would be one renderer rather than a rewrite.
