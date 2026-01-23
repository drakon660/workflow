@echo off
setlocal enabledelayedexpansion

:: Get current branch name
for /f "tokens=*" %%b in ('git rev-parse --abbrev-ref HEAD 2^>nul') do set "branch=%%b"
if "%branch%"=="" (
    echo Error: Not a git repository or git not found
    exit /b 1
)

:: Get short commit hash
for /f "tokens=*" %%h in ('git rev-parse --short HEAD') do set "hash=%%h"

:: Create timestamp
for /f "tokens=2 delims==" %%a in ('wmic OS Get localdatetime /value') do set "dt=%%a"
set "timestamp=%dt:~0,4%-%dt:~4,2%-%dt:~6,2%_%dt:~8,2%%dt:~10,2%%dt:~12,2%"

:: Create output filename
set "outputFile=%~dp0%branch%_%timestamp%_%hash%.zip"

echo Creating snapshot...
echo   Branch: %branch%
echo   Commit: %hash%
echo   Output: %outputFile%

git archive --format=zip --output="%outputFile%" HEAD

if %errorlevel%==0 (
    echo Snapshot created successfully!
) else (
    echo Error creating snapshot
    exit /b 1
)
