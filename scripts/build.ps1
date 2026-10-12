$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Install .NET 8 SDK for Windows, then run build.cmd again.'
}
[xml]$project = Get-Content 'src/LMUDualSenseHaptics/LMUDualSenseHaptics.csproj'
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid project version.' }
New-Item -ItemType Directory -Force 'artifacts/build-logs' | Out-Null
function Invoke-Dotnet {
    param([string]$LogName, [string[]]$Arguments)
    & dotnet @Arguments 2>&1 | Tee-Object -FilePath (Join-Path 'artifacts/build-logs' $LogName)
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed; see artifacts/build-logs/$LogName" }
}
Invoke-Dotnet -LogName '01-build.txt' -Arguments @('build','LMUDualSenseHaptics.sln','-c','Release','-p:EnableWindowsTargeting=true','-m:1')
Invoke-Dotnet -LogName '02-tests.txt' -Arguments @('run','--project','tests/LMUDualSenseHaptics.Tests','-c','Release','--no-build')
# Start with an empty staging folder to exclude stale assemblies and personal files.
if (Test-Path 'artifacts/win-x64') { Remove-Item 'artifacts/win-x64' -Recurse -Force }
Invoke-Dotnet -LogName '03-publish.txt' -Arguments @('publish','src/LMUDualSenseHaptics','-c','Release','-r','win-x64','--self-contained','true','-p:EnableWindowsTargeting=true','-m:1','-o','artifacts/win-x64')
Copy-Item README.md,LICENSE,THIRD_PARTY_NOTICES.md 'artifacts/win-x64' -Force
Copy-Item docs 'artifacts/win-x64/docs' -Recurse -Force
$archive = "artifacts/LMUDualSenseHaptics-win-x64-v$version.zip"
Compress-Archive -Path 'artifacts/win-x64/*' -DestinationPath $archive -Force
$checksum = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$checksum  $(Split-Path $archive -Leaf)" | Set-Content "$archive.sha256" -Encoding ASCII
Write-Host "Build and regression tests passed. Package: $archive"
Write-Host 'EXE: artifacts/win-x64/LMUDualSenseHaptics.exe'
