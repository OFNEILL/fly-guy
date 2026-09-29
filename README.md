# Fly Guy

A Windows desktop creature driven by a small recurrent neural simulation. **Milestone one uses synthetic weights, not real fly-connectome connectivity.** The research and the staged plan for replacing them are in [docs/RESEARCH.md](docs/RESEARCH.md).

## Run

Open **Fly Vision** for two independently sampled desktop eyes, five live visualisation modes and configurable geometry/update rates. The brain receives processed pixel signals; it no longer receives cursor coordinates. See [vision design, research and checks](docs/VISION.md).

Fly Spray adds local aversive receptors, separate reward/punishment learning, acute arousal and slowly recovering stress. See [mechanisms, sources and experiments](docs/SPRAY.md).

Install the .NET 10 SDK on Windows, then from this directory:

```powershell
dotnet run --project FlyGuy/FlyGuy.Desktop
```

The creature appears in a transparent, topmost window. Move the cursor within its visual fields to stimulate its eyes, or grab it with the left mouse button. Select Sugar or Fly Spray in the inspector and apply it at the cursor after a three-second countdown. Ctrl+Shift+S drops sugar, Ctrl+Shift+F sprays, and Ctrl+Shift+Space applies the selected tool. Escape cancels a countdown. Minimize the inspector to observe the desktop; restore it from the taskbar. Pause/resume and Exit are in the inspector. Closing the inspector exits the entire application. Nearby desktop pixels are processed in memory; no screenshots are saved or uploaded. No global hooks, OCR, network access or startup registration.

```powershell
dotnet run --project tests/FlyGuy.Checks -c Release
dotnet publish FlyGuy/FlyGuy.Desktop -c Release -r win-x64 --self-contained false -o artifacts/publish
```

Published output requires the .NET 10 Windows Desktop Runtime.

## How it works

Two 24×10 retinas sample different overlapping fields of desktop pixels. Luminance, contrast, temporal change and delayed-neighbor motion feed separate visual rate arrays and 55 brain populations. Explicit nonvisual pathways provide odor, taste, irritant/contact, boundary touch and proprioception. The brain runs at 120 Hz by default, independently of capture and rendering, with recurrent excitation/inhibition, adaptation and seeded neural noise. Drives, metabolism, arousal and stress modulate neural processing. Reward and aversive signals modify opposing cue readouts through eligibility-based plasticity. Four locomotor populations provide thrust, opposing turn signals and braking; a feeding population gates ingestion. Only locomotor outputs feed the body physics; no chase/wander/startle state machine selects movement. Animation follows actual travel. Boundary projection prevents leaving the monitor union but does not choose a heading.

The inspector displays every population, signed connections, drives, reward/aversion, acute arousal, slow stress, sensitization, both plastic weight sets and 120-second histories. Recovery rates can be adjusted independently. Cyan connections excite; coral connections inhibit. Synthetic populations are functional abstractions, not identified Drosophila cell types. Automated conditioning experiments demonstrate limited associative plasticity, not biological realism or general intelligence.

## Limits

The desktop is a flat space; windows are not obstacles. Adjacent monitors share traversable seams; separated monitor islands require repositioning or a future flight mechanic. The creature's center is constrained, so its legs can clip at a screen edge. Mixed-DPI placement uses physical coordinates and a per-monitor-aware window, but needs manual testing on your particular display arrangement. Simulation catch-up is capped after stalls/sleep. The inspector remains available on the taskbar; there is no tray icon. No real data is bundled or downloaded at runtime.
