# Fly Spray: mechanisms and experiments

The [two-eye vision update](VISION.md) replaces the original cursor-coordinate features with generic left/right pixel-derived visual activity. Cursor-specific recognition is not supplied. Learned-state exports now use version 3; old cursor features reset on import, while odor memory is retained. The experiments below describe controlled sensory-cue conditioning and remain applicable to the new generic visual input channels.

Fly Spray is a fictional nonlethal irritant, not a simulation of commercial insecticide poisoning. The brain receives local receptor readings, never spray coordinates or a requested escape direction. All connections and numerical kinetics below are synthetic population-level approximations.

## Biological basis

Both appetitive and aversive reinforcement can be dopaminergic in Drosophila; dopamine is not intrinsically a reward scalar. Sugar-responsive PAM neurons support appetitive memory ([Liu et al., 2012](https://www.nature.com/articles/nature11304)). PPL1 subpopulations participate in aversive taste memory ([Kirkhart and Scott, 2015](https://pubmed.ncbi.nlm.nih.gov/25981787/)). The implementation uses separate PAM-like and PPL1-like signals; these labels do not assert that every neuron in either cluster has the same valence.

Compartment-specific changes to Kenyon-cell (KC) output can rebalance approach- and avoidance-promoting mushroom-body output neurons ([Aso et al., 2014](https://pubmed.ncbi.nlm.nih.gov/25535794/)). Here, reward depresses cue-to-avoidance weights and punishment depresses cue-to-approach weights. This is a small opponent readout model, not a reconstruction of mushroom-body anatomy. Four identity features represent bilateral cursor and food cues; they omit the fly's sparse combinatorial coding.

Flies can form competing appetitive and aversive memories of a compound food stimulus ([Das et al., 2014](https://pubmed.ncbi.nlm.nih.gov/25042590/)). Accordingly, this model preserves two sets of weights rather than collapsing reward and punishment into one signed value.

Octopamine/tyramine signaling can modify circuit activity and sensory-driven startle responses, including through astrocytes ([Ma et al., 2016](https://www.nature.com/articles/nature20145)). Acute arousal here is an OA-like functional abstraction: it increases selected sensory gains, accelerates rate integration and changes motor-population excitability. Astrocytes, receptor subtypes and measured octopamine concentrations are not simulated. Octopamine also has functions beyond threat.

Repeated visual threats can produce persistent, scalable defensive arousal and suppress feeding ([Gibson et al., 2015](https://pubmed.ncbi.nlm.nih.gov/25981791/)). Tachykinergic neurons can gate visual aversion following mechanical threat ([Tsuji et al., 2023](https://pmc.ncbi.nlm.nih.gov/articles/PMC10345120/)). These findings motivate a slower continuous defensive state. The model does not reproduce tachykinin release or theta oscillations, nor establish that fictional chemical spray recruits the experimental pathways identically.

Serotonin also modulates defensive responses in context-dependent ways, including activity and immobility during restraint ([2023 restraint study](https://pmc.ncbi.nlm.nih.gov/articles/PMC9840979/)). It is therefore not treated as a universal escape or stress signal here. There is no literal adrenaline, epinephrine, cortisol or mammalian HPA axis. The slow state is phenomenological, not an identified fly equivalent of cortisol. Escape remains walking driven by the existing motor populations; flight and giant-fiber takeoff circuitry are outside this model.

## Implemented pathway

1. A spray action adds a bounded cloud with a 110-physical-pixel radius. Concentration follows `C *= exp(-dt / 1.5)` and expires below 0.005. Within the radius, the local field is `C * (1 - (distance/radius)^2)^2`; outside it is zero. At most 64 clouds remain active.
2. Left/right receptors sample positions 20 pixels ahead and to each side; a contact receptor samples the body center. Overlapping concentrations add and saturate at 1. Only the receptor layer sees cloud geometry.
3. Filtered aversive sensory populations project to threat, opponent steering and a PPL1-like punishment population. Removing these projections removes spray's motor and modulatory effects.
4. Continuous modulation changes sensory gain, integration speed, exploration, feeding/braking excitability and plasticity. No spray handler changes velocity, heading or motor output.
5. Cue-dependent approach and avoidance populations compete through signed connections. Their rectified opponent difference drives a learned-threat population. Cue recall can increase acute arousal but does not synthesize another punishment teaching signal.

Default decay constants are independently configurable in the inspector, or through `Brain.ThreatSettings`:

| Quantity | Decay constant | Input and consequences |
| --- | ---: | --- |
| Acute arousal | 2 s | Punishment or learned threat; rapid gain/excitability changes |
| Aversive neuromodulation | 1.2 s | PPL1-like activity; punishment learning |
| Slow stress | 35 s | PPL1-like activity; vigilance, altered exploration, stronger punishment plasticity |
| Sensitization | 180 s | PPL1-like activity; increased gain of subsequent irritant responses |

These are model choices for desktop experiments, not measured biological constants. Each bounded state integrates `dx/dt = rise * input * (1-x) - x/decay` exactly for constant input within a tick. Repeated input accumulates continuously; there are no exposure counters or discrete frightened states. Recovery constants describe decay after neural input has stopped, not time to reach zero. UI values range from 0.05 to 3600 seconds.

The existing harmless-cursor habituation trace lowers cursor gain and orienting drive and recovers without input. Sensitization increases irritant gain after punishment. These are separate phenomenological neural adaptation mechanisms, not a claim that biological long-term habituation or consolidation has been reconstructed.

## Plasticity and competition

Each of the four features has a bounded presynaptic eligibility trace with a two-second decay. Reward depresses its avoidance weight at rate `0.22 * reward * eligibility`; aversion depresses its approach weight at rate `0.65 * aversion * (1 + 0.5 * stress) * eligibility`. Both integrate multiplicatively and remain in [0.05, 1]. The identical rule applies to cursor and food features. No cursor-specific association flag exists.

Both readouts feed the same orienting and opponent-turn circuits. Hunger and energy still change neural currents. Neither food nor threat has an explicit priority rule. Learning can eventually depress both readouts near their floors; reversal, extinction, consolidation and multi-timescale memory retention are not implemented. Reset restores both weight sets. Version 2 learned-state export includes both arrays; importing version 1 gives neutral approach weights. Export is an in-memory API, not automatic file persistence, and excludes transient state.

## User experiment

Select **Fly Spray** in the inspector and choose **Apply at cursor in 3s**, then move the cursor near the fly. Alternatively use **Ctrl+Shift+F** at the cursor. **Ctrl+Shift+S** drops sugar; **Ctrl+Shift+Space** applies whichever tool is selected. Shortcuts trigger once per keypress. Escape cancels the countdown. Pausing cancels pending actions and pauses cloud decay with simulation time.

Pink translucent circles show the spray footprint and fade with concentration. The brain inspector shows all receptor and learning populations, both weight arrays, eligibility, reward, aversion, acute arousal, stress and sensitization. Separate 120-second plots retain motor/reward activity, spray/threat/modulatory activity and mean plastic weights. Reset clears those plots and brain state but leaves environmental objects in place: an existing cloud can immediately stimulate the reset brain.

For conditioning, present the cursor shortly before spraying repeatedly, then allow clouds and stress to dissipate before probing with the cursor alone. Compare with trials where spray follows the cue after a long delay. Simultaneous sugar and spray can engage both teaching signals. Free movement changes exposure and cue timing, so useful conditioning during unconstrained interaction is not guaranteed.

## Automated evidence

Run `dotnet run --project tests/FlyGuy.Checks -c Release` from the repository root. Checks cover local sensing, no response to distant spray, expiration, held-body invariance, fast/slow recovery, repeated-exposure accumulation, sensitization and recovery, configurable decay, paired versus delayed aversive conditioning across five seeds, cue-only neural and motor recall, simultaneous reinforcement, sensory-projection ablation, two-compartment learned-state persistence/reset and deterministic repeated-spray simulations.

After eight trials with a one-second cue immediately preceding one second of contact stimulus, the cue's approach weight was 0.050 versus 0.922 in a ten-second-delay control. Fresh brains loaded with those weights showed right-turn outputs 0.839–0.876 versus left-turn outputs -0.620 to -0.699 on a left-cue probe across seeds 1–5. This isolates memory from transient stress; it is not evidence of a biologically calibrated escape response.

Fresh-brain probe turns following reward-only, simultaneous reward/punishment and punishment-only conditioning were -0.871, -0.391 and 0.889 respectively. These controlled sensory experiments demonstrate competition in this circuit, not a universal behavioral outcome.

`tests/Smoke-SprayDesktop.ps1` passed a desktop automation check for startup, selecting Fly Spray, applying decay settings, timed cloud creation, pause/reset/resume and clean shutdown. This checks control behavior, not rendering fidelity. Hotkeys, visual cloud alignment and mixed-DPI behavior still need manual validation.
