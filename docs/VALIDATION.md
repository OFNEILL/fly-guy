# Validation — 28 September 2026

Environment: Windows x64, .NET SDK 10.0.401.

## Two-eye vision update — 29 September 2026

- Release solution build passes. Pixel-based geometry, static/moving stimuli, directional motion, gap validity, exact debugger-to-input correspondence, nonvisual separation, three-seed motor responses and 60/120/240 Hz brain stability checks pass alongside spray and baseline checks.
- `tests/Smoke-VisionDesktop.ps1` verifies live capture, all five viewer modes, geometry/rate editing, pause/resume, viewer reopen and clean shutdown. With render target 8 Hz, brain activity stayed approximately 120 ticks/s.
- See [vision design and measured experiments](VISION.md). Mixed-DPI and visual alignment checks remain manual.

## Fly Spray update — 29 September 2026

- `dotnet build FlyGuy/FlyGuy.slnx -c Release`: passed with zero warnings/errors.
- `dotnet run --project tests/FlyGuy.Checks -c Release`: baseline simulation checks and new spray experiments passed. Includes paired/delayed conditioning across five seeds, cue-only recall with transient state removed, reward/aversion competition, receptor-projection ablation, independent recovery, sensitization, reset/persistence and repeated-exposure stability/replay.
- `dotnet run --project FlyGuy/FlyGuy.Checks -c Release`: existing food, reward, habituation and five-seed stability checks passed after updating sensory input dimensions.
- `powershell.exe -NoProfile -File tests/Smoke-SprayDesktop.ps1`: desktop startup, spray selection, rate controls, timed cloud creation, pause/reset/resume and clean shutdown passed.
- Model details, primary research and measured conditioning outputs: [Fly Spray](SPRAY.md). Visual alignment, hotkeys and mixed-DPI behavior remain manual checks. The earlier validation record below describes the original milestone.

## Original milestone

- `dotnet build src/FlyGuy.Desktop -c Release`: passed, zero warnings/errors.
- `dotnet run --project tests/FlyGuy.Checks -c Release`: passed 72,000 fixed ticks (ten simulated minutes), with a second identical simulation for replay comparison. Checks finite bounded neural activity, finite velocity, monitor containment, nonzero travel and turning, cursor laterality, opponent edge response, zero-motor ablation, a shared monitor seam and a separated monitor gap. Recorded travel: 23,988 physical pixels; peak absolute turn: 0.88. Headless check elapsed time: 0.33 seconds on this machine; this is not a desktop CPU benchmark.
- Desktop process smoke test: alive and responding after five seconds; closing the main window terminated the application with exit code 0.

Not yet visually verified: transparency/click-through on the user's desktop, mixed-DPI transitions, monitor hot-plug, suspend/resume and extended real-time desktop CPU use. These require an interactive desktop check; a process smoke test cannot establish rendering correctness.

Suggested manual check: launch the application, minimize the inspector, move the cursor around the creature, then restore the inspector and observe sensory and motor activity. Check crossing adjacent monitors, pause/resume, monitor removal, and closing the inspector. The debug graph uses smoothed population activity rather than individual spikes.
