using System.Reflection;
using System.Runtime.InteropServices;

// Embeds a proper Win32 version resource (Explorer → Properties → Details)
// so the executable identifies itself instead of showing blank metadata —
// blank version info is a red flag to SmartScreen and to users.
[assembly: AssemblyTitle("mouseclicker.app")]
[assembly: AssemblyDescription("Robust Windows auto clicker with AI-generated click patterns")]
[assembly: AssemblyConfiguration("Release")]
[assembly: AssemblyCompany("stevologic")]
[assembly: AssemblyProduct("mouseclicker.app")]
[assembly: AssemblyCopyright("Copyright (c) 2021-2026 stevologic  -  MIT License")]
[assembly: AssemblyTrademark("")]
[assembly: ComVisible(false)]
[assembly: AssemblyVersion("3.7.0.0")]
[assembly: AssemblyFileVersion("3.7.0.0")]
[assembly: AssemblyInformationalVersion("3.7.0 - https://mouseclicker.app")]
