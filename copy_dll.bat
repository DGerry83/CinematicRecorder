@echo off
REM ==============================================
REM DLL Copy Script - For Post-Build Events
REM ==============================================
REM Reads target folders from dll_copy_config.txt
REM Config file can contain multiple lines:
REM   TARGET_FOLDER=C:\Your\First\Path
REM   TARGET_FOLDER=C:\Your\Second\Path

setlocal EnableDelayedExpansion

REM Get the directory where this batch file is located
set "SCRIPT_DIR=%~dp0"
set "CONFIG_FILE=%SCRIPT_DIR%dll_copy_config.txt"

REM Check if config file exists
if not exist "%CONFIG_FILE%" (
    echo ERROR: Configuration file not found.
    echo.
    echo Please create: dll_copy_config.txt
    echo With one or more lines: TARGET_FOLDER=C:\Your\Path\Here
    echo.
    pause
    exit /b 1
)

REM Check if source file was provided
if "%~1"=="" (
    echo ERROR: No source DLL provided.
    echo Usage: %~nx0 "C:\Path\To\Your.dll"
    echo.
    pause
    exit /b 1
)

REM Extract filename from source path
for %%I in ("%~1") do set "FILENAME=%%~nxI"

set "FOLDER_COUNT=0"
set "COPY_ERRORS=0"

echo ==============================================
echo Copying %FILENAME%
echo From: %~1
echo ==============================================
echo.

REM ==============================================
REM Read all TARGET_FOLDER entries from config
REM ==============================================
for /f "usebackq tokens=1,* delims==" %%A in ("%CONFIG_FILE%") do (
    if /i "%%A"=="TARGET_FOLDER" (
        set /a "FOLDER_COUNT+=1"
        set "TARGET_FOLDER=%%B"
        
        echo [Copy !FOLDER_COUNT!] %%B
        
        REM Create target folder if it doesn't exist
        if not exist "%%B" (
            echo   Creating folder: %%B
            mkdir "%%B" 2>nul
            if errorlevel 1 (
                echo   ERROR: Could not create folder %%B
                set /a "COPY_ERRORS+=1"
            ) else (
                REM Copy the file
                copy /Y "%~1" "%%B\%FILENAME%" >nul 2>&1
                if errorlevel 1 (
                    echo   ERROR: Failed to copy to %%B
                    set /a "COPY_ERRORS+=1"
                ) else (
                    echo   OK: %%B\%FILENAME%
                )
            )
        ) else (
            REM Copy the file
            copy /Y "%~1" "%%B\%FILENAME%" >nul 2>&1
            if errorlevel 1 (
                echo   ERROR: Failed to copy to %%B
                set /a "COPY_ERRORS+=1"
            ) else (
                echo   OK: %%B\%FILENAME%
            )
        )
        echo.
    )
)

REM ==============================================
REM Summary
REM ==============================================
echo ==============================================
if %FOLDER_COUNT%==0 (
    echo ERROR: No TARGET_FOLDER entries found in config file.
    echo Please add one or more lines to %CONFIG_FILE%:
    echo   TARGET_FOLDER=C:\Your\Path\Here
    echo.
    pause
    exit /b 1
)

echo Copy complete: %FILENAME%
echo Folders processed: %FOLDER_COUNT%
if %COPY_ERRORS% GTR 0 (
    echo ERRORS: %COPY_ERRORS% copy operations failed
    echo.
    pause
    exit /b 1
) else (
    echo All copies successful
)
echo ==============================================

endlocal
