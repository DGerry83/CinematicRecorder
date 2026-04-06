@echo off
setlocal

if not exist build mkdir build
if not exist build\intermediate mkdir build\intermediate

call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat"
if errorlevel 1 exit /b 1

cl ^
  /LD ^
  /std:c++17 ^
  /EHsc ^
  /O2 ^
  /DNDEBUG ^
  /Iinclude ^
  /Invenc ^
  /Iamf ^
  /Iamf\public\include ^
  /Iamf\public\common ^
  /Iffmpeg\include ^
  /Fobuild\intermediate\ ^
  src\CinematicRecorderNative.cpp ^
  src\NvencEncoder.cpp ^
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
  /IMPLIB:build\intermediate\CinematicRecorderNative.lib

if errorlevel 1 (
    echo Build failed!
    exit /b 1
)

echo Release build successful: build\CinematicRecorderNative.dll

REM Deploy to KSP test installation
set DEPLOY_PATH=C:\SSDGames\KSPReleaseTest\GameData\CinematicRecorder\PluginData
if not exist "%DEPLOY_PATH%" mkdir "%DEPLOY_PATH%"
copy /Y "build\CinematicRecorderNative.dll" "%DEPLOY_PATH%\"
if errorlevel 1 (
    echo Deploy failed!
    exit /b 1
)
echo Deployed to: %DEPLOY_PATH%

endlocal