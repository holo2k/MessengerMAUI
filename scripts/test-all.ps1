[CmdletBinding()]
param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projects = Get-ChildItem -LiteralPath (Join-Path $repo 'tests') -Filter '*.csproj' -Recurse |
    Sort-Object FullName
foreach ($project in $projects) {
    dotnet test $project.FullName --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Tests failed: $($project.FullName)" }
}
