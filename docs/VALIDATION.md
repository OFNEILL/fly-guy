# Validation — 28 September 2026

Environment: Windows x64, .NET SDK 10.0.401.

- `dotnet build src/FlyGuy.Desktop -c Release`: passed, zero warnings/errors.
- `dotnet run --project tests/FlyGuy.Checks -c Release`: passed 72,000 fixed ticks (ten simulated minutes), with a second identical simulation for replay comparison. Checks finite bounded neural activity, finite velocity, monitor containment, nonzero travel and turning, cursor laterality, opponent edge response, zero-motor ablation, a shared monitor seam and a separated monitor gap. Recorded travel: 23,988 physical pixels; peak absolute turn: 0.88. Headless check elapsed time: 0.33 seconds on this machine; this is not a desktop CPU benchmark.
- Desktop process smoke test: alive and responding after five seconds; closing the main window terminated the application with exit code 0.

Not yet visually verified: transparency/click-through on the user's desktop, mixed-DPI transitions, monitor hot-plug, suspend/resume and extended real-time desktop CPU use. These require an interactive desktop check; a process smoke test cannot establish rendering correctness.

Suggested manual check: launch the application, minimize the inspector, move the cursor around the creature, then restore the inspector and observe sensory and motor activity. Check crossing adjacent monitors, pause/resume, monitor removal, and closing the inspector. The debug graph uses smoothed population activity rather than individual spikes.
