# CinematicRecorder

## Cinematic Recorder – Offline Video Capture for KSP

Instead of recording gameplay in real time, Cinematic Recorder **takes control of the simulation** and advances the game **one exact frame at a time** using a fixed, user-defined timestep.

This makes it possible to capture:
- Cinematic flybys with zero jitter  
- True slow motion (10×, 20×, or more)  
- High-quality footage at high resolutions and framerates  
- Smooth results even on modest hardware  

---

## Why This Isn’t Just OBS

OBS (and similar tools) record **what your PC can render in real time**. If your system can’t keep up, the video stutters, drops frames, or loses smooth motion — especially during slow motion or complex scenes.

Cinematic Recorder works fundamentally differently:

| OBS / Live Capture | Cinematic Recorder |
|--------------------|-------------------|
| Records in real time | Records offline |
| Drops frames under load | Never drops frames |
| Hardware limits smoothness | Hardware only affects capture *time*(not output quality) |
| Slow motion is choppy | Slow motion is perfectly smooth |
| Gameplay-driven | Frame-driven |

In short:  
**OBS records performance. Cinematic Recorder records intent.**

---

## What It Enables

Cinematic Recorder effectively turns KSP into an **offline renderer**:
- Physics, rendering, and encoding are locked to exact frame steps
- Playback speed and capture speed are completely decoupled
- Video quality is no longer tied to real-time framerate

If you can simulate it, you can record it — smoothly.

### Platform
- **Windows 10/11 only** (uses DirectX 11 interop)
- **KSP must run in D3D11 mode** (default). OpenGL and Vulkan are not supported.

### Hardware
- **Zero-Copy Mode (AMD)**: Requires an AMD GPU with Video Coding Engine (VCE) 2.0 or newer:
  - RX 400 series, RX 500 series, Vega, RX 5000/6000/7000 series
  - Ryzen APUs (2000 series and newer)
- **Zero-Copy Mode (NVIDIA)**: Requires an NVENC-capable NVIDIA GPU (verified on RTX 3050).
- **Fallback Mode**: Works on any GPU (AMD, NVIDIA, Intel) but significantly slower.



---

## Installation

Standard KSP mod installation:

1. Download the latest release from the [Releases](../../releases) page
2. Extract the zip and copy the contents of its `GameData/` folder into your `Kerbal Space Program/GameData/` directory
3. Ensure you have the following folder structure:
```
GameData/
├── CinematicRecorder/
│   ├── CinematicRecorder.version
│   ├── Readme.txt / License.txt
│   ├── Icons/
│   │   └── CinematicIcon.png
│   ├── Patches/
│   │   └── CinematicKerbPro.cfg
│   ├── Plugins/
│   │   ├── CinematicRecorder.dll
│   │   └── FFmpeg.AutoGen.dll
│   └── PluginData/
│       ├── CinematicRecorderNative.dll
│       └── FFmpeg/
│           ├── avcodec-59.dll
│           ├── avdevice-59.dll
│           ├── avfilter-8.dll
│           ├── avformat-59.dll
│           ├── avutil-57.dll
│           ├── ffmpeg.exe
│           ├── postproc-56.dll
│           ├── swresample-4.dll
│           └── swscale-6.dll
└── CameraTools/                # bundled fork — see "Bundled Camera Tools" below
    ├── Plugins/
    │   └── CameraTools.dll
    ├── NOTICE.txt / GPLv3.txt / License.txt / Changelog.txt
    ├── CameraTools.version
    ├── Localization/
    ├── Sounds/
    └── Textures/
```

### Dependencies
- **Harmony 2** (CKAN identifier `Harmony2`) — **required**, not bundled.
- **HullCamVDS** — optional; enables hull-camera integration.

**Note:** FFmpeg shared libraries (avcodec, avformat, avutil, swresample, swscale) are redistributed with this release in accordance with the LGPL/GPL licenses. Source code for FFmpeg is available from [ffmpeg.org](https://ffmpeg.org/download.html).

---

## Bundled Camera Tools (transitional)

This release bundles a modified fork of [Camera Tools Continued](https://github.com/BrettRyland/CameraTools) by **BrettRyland and contributors** (GPLv3). The fork, [CR_CameraTools](https://github.com/DGerry83/CR_CameraTools), adds a small integration API used by Cinematic Recorder and otherwise changes as little of the original code as possible. See `NOTICE.txt` and `GPLv3.txt` in the `GameData/CameraTools/` folder; complete corresponding source is at the fork link above.

**CameraTools support is transitional.** A built-in camera controller will replace CameraTools in a future release of Cinematic Recorder, and this bundled copy will be removed.

Camera paths are authored with the Camera Tools UI in flight, then assigned to recorder slots via Cinematic Recorder's Camera Panel.

---

## Important: UI Prototype Notice

**The current user interface is a prototype.** It provides basic functionality for testing the underlying capture systems, but it **will change significantly** in future updates. Expect breaking changes to the workflow, settings organization, and visual design.

The current UI is functional but minimal—it exists primarily to validate that the recording pipeline works.  Functionality here will change and improve now that the foundation is done.

---

## Known Issues & Bug Reports

**This is a pre-release — expect bugs.** Please check the list below before reporting, then file on [GitHub Issues](https://github.com/DGerry83/CinematicRecorder/issues) with your `KSP.log` and repro steps.

- **Stationary camera drift (CameraTools):** assigning a stationary camera to a Camera Panel slot in orbit, then returning to it, produces wildly drifting positions; afterwards pathing positions and CameraTools FOV control can misbehave. Avoid reusing orbital stationary slots.
- **No audio above 30 simulation FPS:** slow-motion captures are silent.
- **UI prototype:** layout, settings organization, and workflow will change.

**Capture-core bugs** (crashes, corrupted output, audio desync, encoder failures) are the highest priority, please report them. **CameraTools camera-behaviour bugs** (camera panel operations, pathing quirks) are also welcome but expect them to be fixed *by the replacement* rather than patched.

---

## How to Use

1. **Enter Flight Mode** – The recorder only works during active flight
2. **Click the Toolbar Icon** – Look for the film camera icon in the Application Launcher
3. **Configure Settings**:
   - **Capture FPS**: How many physics steps per second to simulate (higher = smoother slow-mo, longer recording time)
   - **Playback FPS**: Frame rate of the output video
   - **Duration**: Seconds of game time to capture
   - **Force Software Encoding**: Emergency fallback if hardware encoding fails (see below)
4. **Click Record** – The game will appear to slow down as it captures frames deterministically.
5. **Find Your Video** – Output files are saved to `GameData/CinematicRecorder/Videos/` named with timestamps.

---

## Technical Overview & Capture Modes

This mod uses zero-copy GPU encoding where possible, falling back to standard methods if your hardware doesn't support the current implementation.

### Mode 1: Zero-Copy Hardware (Preferred)
*AMD RX 400 series and newer (VCE 2.0+), or NVENC-capable NVIDIA GPUs*

- **What it does**: Frame data stays on the GPU from Unity → AMD AMF or NVIDIA NVENC encoder → disk. Never touches system RAM.
- **Performance**: Minimal overhead, uses GPU resources in a CPU-constrained environment
- **File Format**: HEVC (H.265) by default—approximately **75% smaller files** than H.264 (~230 MB/min vs ~970 MB/min at 4K/60fps).
- **Fallback**: If HEVC isn't available on your hardware, automatically falls back to H.264 hardware encoding.

### Mode 2: Standard Hardware/Software (Fallback)
*Older AMD cards, NVIDIA, Intel, or Software*

- **What it does**: Frames are read back to CPU memory, then sent to FFmpeg for encoding (NVENC, AMF, or x264).
- **Performance**: Significantly slower due to PCIe memory transfers and CPU overhead. Expect 5-10x longer capture times compared to real-time.
- **File Format**: H.264 (larger files, maximum compatibility).
- **Use "Force Software Encoding"** only if the primary mode causes crashes.

---

### Dependencies
- **AMD drivers**: Adrenalin 2020 Edition or newer recommended for HEVC stability.
- **NVIDIA drivers**: Any recent driver with NVENC support.
- **FFmpeg Libraries**: Included in the release (`PluginData/FFmpeg/`). These are LGPL/GPL licensed shared libraries required for the fallback encoding path.

---

## Building from Source

This repository contains the C# Kerbal Space Program plugin and the C++ native encoding plugin.

**Requirements:**
- Visual Studio 2022 (Desktop C++ workload)
- AMD AMF SDK (headers only, clone from [GPUOpen](https://github.com/GPUOpen-LibrariesAndSDKs/AMF) into `CinematicRecorderNative/amf/`)
- NVIDIA Video Codec SDK (`nvEncodeAPI.h` into `CinematicRecorderNative/nvenc/`)
- FFmpeg development libraries (Windows x64 shared build, into `CinematicRecorderNative/ffmpeg/`)
- .NET Framework 4.8

**Quick Build:**
```batch
cd CinematicRecorderNative
build_release.bat
cd ..
dotnet build CinematicRecorder.slnx
```

## Acknowledgements

- [@Halbann](https://github.com/Halbann) — repository reorganisation to standard KSP modding layout (KSPBuildTools-based build, SDK-style csproj, split C#/native projects) and build process improvements.

## License

Cinematic Recorder is licensed under the **GNU General Public License v3.0**.

This project redistributes FFmpeg binaries and links against FFmpeg at runtime.
As such, it is distributed under GPLv3 to ensure full compliance.

The bundled Camera Tools fork is likewise GPLv3 (original by BrettRyland and
contributors); see `GameData/CameraTools/NOTICE.txt` and `GPLv3.txt`.

You are free to use, modify, and redistribute this software under the terms
of the GPLv3. See the [LICENSE](LICENSE) file for full details.
