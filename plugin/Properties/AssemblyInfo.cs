using System.Reflection;
using System.Runtime.InteropServices;

// Assembly identity. Company and copyright are what Explorer's Properties >
// Details tab and Revit's add-in manager show for the shipped DLL, so they name
// the maintainer of this fork (see LICENSE), not the project the fork started
// from.
[assembly: AssemblyTitle("mcp-servers-for-revit-plugin")]
[assembly: AssemblyDescription("MCP bridge add-in for Autodesk Revit (MrGezz fork of mcp-servers-for-revit)")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("MrGezz")]
[assembly: AssemblyProduct("mcp-servers-for-revit-plugin")]
[assembly: AssemblyCopyright("Copyright (c) 2026 MrGezz. MIT licensed.")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Not exposed to COM.
[assembly: ComVisible(false)]

// The typelib id for this assembly, should it ever be exposed to COM.
[assembly: Guid("43cd0fd7-df41-4f64-92be-a0f78666d86f")]

// Version: Major.Minor.Patch.0, bumped by scripts/release.ps1 together with
// server/package.json and commandset/RevitMCPCommandSet.csproj.
[assembly: AssemblyVersion("1.0.2.0")]
[assembly: AssemblyFileVersion("1.0.2.0")]

#if NET5_0_OR_GREATER || NETCOREAPP3_1_OR_GREATER
[assembly: System.Runtime.Versioning.SupportedOSPlatformAttribute("windows")]
#endif
