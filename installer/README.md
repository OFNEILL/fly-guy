# Windows installer

## Velopack (recommended)

Build a self-contained Windows x64 installer and update feed:

```powershell
powershell.exe -NoProfile -File installer/Build-Velopack.ps1 -Version 1.1.0
```

Output is under `artifacts/velopack/1.1.0/`. Distribute `FlyGuyDesktop-win-Setup.exe`. The app checks public, stable GitHub Releases at `https://github.com/OFNEILL/fly-guy` on launch and every six hours, downloads updates in the background, and applies downloaded updates on the next launch. Offline checks and failed downloads leave the app running. Debug builds and `--dev` skip update checks. Unpackaged builds do not check the feed.

Every push to `main`, including a merged PR, runs **Windows production release**. The first production release is `v1.0.0`; subsequent commits increment the highest production tag's patch version (`v1.0.1`, `v1.0.2`, ...). Beta tags are ignored. Checks must pass before packaging. All output files, including `releases.win.json` and the full `.nupkg`, are uploaded to a draft GitHub Release before it is published as stable/latest. Failed uploads leave the draft unpublished; rerun the workflow to retry. An already published commit is skipped. A delayed commit older than the latest release is skipped. Release runs are serialized to avoid version collisions. You can also dispatch the workflow manually on `main`.

The workflow uses GitHub's built-in token with `contents: write`; no personal access token is required. Repository policy must permit that permission. The repository/releases must be publicly accessible for installed apps; no GitHub credentials are embedded in the app. Draft and prerelease releases are excluded from update checks. Local builds do not publish anything. To publish manually, attach **all files from the version's output folder** to a stable GitHub Release using the corresponding `vX.Y.Z` tag.

Release installs show compact creature controls. The neural graph, brain reset, rate settings, Fly Vision panel and performance status are available only in developer mode: a Debug build or launch with `--dev`.

Existing MSI users must close Fly Guy, uninstall the old MSI via Windows Settings, and install the new Setup once. Settings and language memory live separately in `%LOCALAPPDATA%\FlyGuy`; the new application installs under `%LOCALAPPDATA%\FlyGuyDesktop`. Subsequent Velopack updates require no manual reinstall. Do not ship the legacy MSI for new users.

Packages are currently unsigned. Before wider distribution, configure release signing. Validate a real installed upgrade from version A to version B on a test Windows account before publishing broadly; a build alone does not verify that flow.

## Legacy MSI

Build from the repository root with:

```powershell
powershell.exe -NoProfile -File installer/Build-Msi.ps1 -Version 1.0.0
```

Requires .NET 10 SDK and NuGet access. The script uses workspace-local WiX 5.0.2, publishes the app and .NET runtime for Windows x64, includes runtime license notices, and produces `artifacts/installer/FlyGuy-1.0.0-win-x64.msi` plus a SHA-256 checksum.

The MSI installs for the current user under `%LOCALAPPDATA%\Fly Guy`, adds **Fly Guy** to the Start menu, and supports uninstall through Windows Settings. It requires Windows 10 version 2004 or newer, or Windows 11. Friends do not need the .NET runtime or administrator rights. No automatic startup is added.

Increment the three-part version for each new release. The upgrade code and generated component identities remain stable. `-SkipPublish` packages the existing `artifacts/publish/win-x64` output.

The MSI is unsigned; Windows may display an unknown-publisher warning or block it under restrictive application-control policies. This build does not configure or bypass those policies.
