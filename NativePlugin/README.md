# Native Plugin Build Instructions

## Prerequisites

1. **AMD AMF SDK**: 
   - Download from https://github.com/GPUOpen-LibrariesAndSDKs/AMF
   - Download AMF-headers-v1.5.0.tar.gz <---Source Code--->
   - Extract /amf/ folder to `NativePlugin/amf/`

2. **FFmpeg (LGPL or GPL)**:
   - Windows builds: https://github.com/BtbN/FFmpeg-Builds/releases
   - Download `ffmpeg-n5.1.6-2-g0e8b267a97-win64-gpl-shared-5.1`
   - Extract /ffmpeg/ folder to `NativePlugin/ffmpeg/`

3. **Visual Studio 2022**: Install "Desktop development with C++" workload

## Build

Run `build.bat` (Debug) or `build_release.bat` (Release).

Output goes to `NativePlugin/build/CinematicRecorderNative.dll`