@echo off
setlocal

:: Create output directories
if not exist build mkdir build
if not exist build\intermediate mkdir build\intermediate

call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat"
if errorlevel 1 exit /b 1

cl ^
  /LD ^
  /std:c++17 ^
  /EHsc ^
  /Zi ^
  /Od ^
  /Iinclude ^
  /Iamf ^
  /Iamf\public\include ^
  /Iamf\public\common ^
  /Iffmpeg\include ^
  /Fobuild\intermediate\ ^
  src\CinematicRecorderNative.cpp ^
  amf\public\common\AMFFactory.cpp ^
  amf\public\common\Thread.cpp ^
  amf\public\common\Windows\ThreadWindows.cpp ^
  amf\public\common\AMFSTL.cpp ^
  amf\public\common\TraceAdapter.cpp ^
  /link ^
  /LIBPATH:ffmpeg\lib ^
  avcodec.lib avformat.lib avutil.lib ^
  d3d11.lib dxgi.lib ole32.lib ^
  /OUT:build\CinematicRecorderNative.dll ^
  /IMPLIB:build\intermediate\CinematicRecorderNative.lib ^
  /PDB:build\CinematicRecorderNative.pdb

if errorlevel 1 (
    echo Build failed!
    exit /b 1
)

:: Optional: Clean up temp files that persist after successful link
del build\intermediate\*.exp 2>nul

echo Build successful: build\CinematicRecorderNative.dll
echo Intermediate files in: build\intermediate\
endlocal