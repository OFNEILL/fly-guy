# Research and implementation decision — 28 September 2026

## Available sources

| Source | Useful coverage | Access / limitation |
|---|---|---|
| [FlyWire FAFB v783](https://zenodo.org/records/10676866) | Whole adult female brain; visual, central-complex, interneuron and descending pathways; connectivity and predicted transmitters | Versioned downloadable tables; not a complete nerve cord. [Annotation paper](https://www.nature.com/articles/s41586-024-07686-5) provides cell identities. |
| [BANC](https://www.nature.com/articles/s41586-026-10735-w) | Brain and nerve cord in the same female; local sensory/motor loops and descending/ascending coordination | Recommended next dataset. [Authors' repository](https://github.com/htem/BANC-project/) and [archived data](https://doi.org/10.7910/DVN/7WTH1N); pin v888 and the exact annotation release. |
| [MANC](https://www.janelia.org/node/68782) | Adult male nerve cord, roughly 23,000 neurons; sensory, interneuron, descending and motor annotations | Excellent motor reference; cannot join female brain IDs directly to male cord IDs. |
| [MaleCNS v1.0](https://male-cns.janelia.org/download/) | Male brain plus cord; a further whole-CNS option | Released June 2026; downloadable weights/annotations, CC-BY, neuPrint dataset. Compare with BANC after the prototype. |
| [Hemibrain](https://www.janelia.org/node/65250) | Detailed central-brain navigation circuitry | Partial brain; useful for circuit comparison, not complete visual-to-muscle control. |

[neuPrint Python](https://connectome-neuprint.github.io/neuprint-python/docs/quickstart.html) supports programmatic queries with a token. [Codex](https://codex.flywire.ai/faq) exposes searchable annotations and connectivity; computational features currently require sign-in. Prefer archived tables for reproducibility, not a live runtime API dependency.

## Recommended model

First establish the closed sensor–neural–motor loop using a small synthetic rate network. This is a computational experiment, **not measured fly connectivity or a validated model of fly behaviour**. Bilateral sensory populations, recurrent excitation, opponent inhibition, activity-dependent adaptation and continuous descending-like motor readouts are useful motifs. Neural noise is seeded and enters neural current only; no random movement commands or behavioural state machine. Fatigue, curiosity, arousal, startle, attraction and habituation modulate current gradually. Animation follows physical speed.

Next extract a BANC subnetwork: select annotated visual/descending walking and turning populations, include intervening and local premotor populations, retain laterality, and trace sensory input to motor output. Use FlyWire central-complex and visual annotations as cross-checks. Preserve original IDs as strings, release, source URL, confidence, transmitter predictions and original synapse counts in the export. Aggregate by type AND side only after checking pathway preservation. Normalize incoming counts to avoid high-degree saturation. Transmitter alone does not establish receptor-dependent sign; mark uncertain signs and run sensitivity studies. Do not assume anatomical counts establish physiological weights, time constants, drives or desktop sensory mapping.

Use sparse leaky rate dynamics initially: tau dr/dt = -r + sigmoid(bias + sensory current + weighted recurrent rates - adaptation). Integrate synchronously at 120 Hz; adaptation is a slower low-pass trace. Continuous motor populations produce thrust, braking and signed turning. Desktop coordinates never enter the brain. Contact projection is a physical constraint, not an avoidance behaviour.

## Architecture and budget

- `src/FlyGuy.Core`: sensory encoding, recurrent brain/drives, motor physics, creature state and monitor geometry; no Windows dependencies.
- `src/FlyGuy.Desktop`: WPF transparent, topmost, nonactivating click-through creature window; Win32 cursor/monitor positions in physical pixels; separate debug window.
- `tests/FlyGuy.Checks`: dependency-free executable checks for determinism, sensory response, motor ablation and multi-minute stability.

WPF ships with .NET desktop; no third-party runtime packages. A small moving window avoids a full-desktop transparent render surface. Use a per-monitor-aware manifest and native physical window placement. Render at desktop frame cadence; fixed-step simulation with bounded catch-up; display actual observed rates. Monitor topology is refreshed periodically. Connected monitors can be crossed; disconnected islands cannot be reached by continuous movement.

Prototype: tens of populations and edges, negligible simulation memory, roughly thousands of edge evaluations/second. WPF is expected to dominate memory and CPU; measure on the target machine. A later 500-node/20,000-edge model at 120 Hz needs 2.4 million edge evaluations/second, plausibly ordinary CPU territory (estimate, not benchmark). No GPU needed. Whole-brain simulation is deliberately out of scope.

## Stages

1. Synthetic network, animated overlay, mouse/edge/proprioception inputs and inspectable live graph. Deterministic headless checks and desktop smoke launch.
2. Versioned offline BANC extraction with provenance and a data-driven graph loader; compare against synthetic baseline and ablations before calling it connectome-derived.
3. Calibrate sensory transforms and dynamics; validate edge response, habituation, recovery and sensitivity across seeds. Record traces.
4. Add windows, clicks and visual input only as neural stimulation; consider richer body mechanics and multiple organisms.

Milestone one intentionally ships no real-connectome weights. The next stage must replace the graph, not merely rename synthetic nodes.
