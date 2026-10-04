[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$ciPath = Join-Path $repo '.github/workflows/ci.yml'
$deployPath = Join-Path $repo '.github/workflows/deploy-development.yml'
$packagePath = Join-Path $repo 'scripts/package-linux-release.ps1'
foreach ($path in @($ciPath, $deployPath, $packagePath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing CI/CD file: $path" }
}

$ci = Get-Content -Raw $ciPath
foreach ($required in @('pull_request:', 'push:', 'test-all.ps1', 'actions/checkout@v4', 'actions/setup-dotnet@v4', 'contents: read', 'docker build --tag messenger-minio:RELEASE.2025-10-15T17-29-55Z')) {
    if (-not $ci.Contains($required, [StringComparison]::Ordinal)) { throw "CI workflow is missing: $required" }
}

$deploy = Get-Content -Raw $deployPath
foreach ($required in @('workflow_dispatch:', 'branches: [main]', 'concurrency:', 'test-all.ps1', 'package-linux-release.ps1', 'DEPLOY_SSH_KEY', 'DEPLOY_HOST_KEY', 'known_hosts', 'messenger-deploy', 'curl --fail', 'docker build --tag messenger-minio:RELEASE.2025-10-15T17-29-55Z')) {
    if (-not $deploy.Contains($required, [StringComparison]::Ordinal)) { throw "Deployment workflow is missing: $required" }
}
foreach ($forbidden in @('StrictHostKeyChecking=no', 'root@', 'password')) {
    if ($deploy.Contains($forbidden, [StringComparison]::OrdinalIgnoreCase)) { throw "Deployment workflow contains forbidden text: $forbidden" }
}

$packager = Get-Content -Raw $packagePath
foreach ($required in @('dotnet publish', '--self-contained true', 'linux-x64', 'migrations bundle', 'Messenger.Api', 'efbundle')) {
    if (-not $packager.Contains($required, [StringComparison]::OrdinalIgnoreCase)) { throw "Release packager is missing: $required" }
}
if ($packager -match '(Smtp__Password|POSTGRES_PASSWORD|MINIO_ROOT_PASSWORD)') { throw 'Release packager must not contain application secrets.' }

Write-Output 'CI/CD workflows validated.'
