# Windows installer

Build from the repository root with:

```powershell
powershell.exe -NoProfile -File installer/Build-Msi.ps1 -Version 1.0.0
```

Requires .NET 10 SDK and NuGet access. The script uses workspace-local WiX 5.0.2, publishes the app and .NET runtime for Windows x64, includes runtime license notices, and produces `artifacts/installer/FlyGuy-1.0.0-win-x64.msi` plus a SHA-256 checksum.

The MSI installs for the current user under `%LOCALAPPDATA%\Fly Guy`, adds **Fly Guy** to the Start menu, and supports uninstall through Windows Settings. It requires Windows 10 version 2004 or newer, or Windows 11. Friends do not need the .NET runtime or administrator rights. No automatic startup is added.

Increment the three-part version for each new release. The upgrade code and generated component identities remain stable. `-SkipPublish` packages the existing `artifacts/publish/win-x64` output.

The MSI is unsigned; Windows may display an unknown-publisher warning or block it under restrictive application-control policies. This build does not configure or bypass those policies.
