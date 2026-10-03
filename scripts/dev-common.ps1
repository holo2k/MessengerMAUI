$ErrorActionPreference = 'Stop'

function Import-MessengerDevelopmentEnvironment {
    param([Parameter(Mandatory)][string]$RepositoryRoot)

    $envPath = Join-Path $RepositoryRoot '.env'
    if (-not (Test-Path -LiteralPath $envPath -PathType Leaf)) {
        throw 'Create .env from .env.example and replace every placeholder first.'
    }

    $values = @{}
    foreach ($line in Get-Content -LiteralPath $envPath) {
        if ($line -match '^\s*([^#][^=]*)=(.*)$') {
            $name = $matches[1].Trim()
            $value = $matches[2].Trim().Trim('"').Trim("'")
            $values[$name] = $value
            if ($name -like '*__*') { Set-Item -Path "Env:$name" -Value $value }
        }
    }

    foreach ($required in @('POSTGRES_DB', 'POSTGRES_USER', 'POSTGRES_PASSWORD', 'MINIO_ROOT_USER', 'MINIO_ROOT_PASSWORD')) {
        if (-not $values[$required] -or $values[$required] -like 'replace-with-*') {
            throw "Missing safe value for $required in .env."
        }
    }

    $pgPort = if ($values.POSTGRES_PORT) { $values.POSTGRES_PORT } else { '5432' }
    $minioPort = if ($values.MINIO_API_PORT) { $values.MINIO_API_PORT } else { '9000' }
    $env:ConnectionStrings__Messenger = "Host=localhost;Port=$pgPort;Database=$($values.POSTGRES_DB);Username=$($values.POSTGRES_USER);Password=$($values.POSTGRES_PASSWORD);Timeout=5;Command Timeout=30"
    $env:Minio__Endpoint = "http://localhost:$minioPort"
    $env:Minio__AccessKey = $values.MINIO_ROOT_USER
    $env:Minio__SecretKey = $values.MINIO_ROOT_PASSWORD
    $env:Minio__Bucket = 'messenger-private'
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    return $values
}

function Wait-MessengerComposeService {
    param(
        [Parameter(Mandatory)][string]$Service,
        [TimeSpan]$Timeout = [TimeSpan]::FromMinutes(2)
    )

    $containerId = (docker compose ps -q $Service).Trim()
    if ($LASTEXITCODE -ne 0 -or -not $containerId) { throw "Compose service '$Service' did not start." }
    $deadline = [DateTime]::UtcNow.Add($Timeout)
    do {
        $health = (docker inspect --format '{{.State.Health.Status}}' $containerId).Trim()
        if ($LASTEXITCODE -ne 0) { throw "Could not inspect Compose service '$Service'." }
        if ($health -eq 'healthy') { return }
        if ([DateTime]::UtcNow -ge $deadline) { throw "Compose service '$Service' is not healthy: $health" }
        Write-Host "Waiting for $Service ($health)..."
        Start-Sleep -Seconds 2
    } while ($true)
}

function Get-MessengerAndroidTools {
    param([Parameter(Mandatory)][string]$RepositoryRoot)

    $sdk = $env:ANDROID_SDK_ROOT
    if (-not $sdk) { $sdk = $env:ANDROID_HOME }
    $localProps = Join-Path $RepositoryRoot 'Directory.Build.local.props'
    if (-not $sdk -and (Test-Path -LiteralPath $localProps)) {
        $propsText = [IO.File]::ReadAllText($localProps)
        if ($propsText -match '<AndroidSdkDirectory>([^<]+)</AndroidSdkDirectory>') { $sdk = $matches[1].Trim() }
    }
    if (-not $sdk) { throw 'Android SDK was not found. Set ANDROID_SDK_ROOT or AndroidSdkDirectory in Directory.Build.local.props.' }

    $adb = Join-Path $sdk 'platform-tools\adb.exe'
    $emulator = Join-Path $sdk 'emulator\emulator.exe'
    if (-not (Test-Path -LiteralPath $adb -PathType Leaf)) { throw "adb was not found at $adb" }
    if (-not (Test-Path -LiteralPath $emulator -PathType Leaf)) { throw "Android emulator was not found at $emulator" }
    return @{ Sdk = $sdk; Adb = $adb; Emulator = $emulator }
}
