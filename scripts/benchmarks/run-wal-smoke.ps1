param(
    [string]$Filter = '*Wal*',

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ExtraArgs
)

$runner = Join-Path $PSScriptRoot 'run-wal.ps1'
& $runner -Profile smoke -Filter $Filter @ExtraArgs
exit $LASTEXITCODE

