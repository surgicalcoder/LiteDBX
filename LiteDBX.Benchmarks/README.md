# LiteDbX Benchmarks

This project contains BenchmarkDotNet benchmarks for LiteDbX, including the WAL baseline suite added to support the proposed pipeline evaluation work.

## WAL benchmark profiles

The benchmark runner supports a custom profile switch:

- `--profile smoke` — faster, lower-confidence runs for local checks and pre-PR smoke testing
- `--profile full` — slower, steadier runs intended for baseline collection and comparison

The profile switch is consumed by `LiteDbX.Benchmarks/Program.cs` before the remaining arguments are passed through to BenchmarkDotNet.

## PowerShell runner scripts

Convenience scripts live in `scripts/benchmarks/`.

### Fast WAL smoke run

```powershell
powershell -ExecutionPolicy Bypass -File "D:\Work\LiteDBX\scripts\benchmarks\run-wal-smoke.ps1"
```

### Full WAL baseline run

```powershell
powershell -ExecutionPolicy Bypass -File "D:\Work\LiteDBX\scripts\benchmarks\run-wal-full.ps1"
```

### Run a specific WAL benchmark filter

```powershell
powershell -ExecutionPolicy Bypass -File "D:\Work\LiteDBX\scripts\benchmarks\run-wal-smoke.ps1" "*WalCommitBenchmark*"
powershell -ExecutionPolicy Bypass -File "D:\Work\LiteDBX\scripts\benchmarks\run-wal-full.ps1" "*WalCheckpointBenchmark*"
```

### Pass extra BenchmarkDotNet arguments through

```powershell
powershell -ExecutionPolicy Bypass -File "D:\Work\LiteDBX\scripts\benchmarks\run-wal-smoke.ps1" "*WalEncryptionVariantBenchmark*" -- --list flat
```

Or use the generic runner directly:

```powershell
powershell -ExecutionPolicy Bypass -File "D:\Work\LiteDBX\scripts\benchmarks\run-wal.ps1" -Profile smoke -Filter "*Wal*"
powershell -ExecutionPolicy Bypass -File "D:\Work\LiteDBX\scripts\benchmarks\run-wal.ps1" -Profile full -Filter "*WalConcurrentCommitBenchmark*"
```

## What the profiles do

### Smoke

The smoke profile keeps the same runtime/JIT configuration but uses fewer warmup and measured iterations so the run completes much faster.

Use it for:

- quick local checks
- pre-PR benchmark sanity runs
- verifying that a change clearly regressed or improved the WAL path

### Full

The full profile runs with more warmup and more measured iterations for steadier numbers.

Use it for:

- before/after comparisons
- collecting baseline numbers to attach to design notes or PRs
- validating whether a change is worth pursuing

## Current artifact location

BenchmarkDotNet reports are written under:

- `BenchmarkDotNet.Artifacts/results/`

Typical exported files include:

- `*-report-github.md`
- `*-report.csv`
- `*-report.html`

## WAL suite contents

The WAL benchmark suite currently includes:

- `WalCommitBenchmark`
- `WalConcurrentCommitBenchmark`
- `WalCheckpointBenchmark`
- `WalEncryptionVariantBenchmark`

