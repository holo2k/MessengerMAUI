[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'dev-common.ps1')
$null = Import-MessengerDevelopmentEnvironment -RepositoryRoot $repo
Push-Location $repo
try { dotnet run --project src/Messenger.Api }
finally { Pop-Location }
