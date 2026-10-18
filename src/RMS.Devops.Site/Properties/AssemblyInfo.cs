using System.Reflection;
using System.Runtime.InteropServices;

// The console is deployed as part of this repository and never published to a package feed, so the identity
// below is for the file system and the log rather than for a consumer. The version is deliberately inert: a
// version number that changed on every build would make the artefact listing on the console's own Artefacts
// page useless for telling one build from another.
[assembly: AssemblyTitle("RMS.Devops.Site")]
[assembly: AssemblyDescription("The RMS DevOps console: services, run history, logs, artefacts and the state of the catalogue index.")]
[assembly: AssemblyProduct("RMS")]
[assembly: AssemblyCompany("RMS")]
[assembly: AssemblyCopyright("Copyright (c) RMS")]
[assembly: AssemblyCulture("")]

// Not visible to COM. Nothing here is designed to be reached through it, and the attribute defaults to true,
// which would create a registration surface that nothing asked for.
[assembly: ComVisible(false)]

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]
