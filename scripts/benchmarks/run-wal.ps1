param(
    [ValidateSet('smoke', 'full')]
    [string]$Profile = 'smoke',

    [string]$Filter = '*Wal*',

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ExtraArgs
)

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$projectPath = Join-Path $repoRoot 'LiteDbX.Benchmarks\LiteDbX.Benchmarks.csproj'

$commandArgs = @(
    'run',
    '--project', $projectPath,
    '-c', 'Release',
    '--',
    '--profile', $Profile,
    '--filter', $Filter
)

if ($ExtraArgs)
{
    $commandArgs += $ExtraArgs
}

Write-Host "Running WAL benchmarks with profile '$Profile' and filter '$Filter'..." -ForegroundColor Cyan
Write-Host "Project: $projectPath" -ForegroundColor DarkGray

& dotnet @commandArgs
exit $LASTEXITCODE

