# Research and architecture — 28 September 2026

## Decision
Use WPF layered windows, a platform-independent .NET core, fixed 120 Hz population-rate dynamics and continuous force/turn/brake outputs. Retain the existing synthetic locomotor network; add bilateral local food receptors, mechanosensation, a reward population and mushroom-body-inspired plastic readouts. No measured connectivity is claimed in this milestone. Walking is the first motor modality; flight biomechanics are deferred.

## Biological grounding and datasets
- [FlyWire whole-brain annotation (2024)](https://www.nature.com/articles/s41586-024-07686-5): approximately 140,000 neurons with cell-type annotations. Useful for selecting sensory-to-descending pathways; a whole-brain simulation is unnecessary for this desktop prototype.
- [Hemibrain](https://pmc.ncbi.nlm.nih.gov/articles/PMC7546738/) and [neuPrint](https://neuprint.janelia.org/): annotated central-brain connectivity, particularly useful for KC/MBON/DAN mushroom-body compartments and central-complex motifs.
- [MaleCNS v1.0 downloads](https://male-cns.janelia.org/download/) provide brain and nerve-cord connectivity, annotations and confidence-filtered edge tables. Prefer this for later connected descending/premotor circuits; [MANC](https://www.janelia.org/project-team/flyem/manc-connectome) remains useful for motor-circuit comparisons. Do not silently join neuron IDs across animals, sexes or releases.
- [Descending steering physiology](https://pmc.ncbi.nlm.nih.gov/articles/PMC10614758/): DNa02 and DNg13 contribute graded steering through different leg adjustments. This motivates bilateral continuous turning populations, not named behavioral commands.
- [Central-complex navigation](https://www.nature.com/articles/s41586-021-04067-0) describes heading/travel vector computations. Current heading receptors are a simplified proprioceptive input, not an implemented EPG/PEN ring attractor.
- [Connectome-based feeding model](https://www.nature.com/articles/s41586-024-07763-9) demonstrates sensory-to-motor propagation using connectivity and transmitter predictions. Later extract sweet gustatory-to-feeding pathways rather than assuming synapse counts fully determine physiology.
- [Reward dopamine neurons](https://www.nature.com/articles/nature11304) motivate a PAM-like reward population driven by ingestion. [Mushroom-body re-evaluation](https://www.nature.com/articles/nature21716) describes reward-associated depression of active KC-to-avoidance-MBON synapses, shifting the balance toward approach. Dopamine is not a universal positive Hebbian increment.
- [Olfactory habituation](https://elifesciences.org/articles/39569) involves temporally distinct inhibitory circuits. Our slowly adapting sensory gain is only a phenomenological analogue, not a reconstruction of those circuits.

## Sensory and learning model
Food emits an explicitly fictional volatile cue: crystalline sucrose itself is not an odor source. Two receptors sample a local concentration field with 180 px cutoff; the brain never receives object coordinates. Taste requires physical contact. Feeding motor activity gates ingestion. Ingested mass drives a low-pass PAM-like rate and decaying dopamine, reduces hunger and replenishes energy. Hunger/energy affect neural currents only.

Four KC-like feature populations encode left/right cursor and food cues. They are a deliberately tiny identity feature basis, not the fly's sparse combinatorial expansion. Each has a decaying eligibility trace (2 s). Reward depresses its bounded avoidance-readout weight: dw/dt = -eta * dopamine * eligibility * w. A fixed approach pathway opposed by that readout turns reduced avoidance into cue-dependent attraction. The same rule applies to every feature; there is no cursor-specific reward rule. Eligibility is presynaptic in this first model; a replaceable plasticity interface permits compartment-specific rules later. Weight limits prevent runaway dynamics. Habituation adapts separately and recovers without input. Weight changes demonstrate association capacity, not a guarantee of useful free-roaming learning.

## Modules and budget
Core: habitat/physics, receptors, rate brain, replaceable plasticity, internal metabolism, sugar environment and simulation orchestration. Desktop: native cursor/monitor bridge, small transparent creature window, click-through sugar windows and live inspector. Checks: deterministic console experiments independent of Windows.

Estimated core state below 100 KB and fewer than 1 million edge operations/s. WPF dominates memory (budget 100–250 MB) and rendering CPU (target under 5% of one modern desktop core); these are estimates, not measurements. A later 1,000-population/50,000-edge model at 120 Hz needs about 6 million edge accumulations/s and a few MB of core state. Benchmark before expanding; whole-brain spiking simulation is outside this budget.

## Stages
1. Complete the existing closed loop with contact, constrained dragging, local food, feeding/reward, plasticity, debug readouts, reset and deterministic experiments.
2. Measure paired versus unpaired learning across seeds, motor changes, habituation/recovery and performance; tune without adding behavioral scripts.
3. Offline export a pinned neuPrint/FlyWire subgraph with neuron IDs, type, side, transmitter confidence, synapse counts, dataset release and license. Normalize grouped edges, retain provenance, separate measured edges from synthetic adapters and compare ablations.
4. Replace selected MB compartments and descending pathways; add central-complex heading populations, richer taste channels and flight motor/physics. Validate each circuit independently.
5. Optional versioned saved learned-state files. Persistence design separates weights from transient dynamics; full deterministic checkpoints additionally need RNG, sensor history, eligibility and environment state.
