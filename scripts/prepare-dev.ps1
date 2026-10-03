[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'dev-common.ps1')

Push-Location $repo
try {
    $null = Import-MessengerDevelopmentEnvironment -RepositoryRoot $repo
    Write-Host 'Starting PostgreSQL and MinIO...'
    docker compose up -d postgres minio
    if ($LASTEXITCODE -ne 0) { throw "Docker Compose failed with exit code $LASTEXITCODE." }
    Wait-MessengerComposeService -Service postgres
    Wait-MessengerComposeService -Service minio

    Write-Host 'Applying database migrations with settings from .env...'
    dotnet ef database update --project src/Messenger.Infrastructure --startup-project src/Messenger.Infrastructure
    if ($LASTEXITCODE -ne 0) { throw "EF migration failed with exit code $LASTEXITCODE." }
    Write-Host 'Development dependencies and database are ready.' -ForegroundColor Green
}
finally { Pop-Location }
