[CmdletBinding()]
param([switch]$SkipRestore)

$ErrorActionPreference = 'Stop'
function Assert-NativeSuccess([string]$operation) {
    if ($LASTEXITCODE -ne 0) { throw "$operation failed with exit code $LASTEXITCODE." }
}
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$asciiWorkspace = Join-Path $env:LOCALAPPDATA 'MessengerWorkspace'
if ($repo -match '[^\x00-\x7F]') {
    if (-not (Test-Path -LiteralPath $asciiWorkspace)) {
        New-Item -ItemType Junction -Path $asciiWorkspace -Target $repo | Out-Null
    }
    $work = (Resolve-Path $asciiWorkspace).Path
} else { $work = $repo }

Push-Location $work
try {
    if (-not $SkipRestore) { dotnet restore Messenger.slnx; Assert-NativeSuccess 'dotnet restore' }
    dotnet format analyzers Messenger.slnx --verify-no-changes --no-restore --severity error
    Assert-NativeSuccess 'dotnet format analyzers'

    docker compose up -d postgres minio
    Assert-NativeSuccess 'Docker Compose startup'
    $deadline = [DateTime]::UtcNow.AddMinutes(2)
    foreach ($service in @('postgres', 'minio')) {
        $containerId = (docker compose ps -q $service).Trim(); Assert-NativeSuccess "Docker Compose lookup for $service"
        if (-not $containerId) { throw "Compose service '$service' did not start." }
        do {
            $health = (docker inspect --format '{{.State.Health.Status}}' $containerId).Trim(); Assert-NativeSuccess "Docker health check for $service"
            if ($health -eq 'healthy') { break }
            if ([DateTime]::UtcNow -ge $deadline) { throw "Compose service '$service' is not healthy: $health" }
            Start-Sleep -Seconds 2
        } while ($true)
    }

    $envFile = Join-Path $work '.env'
    $values = @{}
    if (Test-Path -LiteralPath $envFile) {
        foreach ($line in Get-Content -LiteralPath $envFile) {
            if ($line -match '^\s*([^#][^=]*)=(.*)$') { $values[$matches[1].Trim()] = $matches[2].Trim() }
        }
    }
    $db = if ($values.POSTGRES_DB) { $values.POSTGRES_DB } else { 'messenger' }
    $user = if ($values.POSTGRES_USER) { $values.POSTGRES_USER } else { 'messenger' }
    $password = if ($values.POSTGRES_PASSWORD) { $values.POSTGRES_PASSWORD } else { 'messenger-local-only' }
    $port = if ($values.POSTGRES_PORT) { $values.POSTGRES_PORT } else { '5432' }
    $previousConnection = $env:ConnectionStrings__Messenger
    $env:ConnectionStrings__Messenger = "Host=localhost;Port=$port;Database=$db;Username=$user;Password=$password"
    try { dotnet ef database update --project src/Messenger.Infrastructure --startup-project src/Messenger.Infrastructure; Assert-NativeSuccess 'EF migration update' }
    finally { $env:ConnectionStrings__Messenger = $previousConnection }

    dotnet test Messenger.slnx --no-restore --verbosity minimal
    Assert-NativeSuccess 'dotnet test'
    dotnet build src/Messenger.Api/Messenger.Api.csproj -c Release --no-restore
    Assert-NativeSuccess 'API Release build'
    dotnet build src/Messenger.Maui/Messenger.Maui.csproj -f net10.0-android -c Debug --no-restore
    Assert-NativeSuccess 'Android Debug build'
    $debugManifest = Join-Path $work 'src/Messenger.Maui/obj/Debug/net10.0-android/android/AndroidManifest.xml'
    if (-not (Test-Path -LiteralPath $debugManifest)) { throw 'Merged Android Debug manifest was not produced.' }
    $debugManifestText = [IO.File]::ReadAllText($debugManifest)
    if ($debugManifestText -notmatch 'usesCleartextTraffic="true"' -or $debugManifestText -notmatch 'networkSecurityConfig="@xml/network_security_config"') {
        throw 'Android Debug transport policy does not permit the emulator host configured by network_security_config.'
    }
    dotnet build src/Messenger.Maui/Messenger.Maui.csproj -f net10.0-android -c Release --no-restore
    Assert-NativeSuccess 'Android Release build'
    $releaseManifest = Join-Path $work 'src/Messenger.Maui/obj/Release/net10.0-android/android/AndroidManifest.xml'
    if (-not (Test-Path -LiteralPath $releaseManifest)) { throw 'Merged Android Release manifest was not produced.' }
    $releaseManifestText = [IO.File]::ReadAllText($releaseManifest)
    if ($releaseManifestText -notmatch 'usesCleartextTraffic="false"' -or $releaseManifestText -match 'networkSecurityConfig') {
        throw 'Android Release transport policy permits or references development cleartext configuration.'
    }

    $adb = Get-Command adb -ErrorAction SilentlyContinue
    $adbPath = if ($adb) { $adb.Source } else { $null }
    if (-not $adbPath) {
        $localProps = Join-Path $work 'Directory.Build.local.props'
        if (Test-Path -LiteralPath $localProps) {
            $propsText = [IO.File]::ReadAllText($localProps)
            if ($propsText -match '<AndroidSdkDirectory>([^<]+)</AndroidSdkDirectory>') {
                $candidate = Join-Path $matches[1].Trim() 'platform-tools\adb.exe'
                if (Test-Path -LiteralPath $candidate) { $adbPath = $candidate }
            }
        }
    }
    $device = if ($adbPath) { (& $adbPath devices | Select-Object -Skip 1 | Where-Object { $_ -match "\tdevice$" } | Select-Object -First 1) } else { $null }
    if ($device) {
        $apk = Get-ChildItem (Join-Path $work 'src/Messenger.Maui/bin/Release/net10.0-android') -Filter '*-Signed.apk' -Recurse | Select-Object -First 1
        if (-not $apk) { throw 'Android Release APK was not produced.' }
        & $adbPath install -r $apk.FullName | Out-Host; Assert-NativeSuccess 'ADB install'
        & $adbPath shell monkey -p com.companyname.messenger.maui -c android.intent.category.LAUNCHER 1 | Out-Host; Assert-NativeSuccess 'Android launch smoke'
        Start-Sleep -Seconds 2
        $activities = & $adbPath shell dumpsys activity activities
        Assert-NativeSuccess 'Android activity inspection'
        if (-not ($activities -match 'com\.companyname\.messenger\.maui/.+MainActivity')) { throw 'Messenger MainActivity is not present after launch.' }
    } else {
        Write-Warning 'No running Android emulator/device was found; install/launch smoke remains unverified.'
    }

    $trackedEnv = git ls-files .env
    if ($trackedEnv) { throw '.env must never be tracked.' }
    $unsafeFiles = [System.Collections.Generic.List[string]]::new()
    foreach ($path in (git ls-files)) {
        $absolute = Join-Path $work $path
        if (-not (Test-Path -LiteralPath $absolute -PathType Leaf)) { continue }
        $text = [IO.File]::ReadAllText($absolute)
        if ($text -match '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----') { $unsafeFiles.Add($path); continue }
        foreach ($line in ($text -split "`r?`n")) {
            if ($line -match '^\s*Smtp__Password\s*=\s*(.+)$' -and $matches[1] -notmatch 'replace-with') { $unsafeFiles.Add($path); break }
        }
    }
    $historyText = git log -p --all -- .
    Assert-NativeSuccess 'Git history read for secret scan'
    foreach ($line in ($historyText -split "`r?`n")) {
        if ($line -match '^[+-]?\s*Smtp__Password\s*=\s*(.+)$' -and $matches[1] -notmatch 'replace-with') { $unsafeFiles.Add('git-history'); break }
        if ($line -match '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----') { $unsafeFiles.Add('git-history'); break }
    }
    foreach ($log in Get-ChildItem $work -Recurse -File -Filter '*.log' -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '\\(bin|obj|\.git)\\' }) {
        $logText = [IO.File]::ReadAllText($log.FullName)
        if ($logText -match '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----' -or $logText -match 'Smtp__Password\s*=\s*(?!replace-with)\S+') { $unsafeFiles.Add($log.FullName) }
    }
    if ($unsafeFiles.Count -gt 0) { throw "Potential tracked secret found in: $($unsafeFiles -join ', ')" }
    Write-Host 'Verification succeeded: formatting, migrations, tests, API/Android Debug+Release builds, Compose health, release transport policy, and secret scan.' -ForegroundColor Green
}
finally { Pop-Location }
