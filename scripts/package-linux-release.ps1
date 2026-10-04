[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Output = 'artifacts/deployment',
    [string]$ReleaseId
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repo 'artifacts'))
$outputRoot = if ([IO.Path]::IsPathRooted($Output)) {
    [IO.Path]::GetFullPath($Output)
} else {
    [IO.Path]::GetFullPath((Join-Path $repo $Output))
}
$allowedPrefix = $artifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $outputRoot.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Deployment output must stay inside $artifactsRoot"
}

if ([string]::IsNullOrWhiteSpace($ReleaseId)) {
    $ReleaseId = (git -C $repo rev-parse HEAD).Trim()
}
if ($ReleaseId -notmatch '^[0-9a-f]{7,40}$') { throw 'ReleaseId must be a Git SHA.' }

if (Test-Path -LiteralPath $outputRoot) {
    $verifiedOutput = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $outputRoot).Path)
    if (-not $verifiedOutput.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to remove output outside the artifacts directory.'
    }
    Remove-Item -LiteralPath $verifiedOutput -Recurse -Force
}
$stage = Join-Path $outputRoot 'stage'
$apiOutput = Join-Path $stage 'api'
New-Item -ItemType Directory -Path $apiOutput -Force | Out-Null

dotnet publish (Join-Path $repo 'src/Messenger.Api/Messenger.Api.csproj') `
    --configuration $Configuration --runtime linux-x64 --self-contained true `
    --output $apiOutput
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

$bundle = Join-Path $stage 'efbundle'
dotnet ef migrations bundle `
    --project (Join-Path $repo 'src/Messenger.Infrastructure/Messenger.Infrastructure.csproj') `
    --startup-project (Join-Path $repo 'src/Messenger.Infrastructure/Messenger.Infrastructure.csproj') `
    --configuration $Configuration --self-contained --target-runtime linux-x64 `
    --output $bundle --force
if ($LASTEXITCODE -ne 0) { throw 'EF Core migrations bundle failed.' }

$archive = Join-Path $outputRoot "messenger-$ReleaseId.tar.gz"
tar -czf $archive -C $stage .
if ($LASTEXITCODE -ne 0) { throw 'Release archive creation failed.' }
Write-Output $archive
