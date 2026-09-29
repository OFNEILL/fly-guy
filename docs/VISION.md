# Two-eye vision

The desktop application now uses `desktop pixels -> two retinas -> local contrast/change/motion processing -> visual rate populations -> brain -> motor neurons`. It does not use OCR, accessibility data, window titles, object detection or cursor coordinates as sensory input. Win32 cursor coordinates are used only by user interaction tools and to composite the actual cursor icon into the captured pixels; they never enter `Sensors` or `Brain`.

Open **Fly Vision** in the main inspector. The five modes show both eyes side by side while simulation continues. Settings apply live; changing geometry or rates clears visual temporal history. Closing the viewer does not stop vision. Closing the main inspector stops capture and exits the application.

## Research and chosen approximation

The Drosophila compound eye has roughly 750 ommatidia per eye, with R1–R6 photoreceptors contributing to motion vision and angular sampling on the order of 4.5 degrees. Its physiology and active photoreceptor sampling are much richer than a fixed camera grid ([Juusola et al., 2017, primary research](https://elifesciences.org/articles/26117)). This model intentionally uses only **24 angular columns × 10 distance rows per eye**, with approximately 5.8-degree angular spacing at the default FOV. It does not model neural superposition, microsaccades or 750 biological ommatidia.

T4 and T5 distinguish moving brightness increments and decrements and project by preferred direction ([Maisak et al., 2013](https://pubmed.ncbi.nlm.nih.gov/23925246/)). Here, signed local contrast is split into nonnegative ON/OFF channels, and delayed neighboring signals are correlated separately for both polarities. The opponent difference provides signed horizontal and radial motion. This is an elementary correlator approximation, not a reconstruction of T4/T5 cells. Real direction-selective computation includes both preferred-direction enhancement and null-direction suppression ([Leong et al., 2016](https://pubmed.ncbi.nlm.nih.gov/27488629/)); our circuit omits that full circuitry.

Default time constants are chosen for an interactive desktop demonstration: receptor filter 25 ms, correlation delay 60 ms and early visual-neuron filter 60 ms. Capture at 20 Hz deliberately undersamples fast biological vision and can alias fast motion. These parameters are not fitted measurements. There is no current UV/color receptor model, stereo depth estimation or looming detector.

## Geometry

Coordinates are physical desktop pixels, including negative monitor coordinates. Each eye has its own body-relative forward/lateral offset, yaw and FOV:

| Setting | Left | Right |
| --- | ---: | ---: |
| Forward offset | 18 px | 18 px |
| Lateral offset | -6 px | +6 px |
| Yaw relative to heading | -40° | +40° |
| FOV | 140° | 140° |
| Near / far extent | 12 / 240 px | 12 / 240 px |

The default angular fields overlap by 60°. Body rotation rotates both the eye origins and their axes. Columns traverse a fan from counterclockwise to clockwise. Rows span the desktop plane, **far at the top and near at the bottom**. This is a planar sampling model: rows are not elevation angles and do not imply inferred three-dimensional depth. There are no semantic occlusion rays; visible desktop pixels already reflect the Windows compositor's occlusion.

Each receptor averages 4×4 pixel taps distributed over its angular/radial cell, then applies temporal filtering. Geometry and arrays can be replaced through `VisionSettings` and `EyeVision`; per-eye offsets and time constants are configurable in code. Columns, rows, FOV, yaw, range and all five rates are editable in the viewer. The maximum configuration is 64×32 receptors per eye, up to 6×6 samples per receptor, with a 500 px range.

Monitor gaps, offscreen locations and regions outside the available capture are explicitly invalid. A cell with an incomplete footprint is blind, rather than padded with false black contrast. Newly acquired cells prime their histories without inventing a motion flash. This sacrifices some usable samples along screen boundaries in exchange for an unambiguous validity rule.

## What reaches the brain

The brain receives each eye's independent pooled early-neural activity, mean luminance, mean absolute contrast, temporal-change activity and clockwise/counterclockwise motion. Per-receptor neural targets combine absolute contrast, temporal change and local motion magnitude, then low-pass filter at 60 ms. Activity/change/directional motion use a bounded RMS pool with gain 3, so small moving patches can matter without passing an image or object identifier into the brain.

Visual populations have signed projections to the existing orienting, threat, exploration and motor populations. There is no `object left -> turn left` branch. The local radial-motion map contributes to visual activity, but does not yet implement an expansion/looming detector. The old cursor-distance, cursor-bearing, cursor-velocity and looming shortcuts were removed. Visual learning now operates on coarse left/right eye activity, so it can generalize broadly across unrelated images; it cannot identify a cursor as an object.

Nonvisual senses remain explicit:

- Sugar's fictional odor field -> bilateral chemical receptors; body contact -> taste; feeding gates ingestion.
- Spray concentration -> bilateral irritant receptors and body contact.
- Twelve-pixel virtual boundary feelers -> left/right/front touch channels. These replace the old 160 px invisible boundary rangefinders.
- Held/touch input and body velocity/heading -> mechanosensation and proprioception.

The viewer displays all these values in a separate **Other senses** panel. Sugar coordinates are used only by the environmental/receptor layer, never fed into the brain. The brain cannot infer a special sugar label from yellow pixels. Learning exports are now version 3; importing version 1/2 preserves odor memory but resets the two old cursor-specific feature weights because they are not equivalent to the new visual features.

## Fly Vision views

| Mode | Actual data displayed |
| --- | --- |
| Raw | The pixel taps from each eye's warped source footprint, before receptor pooling; includes color only for diagnosis |
| Receptors | Exact current filtered luminance array, enlarged with nearest-neighbor cells |
| Contrast / edges | Signed center-surround contrast: cyan brighter than neighbors, coral darker; range [-1,1] |
| Motion | Gold temporal-change magnitude plus local signed motion vectors; dot marks the arrow head |
| Neural | Exact early visual-neuron array used by the pool, plus numeric inputs and the receiving brain population's activity |

The **sampling map** shows the captured local desktop with separate eye origins, FOV boundaries, axes and receptor locations. It is inside the excluded debugger window and does not enter the capture. It can show source pixels outside the sampled fans; those pixels are not supplied to the brain. Stage timestamps make the different capture/receptor/processing ages observable. Processed views use the validity mask from their own processing stage.

The inspector and viewer, as well as the creature's own overlay, request Windows [`WDA_EXCLUDEFROMCAPTURE`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity) to avoid self-stimulation from debugging. Sugar and spray windows remain visible. Capture fails visibly if the exclusion API fails. Windows 10 version 2004 or newer is needed for true exclusion; protected content, remote desktop and particular compositor/driver configurations can still yield unavailable or blank surfaces.

## Clocks, costs and failure behavior

| Stage | Default target | Scheduling |
| --- | ---: | --- |
| Desktop capture | 20 Hz | One background worker; one latest bounded frame, no work queue |
| Receptors | 30 Hz | Independent monotonic deadline on the update timer |
| Visual processing | 30 Hz | Separate deadline and temporal state |
| Brain/body | 120 Hz | Fixed steps driven by a dispatcher update timer, independent of rendering callbacks |
| Creature rendering | 60 Hz | Throttled compositor callbacks |
| Fly Vision paint | At most 15 Hz | Diagnostic refresh; hiding/closing it does not stop capture or simulation |

Capture copies a local square around the fly (608×608 px by default), clipped to actual monitor rectangles, using native GDI `BitBlt` with layered-window support. The hardware cursor icon is composited with [`DrawIconEx`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-drawiconex) because ordinary screen copies omit it. Screen buffers stay in memory; the application does not save or upload screenshots.

The viewer reports measured rates, average milliseconds per update, capture frame count and frame age. It also measures the eye viewer and brain graph's CPU drawing work. GPU/compositor time is not included. Higher receptor counts, more samples and larger capture regions increase cost. The UI thread remains shared by receptor processing, brain ticks and drawing; this is independent scheduling, not a hard real-time guarantee.

Visual processing can re-sample the latest frame at a newer body pose, and can process a receptor array more than once. It does not pretend each stage has received a new desktop frame. Failed/stale capture clears vision and supplies zero visual input; body physics and explicit nonvisual senses continue. Frames older than `max(0.5 s, 2/captureHz)` are stale. Pausing suspends capture/brain updates and preserves the last displayed visual state with a paused label. Long stalls cap simulation catch-up at 0.1 s rather than accumulating an unbounded queue.

## Validation

`dotnet run --project tests/FlyGuy.Checks -c Release` includes deterministic image experiments:

- Distinct eye positions/directions, overlapping FOVs and rotation of the entire eye geometry.
- Uniform images give no edges or directional motion; uniform flicker yields temporal change without directional motion.
- A moving pointer-shaped pixel patch gives mean temporal activity 0.2101 versus approximately zero when stationary.
- Opposite rotating gratings give signed motion 0.1574 and -0.1577.
- Debugger arrays numerically reproduce the pooled neural input; invalid monitor gaps do not create edges; clearing vision removes all visual state.
- Left/right pixel patches lead through both retinas into the full simulation, producing asymmetric motor outputs across three seeds (mean turns approximately -0.11 to -0.17 versus +0.30 to +0.38).
- No-pixel input cannot acquire a hidden visual food signal; changing brain rates to 60/120/240 Hz remains bounded.

`tests/Smoke-VisionDesktop.ps1` checks live capture, opening/reopening the viewer, all five modes, changing geometry and rates, pause/resume and shutdown. A run with rendering limited to 8 Hz maintained approximately 120 brain ticks/s while capture targeted 12 Hz and receptor/processing clocks targeted 24/18 Hz. These are runtime checks, not proof of visual fidelity on every display. Mixed-DPI transitions, protected/video surfaces, cursor variants and sampling-map alignment require manual testing on the user's desktop.

Future extensions can replace fan geometry with ommatidial angular maps, add photoreceptor classes or richer optic-lobe modules, and expand visual learning without changing the pixel-source/retina/brain boundary. A future looming pathway must consume visible retinal data rather than reintroducing cursor geometry.
