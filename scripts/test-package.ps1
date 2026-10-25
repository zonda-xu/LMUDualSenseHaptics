param([Parameter(Mandatory=$true)][string]$Archive)
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('LMU 解压 测试 ' + [guid]::NewGuid().ToString('N'))
$launcher = $null
$child = $null
try {
    Expand-Archive -LiteralPath $Archive -DestinationPath $testRoot
    $rootFiles = @(Get-ChildItem $testRoot -File)
    if ($rootFiles.Count -ne 1 -or $rootFiles[0].Name -ne 'LMUDualSenseHaptics.exe') { throw 'Only the launch EXE may be a root file.' }
    foreach ($file in @('bin/LMUDualSenseHaptics.exe','bin/LMUDualSenseHaptics.dll','bin/hostfxr.dll','bin/coreclr.dll','docs/LICENSE','docs/THIRD_PARTY_NOTICES.md')) {
        if (-not (Test-Path (Join-Path $testRoot $file))) { throw "Missing package file: $file" }
    }
    # Start the extracted root launcher from an unrelated working directory.
    $launcher = Start-Process -FilePath $rootFiles[0].FullName -WorkingDirectory $env:TEMP -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(40)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($launcher.HasExited) { throw "Launcher exited before the window opened: $($launcher.ExitCode)" }
        if (-not $child) {
            $info = Get-CimInstance Win32_Process -Filter "ParentProcessId = $($launcher.Id)" | Where-Object { $_.Name -eq 'LMUDualSenseHaptics.exe' } | Select-Object -First 1
            if ($info) { $child = Get-Process -Id $info.ProcessId }
        }
        if ($child) { $child.Refresh(); if ($child.MainWindowHandle -ne 0) { break } }
        Start-Sleep -Milliseconds 250
    }
    if (-not $child -or $child.MainWindowHandle -eq 0) { throw 'Extracted application did not open a WPF window.' }
    if ($child.Path -ne (Join-Path $testRoot 'bin/LMUDualSenseHaptics.exe')) { throw 'Launcher started the wrong application.' }
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($child.MainWindowHandle)
    $scope = [System.Windows.Automation.TreeScope]::Descendants
    foreach ($theme in @('浅色','深色')) {
        if ($theme -eq '深色') {
            $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, '深色外观')
            $button = $window.FindFirst($scope, $condition)
            if (-not $button) { throw 'Theme button not found.' }
            $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            Start-Sleep -Milliseconds 150
        }
        foreach ($name in @('编辑车辆扳机配置','刹车反馈模式')) {
            $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name)
            $combo = $window.FindFirst($scope, $condition)
            if (-not $combo) { throw "UI control not found: $name" }
            $pattern = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
            $pattern.Expand()
            Start-Sleep -Milliseconds 150
            $pattern.Collapse()
        }
    }
    if (-not $child.CloseMainWindow()) { throw 'Could not close application window.' }
    if (-not $launcher.WaitForExit(15000) -or $launcher.ExitCode -ne 0) { throw 'Launcher did not propagate clean application shutdown.' }
    Write-Host 'PACKAGE PASS: root-only EXE, bin dependencies, Unicode/spaces path, unrelated cwd, WPF startup, Material combos in light/dark themes, clean exit.'
} finally {
    if ($child -and -not $child.HasExited) { Stop-Process -Id $child.Id -Force }
    if ($launcher -and -not $launcher.HasExited) { Stop-Process -Id $launcher.Id -Force }
    if (Test-Path $testRoot) { Remove-Item $testRoot -Recurse -Force }
}
