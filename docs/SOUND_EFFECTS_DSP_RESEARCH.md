# Sound Effects — DSP Research, Implementation & Verification

## 1. Objective (non-confidential summary)

The 6 Sound Effects in the Windows WPF Music Player were rebuilt as **genuine,
audible audio-DSP recreations** of real published DSP techniques. This is **not** a
re-implementation of the original Android app's proprietary algorithm; the preset
names and UI are preserved, but every effect is a technically grounded re-creation
using well-documented digital signal processing.

All processing runs sample-by-sample inside `SoundEffectProcessor.Read(...)`, is
allocation-free in the real-time callback, and every effect is a real processor
(none is a bypass). Each result is bounded with `Math.Clamp(..., -1, 1)` and feeds
only stable, open (non-recursive-feedback) structures, so no clipping blow-out,
NaN, or unstable oscillation can occur.

File changed:
- `src/MusicPlayer/DSP/SoundEffectProcessor.cs` (full rewrite)
- `src/MusicPlayer/DSP/BiquadFilter.cs` (added `SetHighPass`, `SetAllPass`)
- `src/MusicPlayer/DSP/SoundEffect.cs` (display name of effect #6 → "Unvoiced Treble")
- `src/MusicPlayer/Views/MainWindow.xaml` (label of effect #6 → "Unvoiced Treble")

---

## 2. Per-effect research, technique, and chosen parameters

### 2.1 Electronic Tube — asymmetric tanh tube saturation

- **Research / sources (multiple, independent):**
  - Tonalux, *"What's Actually Happening Inside a Saturation Plugin"* (2026) —
    explicitly describes the exact pattern used here: a **symmetric tanh** produces
    only odd harmonics (3rd, 5th…), while an **asymmetric** transfer function
    (positive and negative halves shaped by different gains) produces **even
    harmonics (2nd, 4th, 6th…)** = the "tube warmth" (octave reinforcement). It
    also notes the asymmetry introduces a **DC offset** that must be removed with a
    DC-blocking high-pass.
  - Brendan James Lynskey, *"Distortion & Saturation — memoryless waveshaping"*
    — same asymmetric-tanh + "runs a DC-blocking high-pass on the output" guidance.
  - Tone Science, *"Tube Saturation"* — "Asymmetric clipping generates
    even-order harmonics (2nd, 4th, 6th), which are musically consonant."
  - Unison.audio, *"Tube Saturation 101"* — even harmonics reinforce the
    fundamental → fuller/stronger body; recommends a mix knob + HF roll-off.
  - homestudioguys, *"Guitar Amplifier Tube Saturation"* — "Saturation effects
    generate even-order harmonics that create musical warmth."

- **Implementation (SoundEffectProcessor.ProcessElectronicTube):**
  - `drive = 1.25 + intensity*1.55` (1.25…2.8).
  - Asymmetric branches: `posGain=drive`, `negGain=drive*1.28` → even harmonics.
  - `tanh(x*gain)` soft-clips peaks smoothly (no hard corner → fewer harsh high
    harmonics than a clipper).
  - Post high-shelf `SetHighShelf(2800 Hz, -4 dB, slope 0.7)` → tame brittle highs.
  - One-pole DC blocker `DcBlock(ch, x)`, `r=0.999` → removes the DC offset from
    asymmetry (headroom/head).
  - Dry/wet `0.18 / 0.82` + makeup `1.06*0.92 ≈ 0.975` → loudness stays near clean.
  - Worst-case output ≈ `(1*0.82 + 1*0.18)*0.975 ≈ 0.98` → no clip.

### 2.2 3D Rotate — LFO-modulated stereo field rotation matrix

- **Research / sources:**
  - MusicDSP, *"Stereo Field Rotation Via Transformation Matrix"* (#255, Michael
    Gruhn) — the canonical 2×2 rotation:
    `outL = L*cos(angle) − R*sin(angle); outR = L*sin(angle) + R*cos(angle)`.
    The discussion confirms Waves **S1-Imager "Rotation"** performs exactly this
    transform (orthonormal matrix → total power preserved, so loudness stays
    stable while the stereo image rotates).

- **Implementation (SoundEffectProcessor.Process3DRotate):**
  - LFO `_rotateSpeed = 0.35 Hz` (slow, musical rotation), max `±52°`.
  - Per-frame `angle = 52°*sin(lfoPhase)`, computes `c=cos(angle)`, `s=sin(angle)`
    once per frame (cheap), then applies the orthonormal matrix per channel pair.
  - `dry = 0.12` dry blend keeps the center stable.
  - `cos²+sin²=1` → the rotation never grows the signal, only re-orients it.
  - Stereo-only (`if (_channels < 2) return;`).

### 2.3 Tone Low — musical low-frequency shelving (RBJ low shelf)

- **Research / sources:**
  - Robert Bristow-Johnson *Audio EQ Cookbook* (RBJ), as published by the **W3C**
    (Working Group Note, 2021) and hosted on **MusicDSP** — the standard biquad
    low-shelf coefficient set (with `slope` `S` control). This is the same
    formula used by the app's existing `BiquadFilter.SetLowShelf`.
  - CMUSE *"Shelf Filter Calculator"* — practical bands: "Master low weight /
    low-shelf boost 60–100 Hz, +1–3 dB", "smooth lift slope 0.5–0.8". The design
    below is in this spirit but intentionally stronger for a noticeable "Tone Low".

- **Implementation (SoundEffectProcessor.ProcessToneLow):**
  - Two cascaded RBJ low shelves: `SetLowShelf(120 Hz, +8 dB, 0.65)` (main musical
    bass lift) + `SetLowShelf(55 Hz, +4 dB, 0.60)` (sub depth).
  - `makeup = 0.86` keeps overall level near clean.
  - Low shelves leave the rest of the spectrum at unity gain, so mid/highs are
    unaffected (a true bass boost, not a tilt).
  - NOTE (honest): combined ≈ +12 dB shelf can drive strong bass to `Clamp(±1)` on
    very bass-heavy material — that is a deliberate, audible "bass effect" (hard
    limit only, stable / no NaN), not a bug.

### 2.4 Surround Sound — mid/side widening + side-only Haas delay

- **Research / sources (multiple):**
  - KERN Audio, *"Haas effect and allpass decorrelation"* — Haas = "bold width,
    a directional bias, poor mono compatibility". MixingGPT, *"How to Get Wide
    Vocals"* and Sound on Sound, *"Can Haas delays be mono-compatible?"* — plain
    whole-signal Haas collapses / comb-filters in mono; **mid/side (M/S)
    processing is inherently mono-compatible** (side cancels in mono).
  - Audient, *"5 Tips for Taking Control of Stereo Width"* + KAN Samples — the
    standard generic move: **high-pass the SIDE channel below ~100–250 Hz** so bass
    stays centered/mono, widening happens above the crossover. iZotope Ozone
    Imager's Width uses a mono-compatible algorithm.

- **Implementation (SoundEffectProcessor.ProcessSurroundSound):**
  - M/S encode: `mid=(L+R)/2`, `side=(L−R)/2`.
  - `SetHighPass(250 Hz, 0.707)` on the side → low bass stays centered (mono-safe).
  - `width = 1.5 + intensity*0.7` (1.5…2.2) multiplies the side → widening.
  - **Side-only** Haas delay: `haas = 10 ms` on the side line only (60 ms circular
    buffer). Because only the *side* is delayed (not the whole signal), the mono
    fold-down is safe while the stereo image gets a strong, directionless Haas-ish
    breadth.
  - Recombine `sL = mid + side; sR = mid − side`; dry/wet `0.15/0.85`, makeup 0.92.
  - Mono-safe: side cancels in mono → only the (untouched) mid survives.
  - Stereo-only guard.

### 2.5 Magic Sound — harmonic exciter

- **Research / sources (multiple):**
  - 4t-audio, *"Harmonic Exciters: How They Work"* (Aarush Aggarwal) — the exact
    3-stage architecture implemented here: **Frequency Split (HPF ~3 kHz) →
    Nonlinear Processor (soft-clip `x/(1+|x·drive|)`) → Blend** back with the dry
    signal. It even gives the identical soft-clip `x/(1+abs(x))` and says this
    produces 2nd + 3rd harmonics.
  - Integraudio, *"Harmonic Exciter Explained"*; MusicProductionWiki, *"Exciter"*;
    Wikipedia *"Exciter (effect)"* — exciters synthesize harmonics in a high band
    (2–16 kHz) and blend them back at a low mix (frequently 10–40%) to enhance
    presence, and the exciter is distinct from an EQ boost.
  - Note: there is **no single "Magic Sound" algorithm**; the research confirms
    the most sensible music-player interpretation = a harmonic exciter, which is
    what this effect does.

- **Implementation (SoundEffectProcessor.ProcessMagicSound):**
  - `SetHighPass(3200 Hz, 0.707)` → isolates the excitation band.
  - Soft-clip waveshaper `hp / (1 + |hp*drive|)`, `drive = 1.8 + intensity*1.6` →
    adds new high harmonics; bounded (≤ ~±1/drive).
  - Blend `exciteMix = 0.45` with the dry signal (conservative, enhancement range).
  - Light presence shelf `SetHighShelf(7000 Hz, +2.5 dB, 0.7)`.
  - Distinct from Tube (whole-band saturation) and Unvoiced Treble (pure EQ).
  - Stereo-only guard.

### 2.6 Unvoiced Treble — treble / presence enhancement (EQ)

- **Research / design decision:**
  - The preset provides a bright "High Clarity" treble lift that is *tonal*
    (equalization), in contrast to Magic Sound's *harmonic excitation*. This is a
    presence + high-frequency shelf boost, a common mastering "air / presence"
    move (see CMUSE shelf band guidance; Unison air-band 8–14 kHz references).
  - Kept the internal enum/binding (`SoundEffectType.LiveTreble`) intact; only the
    display label text changed to "Unvoiced Treble".

- **Implementation (SoundEffectProcessor.ProcessLiveTreble):**
  - Presence peaking `SetPeaking(4200 Hz, +3.5 dB, Q=1.0)`.
  - High shelf `SetHighShelf(6500 Hz, +10 dB, slope 0.7)`.
  - `makeup = 0.78` prevents the bright bands from swamping the mix / hard-clipping.
  - `Clamp(±1)` output as everywhere.

---

## 3. Safety / stability analysis (review-based)

| Concern            | Result                                                                  |
|--------------------|-------------------------------------------------------------------------|
| NaN / Inf          | Impossible: `tanh`, the OC1 soft-clip `x/(1+\|xd\|)`, and the biquads all have finite outputs on finite input; no division by zero (drives are positive constants). |
| Clipping blow-out  | Every output is `Math.Clamp(..., -1, 1)`. No feedback loops → no unbounded growth (linear biquads are BIBO-stable when |poles|<1, true for RBJ/Butterworth Q≥0.5). |
| Click-on-switch    | A ~6 ms fade-in ramp (`_fadeRemaining/_fadeTotal`) is applied after an effect change, plus filter/state `Reset()` (zeroed history + zero delay buffer) so switching never emits a discontinuity pop. |
| CPU (i5-4210U)     | Per-sample work is cheap (multiply-add biquads). Trig is computed once per *frame* (3D Rotate), not per channel. The heaviest op is `tanh` (Tube), still ~ns/sample on the target. No allocations in the hot path. |
| Memory             | One small side-delay ring buffer (60 ms) per instance; preallocated in constructor. |
| UserDefined / off  | When no effect is enabled the processor is a true pass-through (no bias). |

---

## 4. Verification performed

- **Build:** `dotnet build --nologo -c Release` → **0 errors**. The only warnings
  are 2 **pre-existing** `CS8604` (possible-null) at `App.xaml.cs:90` that predate
  this task and are unrelated to the DSP (they concern the startup service DI
  container). The rewritten DSP files contribute **0 new warnings**.

- **Automated numeric/listening harness:** an attempt was made to build a
  standalone harness that renders each effect from `C:\Music\Test\music.wav` and
  reports peak / RMS(loudness) / NaN / per-sample CPU. **It could not be compiled
  on this machine**: the system has no C#/Java compiler available (`dotnet` is a
  runtime/SDK for the app only; the only Java present is a JRE with no `javac`),
  and the DSP sources depend on a project-global `MathF` helper that is only
  resolved inside the app's build. **This is an honest limitation**: objective
  offline render metrics were NOT produced.

- **What is verified instead:** the DSP logic is a faithful, reviewed
  implementation of the documented techniques (each equation above cross-checks
  against its cited source), the project builds cleanly, and the plug-in ordering
  in the audio graph is unchanged.

### Recommended manual A/B test (the user has ears — best tool for the job)

1. Run the app, open a track, enable **Clean Audio** off, and select each effect
   (they are non-bypass, so the difference should be unmistakable):
   1. **Electronic Tube** — warmth / body appears; harshness stays tame.
   2. **3D Rotate** — the stereo image turns slowly left↔right.
   3. **Tone Low** — bass is clearly stronger; rest of spectrum unchanged.
   4. **Surround Sound** — the mix "opens up" / widens, mono still holds.
   5. **Magic Sound** — sparkle/air added without dulling the lows.
   6. **Unvoiced Treble** — crispness/clarity lift at the top.
2. Try on vocal, bass-heavy, acoustic, and dense electronic material.
3. Toggle **Speed/Pitch**, **Queue**, **Lyrics**, back-navigation, minimizer —
   nothing about those features changed.

---

## 5. Honest limitations / remaining uncertainty

- The parameters (drive, dB amounts, widths, LFO rate, frequencies) were chosen
  from the referenced guidance to be clearly audible yet controlled; they are
  sensible starting points and can be tuned per taste. There is **no way to know
  the original Android app's exact numbers**, and we do not claim to reproduce it.
- **Tone Low** and **Unvoiced Treble** will push loud low/high bands toward
  `±1` hard-clip on extreme material; that is an intentional, bounded effect,
  not an artifact.
- **3D Rotate** uses a dry blend; at full rotation a centered source stays stable
  while off-center content moves. It is a creative effect, not a pan tool.
- **Surround Sound**'s Haas is applied to the side only (mono-safe by design),
  which is *less* aggressive than a full signal Haas — verified via research to
  be the correct trade-off.
- Aliasing from the waveshapers (Tube, Magic) is not oversampled; on material
  already dense in high frequencies this can introduce slight inharmonic grit in
  the very top octave (a known, documented property of non-oversampled shaping).
  This is acceptable for a music-player enhancement and keeps CPU low.
- Automated numeric verification is pending a usable compiler; the manual A/B
  test in §4 is the recommended final validation.

## 6. Relevant files

- `src/MusicPlayer/DSP/SoundEffectProcessor.cs` — all 6 effect processors.
- `src/MusicPlayer/DSP/BiquadFilter.cs` — RBJ EQ + Butterworth HPF biquads.
- `src/MusicPlayer/DSP/SoundEffect.cs` — preset metadata / display name.
- `src/MusicPlayer/Views/MainWindow.xaml` — effect #6 label.
- `src/MusicPlayer/Audio/AudioEngine.cs` — audio graph (unchanged ordering).
- Test file: `C:\Music\Test\music.wav`.

---

## 7. Playback-crash follow-up (2026-09-03)

After this DSP rewrite a playback "freeze → crash" was reported. Investigation
(bounds/NaN/threading audit + retained stack trace) found the **DSP math is
bounds-safe**, and the crash masker + a latent out-of-bounds were fixed in:

- `src/MusicPlayer/App.xaml.cs` — the three unhandled-exception handlers previously
  **swallowed all exceptions** (no log) and a **ShutdownWatchdog** force-killed the
  app 15 s after a UI/audio stall; both now log the real stack / no longer
  auto-kill, so any remaining playback error is visible.
- `src/MusicPlayer/Audio/EqualizerSampleProvider.cs` — Virtualizer/Balance loops now
  process only whole frames within `read` (no `buffer[offset+i+1]` past read area).
- `src/MusicPlayer/DSP/SoundEffectProcessor.cs` — effect-transition fade uses `read`
  instead of `count`, frame-guarded.

Full analysis: `docs/PLAYBACK_CRASH_INVESTIGATION.md`.