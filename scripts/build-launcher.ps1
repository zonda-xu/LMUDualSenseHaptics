$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (-not (Test-Path $vswhere)) { throw 'Install Visual Studio Build Tools with Desktop development with C++ (x64).' }
$installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $installation) { throw 'Visual Studio C++ x64 build tools are required to build the launcher.' }
$devcmd = Join-Path $installation 'Common7/Tools/VsDevCmd.bat'
New-Item -ItemType Directory -Force 'artifacts/launcher' | Out-Null
$commands = @"
@echo off
call "$devcmd" -arch=x64 -host_arch=x64
if errorlevel 1 exit /b 1
rc /nologo /fo artifacts\launcher\launcher.res src\Launcher\launcher.rc
if errorlevel 1 exit /b 1
cl /nologo /std:c++17 /utf-8 /W4 /WX /O2 /MT /EHsc /DUNICODE /D_UNICODE /Foartifacts\launcher\main.obj /Feartifacts\win-x64\LMUDualSenseHaptics.exe src\Launcher\main.cpp artifacts\launcher\launcher.res user32.lib /link /SUBSYSTEM:WINDOWS /MACHINE:X64 /MANIFEST:NO
exit /b %errorlevel%
"@
$commands | Set-Content 'artifacts/launcher/build.cmd' -Encoding ASCII
& cmd /d /c 'artifacts\launcher\build.cmd' 2>&1 | Tee-Object 'artifacts/build-logs/04-launcher.txt'
if ($LASTEXITCODE -ne 0) { throw 'Native launcher compilation failed.' }
