[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$envPath = Join-Path $repo '.env'
if (-not (Test-Path -LiteralPath $envPath)) { throw 'Create .env from .env.example and replace every placeholder first.' }
$values = @{}
foreach ($line in Get-Content -LiteralPath $envPath) {
    if ($line -match '^\s*([^#][^=]*)=(.*)$') {
        $name = $matches[1].Trim(); $value = $matches[2].Trim().Trim('"').Trim("'"); $values[$name] = $value
        if ($name -like '*__*') { Set-Item -Path "Env:$name" -Value $value }
    }
}
foreach ($required in @('POSTGRES_DB', 'POSTGRES_USER', 'POSTGRES_PASSWORD', 'MINIO_ROOT_USER', 'MINIO_ROOT_PASSWORD')) {
    if (-not $values[$required] -or $values[$required] -like 'replace-with-*') { throw "Missing safe value for $required in .env." }
}
$pgPort = if ($values.POSTGRES_PORT) { $values.POSTGRES_PORT } else { '5432' }
$minioPort = if ($values.MINIO_API_PORT) { $values.MINIO_API_PORT } else { '9000' }
$env:ConnectionStrings__Messenger = "Host=localhost;Port=$pgPort;Database=$($values.POSTGRES_DB);Username=$($values.POSTGRES_USER);Password=$($values.POSTGRES_PASSWORD)"
$env:Minio__Endpoint = "http://localhost:$minioPort"
$env:Minio__AccessKey = $values.MINIO_ROOT_USER
$env:Minio__SecretKey = $values.MINIO_ROOT_PASSWORD
$env:Minio__Bucket = 'messenger-private'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
Push-Location $repo
try { dotnet run --project src/Messenger.Api }
finally { Pop-Location }
