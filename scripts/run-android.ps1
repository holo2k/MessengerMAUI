[CmdletBinding()]
param(
    [string]$AvdName,
    [int]$BootTimeoutSeconds = 240
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'dev-common.ps1')
$tools = Get-MessengerAndroidTools -RepositoryRoot $repo
$adb = $tools.Adb

& $adb start-server | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Could not start adb.' }

function Get-ReadyDeviceSerial {
    $line = & $adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '^\S+\s+device$' } | Select-Object -First 1
    if ($line) { return ($line -split '\s+')[0] }
    return $null
}

$serial = Get-ReadyDeviceSerial
if (-not $serial) {
    $avds = @(& $tools.Emulator -list-avds | Where-Object { $_.Trim() })
    if ($AvdName) {
        if ($AvdName -notin $avds) { throw "AVD '$AvdName' was not found. Available: $($avds -join ', ')" }
        $selectedAvd = $AvdName
    } else {
        $selectedAvd = $avds | Select-Object -First 1
    }
    if (-not $selectedAvd) { throw 'No Android virtual device exists. Create an AVD in Visual Studio Android Device Manager.' }

    Write-Host "Starting Android emulator '$selectedAvd'..."
    Start-Process -FilePath $tools.Emulator -ArgumentList @('-avd', $selectedAvd, '-no-snapshot-save') | Out-Null
}

$deadline = [DateTime]::UtcNow.AddSeconds($BootTimeoutSeconds)
do {
    $serial = Get-ReadyDeviceSerial
    if ($serial) {
        $bootCompleted = (& $adb -s $serial shell getprop sys.boot_completed 2>$null | Out-String).Trim()
        if ($bootCompleted -eq '1') { break }
    }
    if ([DateTime]::UtcNow -ge $deadline) { throw "Android device did not finish booting within $BootTimeoutSeconds seconds." }
    Write-Host 'Waiting for Android device to boot...'
    Start-Sleep -Seconds 2
} while ($true)

Write-Host "Running Messenger.Maui on $serial..."
Push-Location $repo
try {
    dotnet build src/Messenger.Maui/Messenger.Maui.csproj -f net10.0-android -c Debug -t:Run "/p:AdbTarget=-s $serial"
    if ($LASTEXITCODE -ne 0) { throw "Android deployment failed with exit code $LASTEXITCODE." }
}
finally { Pop-Location }
