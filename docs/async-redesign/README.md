# LiteDB Async-Only Redesign — Phase Handoff Pack

This folder contains phase-by-phase handoff documents for the async-only LiteDB redesign.

These files are meant to be given to another LLM or engineer one phase at a time so work can be performed in manageable chunks.

## Finishing Roadmap

The phase documents in this folder describe the major redesign waves that have already been executed.
For the remaining endgame work, use `REMAINING_WORK_IMPLEMENTATION_PLAN.md` as the primary source of truth.
It consolidates lifecycle/open cleanup, startup/recovery bridge removal, system-collection async work,
shared/lock-file support-boundary clarification, and final consumer migration.

## Core Design Rules That Apply To Every Phase

These are **non-negotiable** unless a later architectural decision explicitly replaces them:

1. LiteDB is becoming **async-only**.
2. Public methods should **not** use the `Async` suffix.
3. No fake async (`Task.Run` around synchronous code is not acceptable).
4. No synchronous blocking in runtime paths.
5. No thread-affine transaction ownership.
6. Long-lived resources should move to `IAsyncDisposable` where cleanup can await.
7. Query execution should move toward `IAsyncEnumerable<T>` or a truly async cursor abstraction.
8. If zero sync API surface is required, `Stream`-based public file abstractions must be replaced.

## Recommended Execution Order

1. `01-phase-1-contracts.md`
2. `02-phase-2-transactions-and-locking.md`
3. `03-phase-3-disk-and-streams.md`
4. `04-phase-4-query-pipeline.md`
5. `05-phase-5-file-storage.md`
6. `06-phase-6-shared-mode-and-peripherals.md`
7. `07-phase-7-tests-and-consumers.md`

## Phase Dependency Summary

### Phase 1 — Contracts
Defines the target public and engine-facing async-only surface. This phase should happen first because every later phase needs stable interfaces and target abstractions.

### Phase 2 — Transactions and Locking
Removes the current per-thread transaction model and blocking lock ownership assumptions. This is the biggest architectural blocker for true async correctness.

### Phase 3 — Disk and Streams
Makes storage, WAL, file access, and stream infrastructure actually async. This phase underpins query execution, transactions, rebuild, and file storage.

### Phase 4 — Query Pipeline
Converts result streaming and query execution from sync enumeration to async flow.

### Phase 5 — File Storage
Builds the async-only storage/file API and removes public sync `Stream` assumptions if required.

### Phase 6 — Shared Mode and Peripherals
Handles hard edges like `SharedEngine`, rebuild/import/export/file-reader flows, and sync-dependent peripheral behavior.

### Phase 7 — Tests and Consumers
Updates tests, shell, stress tools, benchmarks, and sample projects to the new async-only contract.

## Suggested Working Style For Another LLM

When handing one of these phases to another model, provide:

- `docs/async-redesign/MASTER_HANDOFF_PROMPT.md`
- the relevant phase document from this folder
- `ASYNC_ONLY_REDESIGN_PLAN.md`
- any files already changed by prior phases
- a clear instruction to keep changes limited to the current phase unless dependencies require otherwise

## Suggested Prompt Wrapper

You can prepend this to any phase handoff:

> You are working on the LiteDB async-only redesign. Async is the default and only execution model. Do not introduce `Async` suffixes. Do not preserve synchronous public APIs. Do not wrap synchronous implementations in `Task.Run`. Follow the attached phase handoff document exactly, keep changes scoped to the phase, and validate all edited files.

## Files In This Folder

- `MASTER_HANDOFF_PROMPT.md`
- `01-phase-1-contracts.md`
- `02-phase-2-transactions-and-locking.md`
- `03-phase-3-disk-and-streams.md`
- `04-phase-4-query-pipeline.md`
- `05-phase-5-file-storage.md`
- `06-phase-6-shared-mode-and-peripherals.md`
- `07-phase-7-tests-and-consumers.md`

## Companion Documents

- `ASYNC_ONLY_REDESIGN_PLAN.md` — high-level architecture plan


