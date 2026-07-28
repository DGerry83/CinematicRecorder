# NVENC Zero-Copy Test Harness

Standalone Windows console harness that proves (or disproves) the C++ NVENC GPU
zero-copy encode path outside Unity/KSP. It compiles the real
`src/NvencEncoder.cpp` (unmodified except one diagnostic `SetError` line), feeds
generated test-pattern textures through the real exported API
(`CR_InitNvencEncoder` / `CR_EncodeNvencFrame` / `CR_ShutdownNvencEncoder`), and
verifies the resulting `.mkv` — resolution, color, orientation, frame order —
using the shipped `ffmpeg.exe`. Encoder fixes are out of scope: known latent
bugs (F2/F3/F4/F8/F9...) are *measured*, not fixed.

## Build (dev box)

From `CinematicRecorderNative\` (Git Bash: `cmd //c build_harness.bat`,
PowerShell/cmd: `cmd /c build_harness.bat`):

```
cmd /c build_harness.bat
```

Produces `build\harness\NvencHarness.exe` and stages the full
`GameData\CinematicRecorder\PluginData\FFMpeg\` runtime (ffmpeg.exe + 8 DLLs)
next to it. `build\harness\` is the xcopy unit for the NVIDIA PC.

## Run on the AMD dev box (no NVENC present)

```
NvencHarness.exe --selftest
```

Builds a reference clip by piping the raw test pattern to the shipped
`ffmpeg.exe` (libx264, fallback rawvideo) and runs the identical verification
chain. Must PASS all four checks, exit code 0. This proves the pattern, the
ffmpeg plumbing, the BMP parsing, and the thresholds without any NVENC.

```
NvencHarness.exe
```

Encode mode. Must fail loudly within seconds: the console prints a non-empty
native error string naming `nvEncodeAPI64.dll`, exit code 2.

## Run on the NVIDIA PC (what to send back)

Xcopy `build\harness\` to the NVIDIA PC, then from that folder run, in order:

```
NvencHarness.exe
NvencHarness.exe --format rgba
```

Send back the **full console output of both runs** (plus exit codes). Expected
evidence per run:

- **Default `--format bgra`** (mimics the Unity render texture layout):
  exercises **F3** — the native encode textures are `R8G8B8A8`
  (`NvencEncoder.cpp:363`) and `EncodeFrame` does a blind `CopyResource`
  (`:569`), which D3D11 silently no-ops for a `B8G8R8A8` source. Expect black
  frames: COLOR/ORIENTATION/MOTION FAIL with `meanLuma` near 0 on every sampled
  frame — a precisely diagnosed FAIL, not a harness bug.
- **`--format rgba`** (source matches the encode texture format): the copy
  works, so the **F2** ARGB-registration mismatch (`:399`) shows as an R/B
  swap — e.g. the RED bar measures as blue. White/black marker and the gray
  box are achromatic, so **the orientation evidence (gate G8) will most likely
  come from this rgba run**; MOTION is expected to PASS here too.

Full PASS or a precisely diagnosed FAIL both satisfy the NVIDIA gates; a crash,
hang, or inconclusive output does not.

## CLI

```
--width N           (default 1920)
--height N          (default 1080)
--fps N             (default 30)
--frames N          (default 90)
--format bgra|rgba  (default bgra)
--out FILE.mkv      (default out.mkv; selftest: reference.mkv)
--ffmpeg PATH       (default: ffmpeg.exe beside the exe)
--selftest          reference-clip mode, no NVENC
```

Exit codes: `0` = all checks PASS, `1` = verify FAIL, `2` = native encoder
failure, `3` = usage error.

## Verification checks

All on frames `0, N/2, N-1` extracted to BMP by the shipped `ffmpeg.exe`:

1. **RESOLUTION** — BMP headers and the `ffmpeg -i` stream line == expected.
2. **COLOR** — 7 bar centers sampled; per-channel tolerance ±48/255 (tolerates
   BT.601/709 matrix choice and 4:2:0 bleed; catches R/B swap, black, gray).
3. **ORIENTATION** — luminance probes at the top-left marker/notch; prints
   `ORIENTATION: CORRECT` or `ORIENTATION: FLIPPED` with measured values
   (`INDETERMINATE` if the frame carries no marker, e.g. black). No flip code
   exists anywhere in the harness: orientation is measured, never corrected.
4. **MOTION** — gray-box centroid X must strictly increase across the sampled
   frames (frame ordering).

Every FAIL prints measured evidence (per-frame mean luminance, sampled RGB vs
expected with named sample points) so the failure is self-diagnosing.

Run artifacts (`verify_tmp\frame_*.bmp`, ffmpeg logs, the `.mkv`) are left next
to the exe for inspection.
