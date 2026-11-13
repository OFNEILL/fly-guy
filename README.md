# Fly Guy

A desktop creature driven by a small recurrent neural simulation, with a Windows frontend and an initial macOS frontend. **Milestone one uses synthetic weights, not real fly-connectome connectivity.** The research and the staged plan for replacing them are in [docs/RESEARCH.md](docs/RESEARCH.md).

For macOS 14+ (Apple Silicon or Intel), see the [Mac build and installation instructions](packaging/macos/README.md), including current feature differences and validation limits. The instructions below describe the Windows frontend.

## Run

Windows user releases now use the [Velopack installer and update feed](installer/README.md). Installed Release builds show compact fly controls without the neural laboratory. Automatic updates download from this repository's public stable GitHub Releases and install on the next launch. Debug builds show the laboratory; to inspect a Release build, launch `FlyGuy.Desktop.exe --dev`. Developer mode disables update checks. Pushes and merges to `main` build and publish stable Windows releases automatically, starting at `1.0.0` and incrementing the patch version thereafter. Local packaging does not publish.

Open **Fly Vision** for two independently sampled desktop eyes, five live visualisation modes and configurable geometry/update rates. The brain receives processed pixel signals; it no longer receives cursor coordinates. See [vision design, research and checks](docs/VISION.md).

Fly Spray adds local aversive receptors, separate reward/punishment learning, acute arousal and slowly recovering stress. See [mechanisms, sources and experiments](docs/SPRAY.md).

Install the .NET 10 SDK on Windows, then from this directory:

```powershell
dotnet run --project FlyGuy/FlyGuy.Desktop
```

The creature appears in a transparent, topmost window. Move the cursor within its visual fields to stimulate its eyes, or grab it with the left mouse button. Select Sugar, Fly Spray or Cigarette in the inspector and apply it at the cursor after a three-second countdown. Ctrl+Shift+S drops sugar, Ctrl+Shift+F sprays, Ctrl+Shift+C drops a cigarette, and Ctrl+Shift+Space applies the selected tool. Escape cancels a countdown. Minimize the inspector to observe the desktop; restore it from the taskbar. Pause/resume and Exit are in the inspector. Closing the inspector exits the entire application. Nearby desktop pixels are processed in memory; no screenshots are saved or uploaded. No global hooks, OCR or startup registration. Optional fly messaging connects only to the relay you configure; see [messaging setup](docs/MESSAGING.md).

```powershell
dotnet run --project tests/FlyGuy.Checks -c Release
dotnet publish FlyGuy/FlyGuy.Desktop -c Release -r win-x64 --self-contained false -o artifacts/publish
```

Published output requires the .NET 10 Windows Desktop Runtime.

## Primitive speech

The Windows fly speaks on its own in one-word bubbles: **hungry**, **food**, **yum**, **scared**, **ouch**, **sleep**, **stop**, **go**, or **buzz**. A smoothed readout of neural activity, hunger, energy, fatigue and threat selects its strongest sustained signal. Words appear for four seconds, with at least fourteen simulated seconds between utterances and forty-five seconds before repeating a word. Pausing also pauses speech decisions. Disable **Primitive speech** in the inspector to mute it; resetting the brain preserves that choice for the session.

Speech runs locally and works offline. It is a fictional expression of the existing simulated state. Owner messages still take priority for their full twelve-second bubble. The shared core computes speech on both platforms; the current bubble display and mute control are Windows-only.

**Speak aloud** is enabled by default and reads these self-generated words through the installed Windows voice, alongside their bubbles. It uses local speech synthesis, with no API key or network call. Turn off **Speak aloud** to keep just the bubbles. Pausing, muting primitive speech, or closing the app stops the voice. Owner-written messages retain their text bubbles and interrupt any current self-generated audio.

## Talk and teach

Windows desktop actions are available in **Talk to fly**. Autonomous actions are enabled by default; **Let the fly act on its own (this session)** lets you turn them off. Its simulated state chooses actions: a calm fly with curiosity above 0.35 may open Notepad once per session; arousal above 0.35 may move a cursor within 180 pixels of the fly by up to 160 pixels toward its screen centre. There is a 30-second startup delay and at least 60 seconds between attempts. Pause, holding the fly, and active owner-message priority suppress autonomous actions. Mouse buttons must be released before cursor movement. The toggle remains active when the panel closes and returns to enabled on app restart; reopen the panel to disable it. These are fixed state-based behaviours, not general computer control.

For manual actions, enable **Enable desktop actions for this session**, then use the buttons or type **open notepad** / **move cursor away**. Manual actions have a five-second cooldown and require typed input; microphone recognition and learned replies cannot execute actions. The manual toggle resets when the panel closes. No files are edited and no clicks, keystrokes or shell commands are generated.

On Windows, open **Talk to fly** and click **Start listening**. Speak a short phrase into the default microphone. The app uses an installed Windows speech recognizer locally and answers with a one-word bubble and, when **Speak aloud** is enabled, its voice. Recognition needs a Windows speech-recognition language and microphone access; the panel reports missing setup. You can also type into **Heard phrase** and click **Say this to the fly**.

The fly learns simple associations, rather than using a conversational language model. An unfamiliar phrase is associated with its current brain state; familiar phrases recall learned replies, with word overlap used for similar phrases. Say **say apple** or **learn apple** to teach a new response word. To correct or teach a phrase, fill in **Heard phrase**, enter a one-word **Teach reply**, and click **Remember this reply**. Taught phrases and vocabulary are added to recognition hints when listening starts, including after explicit teaching.

Words, phrase counts and response associations persist across restarts in `%LOCALAPPDATA%\FlyGuy\language-memory.dat`, encrypted for the current Windows account. Memory is bounded to 1,024 words and 512 phrases; low-frequency phrases are replaced when full. No microphone audio is saved or uploaded. **Clear learned words and phrases** clears the saved language memory. Resetting the simulated brain leaves this separate language memory intact.

Listening is off until started, stops when the conversation panel closes or the fly is paused/muted, and filters out low-confidence recognition and speech occurring during the fly's own audio. Owner messages keep priority, and replies have a four-second cooldown. Headphones help avoid speaker feedback. This learning stores vocabulary and associations; it does not retrain Windows' acoustic recognition model or provide general language understanding.

Windows conversation checks: `dotnet run --project tests/FlyGuy.Conversation.Checks -c Release`. These cover encrypted persistence, microphone startup, local recognition of synthesized audio, learned replies and synthesized response audio. The core checks also cover correction, generalization and memory limits.

## Cigarettes

Drop a cigarette using the tool selector, or click **Give cigarette** for a puff at the fly's position. **Autonomous smoking** is enabled by default: the fly senses local tobacco cues, steers toward them through its neural motor circuit and can try smoking without being prompted. Disable the checkbox to stop autonomous seeking/use. Held flies only smoke when given a puff manually.

Each cigarette provides up to eight puffs with an eight-second cooldown. The first puff removes it from the desktop and transfers it to the fly. The fly can keep puffing on the carried cigarette; it disappears when depleted or after 60 simulated seconds. Windows shows the carried cigarette beside the fly while it is lit. Unlit cigarettes wait until used; pausing also pauses burning. Each puff produces very large **fictional** cortisol, epinephrine and norepinephrine spikes (initially 20/24/24 relative units, capped at 40), plus nicotine and a reward signal. These affect arousal, sensory gain, locomotor activity and learning. Dependence builds quickly (35% of the remaining capacity per puff). Nicotine wears off with a 10-second decay time, bringing stronger withdrawal and craving that reinforce seeking. Dependence recovers slowly with a 30-minute decay time; repeated use also builds tolerance. Dependence and tolerance gradually recover during abstinence. The inspector shows the chemistry and addiction state; **Reset brain** clears them. This is a gameplay model, not a claim about fly endocrinology. Addiction state is session-local and is not included in exported learning weights.

## How it works

Two 24×10 retinas sample different overlapping fields of desktop pixels. Luminance, contrast, temporal change and delayed-neighbor motion feed separate visual rate arrays and 55 brain populations. Explicit nonvisual pathways provide odor, taste, irritant/contact, boundary touch and proprioception. The brain runs at 120 Hz by default, independently of capture and rendering, with recurrent excitation/inhibition, adaptation and seeded neural noise. Drives, metabolism, arousal and stress modulate neural processing. Reward and aversive signals modify opposing cue readouts through eligibility-based plasticity. Four locomotor populations provide thrust, opposing turn signals and braking; a feeding population gates ingestion. Only locomotor outputs feed the body physics; no chase/wander/startle state machine selects movement. Animation follows actual travel. Boundary projection prevents leaving the monitor union but does not choose a heading.

The inspector displays every population, signed connections, drives, reward/aversion, acute arousal, slow stress, sensitization, both plastic weight sets and 120-second histories. Recovery rates can be adjusted independently. Cyan connections excite; coral connections inhibit. Synthetic populations are functional abstractions, not identified Drosophila cell types. Automated conditioning experiments demonstrate limited associative plasticity, not biological realism or general intelligence.

## Limits

The desktop is a flat space; windows are not obstacles. Adjacent monitors share traversable seams; separated monitor islands require repositioning or a future flight mechanic. The creature's center is constrained, so its legs can clip at a screen edge. Mixed-DPI placement uses physical coordinates and a per-monitor-aware window, but needs manual testing on your particular display arrangement. Simulation catch-up is capped after stalls/sleep. The inspector remains available on the taskbar; there is no tray icon. No real data is bundled or downloaded at runtime.
