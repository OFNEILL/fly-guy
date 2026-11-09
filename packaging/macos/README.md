# macOS build

Requires macOS 14 (Sonoma) or later. Separate self-contained packages support Apple Silicon (`osx-arm64`) and Intel (`osx-x64`). The Mac frontend uses Avalonia and shares the Windows simulation, learning and binocular vision code. Windows continues to use its WPF frontend.

## Build and install

On a Mac, install the .NET 10 SDK and Xcode Command Line Tools (`xcode-select --install`), then run from the repository root:

```sh
bash packaging/macos/build.sh osx-arm64
# Or, for Intel:
bash packaging/macos/build.sh osx-x64
```

The script produces `artifacts/macos/FlyGuy-1.0.0-osx-arm64.zip` (or `osx-x64`) and a SHA-256 checksum. Set `FLYGUY_VERSION=1.2.3` to change the version. Each build keeps its `.app` in a fresh staging directory under `artifacts/macos`. Extract the zip and drag **Fly Guy.app** to Applications. No .NET runtime installation is required.

The **macOS build** GitHub Actions workflow builds both architectures and uploads downloadable artifacts. It does not publish a release. The Swift bridge and final bundle must be built on macOS; building the managed project on Windows only verifies the C# frontend.

Packages are ad-hoc signed, not Developer ID signed or notarized. Downloaded copies may require approval through macOS Privacy & Security. Public distribution should use Developer ID signing and Apple notarization; no signing credentials are stored in this repository.

## Use

- Drag the fly to reposition it. Pause/resume, reset brain and exit are in the inspector. Closing the inspector closes the app and overlay.
- Click **Sugar in 3s** or **Spray in 3s**, then move the cursor to the target. **Cancel** or Escape while the inspector has focus cancels the countdown. Tools are disabled while paused.
- Enable desktop vision in the inspector. Grant Screen Recording access in System Settings if prompted, then restart if macOS requests it. Without permission the simulation continues with unavailable visual input.
- ScreenCaptureKit captures a 600-point square around the fly, including the cursor and excluding this application's windows. Pixels stay in memory. Retina images are resampled to desktop points so vision, cursor position and physics use the same coordinates.

This initial Mac frontend includes the creature overlay, drag interaction, sugar/spray simulation, live population values, learning and threat metrics, and processed left/right retinas. It does not yet include Windows' connection graph/history plots, the other four vision views, adjustable eye geometry/rates, global keyboard shortcuts, or sugar/spray desktop overlays. Its transparent 96-point window can intercept clicks around the fly. Multi-monitor placement, capture orientation and Retina scaling still require manual validation on Mac hardware.

## Validation

```sh
dotnet run --project tests/FlyGuy.Checks -c Release
bash -n packaging/macos/build.sh
```

Packaging compiles Swift, validates the plist and bundle signature, and runs `FlyGuy.Mac --smoke-test` when building for the host architecture. That check loads the native bridge, enumerates displays and advances the shared simulation without requesting capture permission.

Before distributing, test launch from Finder, permission denial/grant/restart, asymmetric desktop imagery in both retinas, cursor visibility, self-window exclusion, Retina and mixed-scale monitors (including negative origins), dragging, sugar/spray application, pause/resume, display disconnect, sleep/wake and clean exit. CI does not replace these interactive checks.

References: [Avalonia desktop package](https://www.nuget.org/packages/Avalonia.Desktop/11.3.22), [Apple screenshot API](https://developer.apple.com/documentation/screencapturekit/scscreenshotmanager), [application exclusion filter](https://developer.apple.com/documentation/screencapturekit/sccontentfilter/init(display:excludingapplications:exceptingwindows:)).
