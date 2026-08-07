@echo off
setlocal EnableDelayedExpansion

REM ==============================================
REM Native Plugin Build Script
REM ==============================================
REM Builds the native DLL and copies to all 
REM TARGET_FOLDER entries in dll_copy_config.txt
REM (copies to PluginData subfolder of each)

set "SCRIPT_DIR=%~dp0"
set "CONFIG_FILE=%SCRIPT_DIR%..\dll_copy_config.txt"

REM Check if config file exists
if not exist "%CONFIG_FILE%" (
    echo ERROR: Configuration file not found.
    echo Please create: dll_copy_config.txt in project root
    echo With: TARGET_FOLDER=C:\Your\Path\Here
    exit /b 1
)

if not exist build mkdir build
if not exist build\intermediate mkdir build\intermediate

REM MSVC environment: override with CR_VSVCVARS if your VS install differs.
if not defined CR_VSVCVARS set "CR_VSVCVARS=C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat"
call "%CR_VSVCVARS%"
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
echo.

REM ==============================================
REM Copy to all TARGET_FOLDER entries in config
REM ==============================================
set "FOLDER_COUNT=0"
set "COPY_ERRORS=0"
set "FILENAME=CinematicRecorderNative.dll"

echo ==============================================
echo Deploying %FILENAME%
echo ==============================================
echo.

for /f "usebackq tokens=1,* delims==" %%A in ("%CONFIG_FILE%") do (
    if /i "%%A"=="TARGET_FOLDER" (
        set /a "FOLDER_COUNT+=1"
        REM Replace \Plugins with \PluginData for native DLL
        set "PLUGINS_PATH=%%B"
        set "PLUGINDATA_PATH=!PLUGINS_PATH:\Plugins=\PluginData!"
        
        echo [!FOLDER_COUNT!] !PLUGINDATA_PATH!
        
        if not exist "!PLUGINDATA_PATH!" (
            echo   Creating folder: !PLUGINDATA_PATH!
            mkdir "!PLUGINDATA_PATH!" 2>nul
            if errorlevel 1 (
                echo   ERROR: Could not create folder
                set /a "COPY_ERRORS+=1"
            ) else (
                copy /Y "build\%FILENAME%" "!PLUGINDATA_PATH!\" >nul 2>&1
                if errorlevel 1 (
                    echo   ERROR: Failed to copy
                    set /a "COPY_ERRORS+=1"
                ) else (
                    echo   OK: !PLUGINDATA_PATH!\%FILENAME%
                )
            )
        ) else (
            copy /Y "build\%FILENAME%" "!PLUGINDATA_PATH!\" >nul 2>&1
            if errorlevel 1 (
                echo   ERROR: Failed to copy
                set /a "COPY_ERRORS+=1"
            ) else (
                echo   OK: !PLUGINDATA_PATH!\%FILENAME%
            )
        )
        echo.
    )
)

echo ==============================================
if %FOLDER_COUNT%==0 (
    echo ERROR: No TARGET_FOLDER entries found in config
    exit /b 1
)

echo Deploy complete: %FILENAME%
echo Folders processed: %FOLDER_COUNT%
if %COPY_ERRORS% GTR 0 (
    echo ERRORS: %COPY_ERRORS% copy operations failed
    exit /b 1
) else (
    echo All deployments successful
)
echo ==============================================

endlocal
