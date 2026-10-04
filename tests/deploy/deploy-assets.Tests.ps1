[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$required = @(
    'deploy/docker-compose.infrastructure.yml',
    'deploy/Caddyfile',
    'deploy/messenger-api.service',
    'deploy/messenger-deploy.sudoers',
    'deploy/bootstrap-ubuntu.sh',
    'deploy/deploy-release.sh',
    'deploy/messenger.env.example',
    'deploy/infrastructure.env.example'
)

foreach ($relative in $required) {
    $path = Join-Path $repo $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing deployment asset: $relative" }
}

$composePath = Join-Path $repo 'deploy/docker-compose.infrastructure.yml'
$composeEnvironment = Join-Path $repo 'deploy/infrastructure.env.example'
$composeJson = docker compose --env-file $composeEnvironment -f $composePath config --format json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'docker compose config failed.' }
foreach ($serviceName in @('postgres', 'minio')) {
    $service = $composeJson.services.$serviceName
    foreach ($port in @($service.ports)) {
        if ($port.host_ip -ne '127.0.0.1') { throw "$serviceName exposes a non-loopback port." }
    }
}

$caddy = Get-Content -Raw (Join-Path $repo 'deploy/Caddyfile')
foreach ($requiredText in @('api.projectdomain.ru', '185-56-162-174.sslip.io', '127.0.0.1:5192', '/messenger-private/*', '127.0.0.1:9000')) {
    if (-not $caddy.Contains($requiredText, [StringComparison]::Ordinal)) { throw "Caddyfile is missing: $requiredText" }
}

$service = Get-Content -Raw (Join-Path $repo 'deploy/messenger-api.service')
foreach ($requiredText in @('EnvironmentFile=/etc/messenger/messenger.env', 'ExecStart=/opt/messenger/current/api/Messenger.Api', '127.0.0.1:5192')) {
    if (-not $service.Contains($requiredText, [StringComparison]::Ordinal)) { throw "systemd service is missing: $requiredText" }
}

$deploy = Get-Content -Raw (Join-Path $repo 'deploy/deploy-release.sh')
foreach ($requiredText in @('^[0-9a-f]{7,40}$', 'readlink -f', 'systemctl restart messenger-api.service', 'ln -sfn', 'previous_release', 'curl --fail')) {
    if (-not $deploy.Contains($requiredText, [StringComparison]::Ordinal)) { throw "Deployment script is missing safety behavior: $requiredText" }
}

$example = Get-Content -Raw (Join-Path $repo 'deploy/messenger.env.example')
foreach ($pattern in @('Password=replace-with-postgres-password', 'Minio__SecretKey=replace-with', 'Jwt__SigningKey=replace-with')) {
    if (-not $example.Contains($pattern, [StringComparison]::Ordinal)) { throw "Environment template must contain placeholder: $pattern" }
}
$infrastructureExample = Get-Content -Raw (Join-Path $repo 'deploy/infrastructure.env.example')
foreach ($pattern in @('POSTGRES_PASSWORD=replace-with', 'MINIO_ROOT_PASSWORD=replace-with')) {
    if (-not $infrastructureExample.Contains($pattern, [StringComparison]::Ordinal)) { throw "Infrastructure template must contain placeholder: $pattern" }
}

Write-Output 'Deployment assets validated.'
