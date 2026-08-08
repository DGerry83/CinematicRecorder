@echo off
setlocal EnableDelayedExpansion

REM ==============================================
REM NVENC Zero-Copy Test Harness Build Script
REM ==============================================
REM Compiles the real src\NvencEncoder.cpp plus the
REM harness sources into build\harness\NvencHarness.exe
REM and stages the shipped ffmpeg runtime next to it,
REM so build\harness\ is the xcopy unit for the NVIDIA PC.
REM Run from anywhere; script cds to its own directory.

set "SCRIPT_DIR=%~dp0"
pushd "%SCRIPT_DIR%"

if not exist build\harness mkdir build\harness
if not exist build\intermediate-harness mkdir build\intermediate-harness

REM MSVC environment: override with CR_VSVCVARS if your VS install differs.
if not defined CR_VSVCVARS set "CR_VSVCVARS=C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat"
call "%CR_VSVCVARS%"
if errorlevel 1 exit /b 1

REM Same cl flags / include dirs / libs as build_release.bat (minus /LD and the
REM AMF sources/includes, which the NVENC TU does not use).
cl ^
  /std:c++17 ^
  /EHsc ^
  /O2 ^
  /DNDEBUG ^
  /Iinclude ^
  /Invenc ^
  /Iffmpeg\include ^
  /Fobuild\intermediate-harness\ ^
  src\NvencEncoder.cpp ^
  harness\main.cpp ^
  harness\TestPattern.cpp ^
  harness\VideoVerifier.cpp ^
  harness\HarnessShim.cpp ^
  /link ^
  /LIBPATH:ffmpeg\lib ^
  avcodec.lib avformat.lib avutil.lib ^
  d3d11.lib dxgi.lib ole32.lib ^
  /OUT:build\harness\NvencHarness.exe

if errorlevel 1 (
    echo Harness build failed!
    popd
    exit /b 1
)

echo.
echo Staging ffmpeg runtime into build\harness ...
xcopy /Y /Q "%SCRIPT_DIR%..\GameData\CinematicRecorder\PluginData\FFMpeg\*" "%SCRIPT_DIR%build\harness\" >nul
if errorlevel 1 (
    echo ERROR: failed to stage ffmpeg runtime from GameData\CinematicRecorder\PluginData\FFMpeg
    popd
    exit /b 1
)

echo.
echo Harness build successful: build\harness\NvencHarness.exe
echo Staged runtime: ffmpeg.exe + 8 DLLs next to the exe.
echo build\harness\ is the xcopy unit for the NVIDIA PC.
popd
exit /b 0
