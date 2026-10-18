using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("RMS.Site.Common")]
[assembly: AssemblyDescription("The parts every RMS site shares: accounts, anti-forgery, HTML rendering and the service client.")]
[assembly: AssemblyCompany("RMS")]
[assembly: AssemblyProduct("RMS")]
[assembly: AssemblyCopyright("Copyright (c) RMS")]
[assembly: ComVisible(false)]
[assembly: CLSCompliant(false)]
[assembly: Guid("7c3a6e1b-6e4f-4e1c-9e4b-2e2b0c4a7f02")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]

[assembly: InternalsVisibleTo("RMS.Devops.Site")]
[assembly: InternalsVisibleTo("RMS.Configuration.Site")]
[assembly: InternalsVisibleTo("RMS.Docs.Site")]
[assembly: InternalsVisibleTo("Scanner.WebForms")]
