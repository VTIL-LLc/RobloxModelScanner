---
title: The service platform
order: 20
---

# The service platform

Three projects, one idea: a service is described once, in a platform-neutral plan, and everything else is a
renderer for that description.

| Project | Responsibility |
| --- | --- |
| `RMS.ServiceModel` | The plan, the run records and the log store. The vocabulary everything else shares. |
| `RMS.ServiceHost` | Supervision: start, restart, stop, record. One executable, three hosts. |
| `RMS.ServiceInstaller` | Translate the plan into SCM, systemd, launchd or IIS definitions and apply them. |

## The plan

The deployer writes `artifacts/services/services.ini`. It is the installer's whole input and the DevOps site's
whole description of what should be running.

```ini
[settings]
version  = 1
platform = windows
root     = C:\path\to\the\checkout
out      = C:\path\to\the\checkout\artifacts
log_dir  = C:\path\to\the\checkout\artifacts\logs
host     = HOSTNAME

[api]
name          = rms-api
description   = scanner read-only API
executable    = C:\path\to\the\checkout\artifacts\api\Scanner.MicroApi.exe
working_dir   = C:\path\to\the\checkout\artifacts\api
argument.0    = --urls
argument.1    = http://localhost:5000
environment.ASPNETCORE_URLS = http://localhost:5000
restart       = always
restart_delay = 2
stop_timeout  = 15
```

One section per service. A service that is a website declares `website` and `port`; an executable service declares
`executable`; both may be present.

`settings.host` and `settings.log_dir` must be rooted absolute paths — `RMS.ServiceModel` validates that and refuses
a plan that is not, because a log directory that depends on the working directory is a log directory nobody can
find.

## Supervision

`RMS.ServiceHost` is a Windows service on Windows and a plain supervised process elsewhere. It starts the plan's
children, watches them, restarts them according to `restart`, and records every run.

**Restart policy.** `always`, `on-failure` or `never`. `restart_delay` is the first delay and
`max_restart_delay` the ceiling as repeated failures back off.

**Stopping.** A stop sends `shutdown_line` on the child's standard input, if the plan declares one, and waits
`stop_timeout` before killing it. Standard input is only redirected when a `shutdown_line` is declared, so a
service that reads its input for its own reasons is not handed a pipe it did not ask for.

**Run logging.** One file per run:

```text
<log_dir>/<service>/<runId>.log
<log_dir>/<service>/<runId>.run
<log_dir>/<service>/index.ini
```

`runId` is `<yyyyMMdd-HHmmssfff>-<sequence>`. The `.run` file records the outcome: status, exit code, start and
end. `index.ini` lists the runs newest first. Retention keeps the newest `RetainedRuns` per service, fifty by
default.

The status is `Running`, `Succeeded`, `Failed`, `Stopped` or `Abandoned`. `Abandoned` is the interesting one: it
means the supervisor went away before the child did. It is the only status that is evidence about the supervisor
rather than about the service, and it is why a crash of the host does not silently look like a clean shutdown.

## Websites

A service section that declares a website is one of two things:

- **With an executable** it is supervised as a process, exactly like anything else. A site that is its own
  process needs no special case.
- **Without an executable** it is handed to IIS. The installer creates or updates the site with `appcmd.exe`,
  disables the idle timeout and periodic recycle, and sets `startMode` to `AlwaysRunning` — because a site that
  stops when nobody has looked at it for twenty minutes is a site whose first request after lunch is a cold start.

On POSIX a site is always a separate `xsp4` process, supervised like every other service. That is what gives a
website the same run logs and the same restart behaviour as a daemon, instead of a second lifecycle with its own
failure modes.

## The installer

```text
RMS.ServiceInstaller.exe <verb> --directory <out>/services
```

Verbs: `install`, `uninstall`, `start`, `stop`, `restart`, `status`. It reads the plan from the directory it is
given, renders the platform's definitions, and applies them.

The installer does not decide anything about the deployment. It does not know which services should exist; the
plan says. It does not decide what a service is called; the plan says. Its only judgement is how a name, an
executable, an argument list and an environment become a definition on this platform.

That is also why the deployer injects the resolved `DOTNET_ROOT` into every service that has an executable: a
published .NET program finds its runtime through `DOTNET_ROOT`, and a service started by the SCM does not inherit
the shell environment that a manual run does. A manifest-declared `DOTNET_ROOT` wins, so an operator who needs a
specific runtime can say so.

## The DevOps view

`RMS.Devops.Service` reads the plan and the run store and reports both together. A service in the plan with runs
is a service that is being supervised; a service with runs but no longer in the plan is a leftover, and it says so
rather than silently disappearing. The service is stateless — no database, no cache beyond a bounded record cache
— and reloads the plan when the file's identity changes, so editing a manifest and regenerating the plan is
enough to change what the console reports.
