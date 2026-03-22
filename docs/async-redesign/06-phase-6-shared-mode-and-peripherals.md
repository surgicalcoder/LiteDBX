# Phase 6 — Redesign Shared Mode and Peripheral Subsystems

## Objective

Handle the subsystems that are hardest to fit into the async-only architecture after the core contracts, transactions, storage, queries, and file storage have been redesigned.

This phase focuses on shared mode and peripheral features that still depend on synchronous assumptions.

## Why This Phase Exists

Even if the main engine path becomes async-only, the solution will still be incomplete if edge subsystems remain sync-bound. `SharedEngine`, rebuild/import/export flows, file readers, and system-level helpers are likely to be the last major blockers.

## Prerequisites

Read:

- `ASYNC_ONLY_REDESIGN_PLAN.md`
- `docs/async-redesign/README.md`
- all prior phase docs

Inspect current subsystem code including:

- `LiteDB/Client/Shared/SharedEngine.cs`
- `LiteDB/Client/Shared/SharedDataReader.cs`
- `LiteDB/Engine/FileReader/**`
- `LiteDB/Engine/Engine/Rebuild.cs`
- `LiteDB/Engine/Engine/Recovery.cs`
- `LiteDB/Engine/Engine/Upgrade.cs`
- `LiteDB/Engine/SystemCollections/**`

## Non-Negotiable Architecture Decisions

1. Shared mode must not rely on blocking named mutex semantics in async operational flow.
2. Peripheral subsystems must align with the async-only contracts from earlier phases.
3. If a subsystem cannot be made correct in this phase, explicitly defer or disable it rather than leaving a partial sync path.
4. System-level tools and readers should not force synchronous result flow back into the core engine.

## In Scope

- `SharedEngine` redesign or explicit deferral plan
- shared reader wrappers as needed
- file-reader/rebuild/import/upgrade async alignment
- system collection execution touchpoints still assuming sync flow
- sync-dependent utility paths that remain blockers to full async architecture

## Out of Scope

- mass migration of tests and external consumers beyond what is needed for compile/build correctness
- unrelated performance tuning not required for correctness

## Files To Inspect

### Primary files

- `LiteDB/Client/Shared/SharedEngine.cs`
- `LiteDB/Client/Shared/SharedDataReader.cs`
- `LiteDB/Engine/FileReader/IFileReader.cs`
- `LiteDB/Engine/FileReader/FileReaderV8.cs`
- `LiteDB/Engine/FileReader/FileReaderV7.cs`
- `LiteDB/Engine/Engine/Rebuild.cs`
- `LiteDB/Engine/Engine/Recovery.cs`
- `LiteDB/Engine/Engine/Upgrade.cs`

### Supporting files

- `LiteDB/Engine/SystemCollections/**`
- `LiteDB/Engine/LiteEngine.cs`
- `LiteDB/Client/Database/LiteDatabase.cs`
- `LiteDB/Utils/FileHelper.cs`
- `LiteDB/Utils/Extensions/IOExceptionExtensions.cs`

## Current Problem Summary

The biggest unresolved subsystem is `SharedEngine`, which currently relies on:

- named `Mutex`
- `WaitOne()`
- sync open-per-call/close-per-call semantics

Other peripheral areas may already contain partial async work, but they are not necessarily aligned with the new architecture.

## Detailed Work Items

### 1. Decide the fate of `SharedEngine`

Choose one clearly and document it:

#### Option A — redesign shared mode now
Possible directions:

- async-compatible file-lock coordination
- coordinator/broker model
- serialized access service
- another cross-process strategy not based on blocking named mutex waits

#### Option B — explicitly defer shared mode
If deferred, do not leave a half-working sync fallback hidden inside the async architecture. Mark it clearly and contain the impact.

### 2. Refactor `SharedDataReader` and shared wrappers

If shared mode survives, its reader/wrapper behavior must align with the async query/result model and async disposal semantics.

### 3. Reconcile file-reader/rebuild/import/upgrade flows

Audit these paths for sync assumptions and align them with the async-only engine/storage/query model.

Important examples:

- rebuild flows
- recovery paths
- upgrade/import paths
- file-reader enumeration patterns

### 4. Review `IFileReader` implementations

There is already partial async behavior here. Confirm the contract fits the new async-only architecture and remove any remaining sync-only bottlenecks.

### 5. Review system collection interactions

System collections may still assume sync query or sync reader behavior. Ensure they align with the query model established in Phase 4.

### 6. Review utility methods that block

Audit helper code for sync waiting patterns such as:

- `.Wait()`
- `.Result`
- retry loops using blocking waits
- sync file operations in operational paths

Replace or isolate them appropriately.

### 7. Decide what is temporarily unsupported

If some peripheral features cannot be made correct in this phase, explicitly document and gate them rather than leaving misleading partial behavior.

### 8. Produce a subsystem status note

At the end of this phase, write a short markdown note describing:

- what was redesigned
- what was deferred
- what remains intentionally unsupported for the async-only milestone

## Preferred Design Direction

- Avoid blocking OS mutex wait patterns in async runtime paths
- Prefer explicit redesign over compatibility hacks
- Defer a subsystem explicitly if correctness would otherwise be compromised
- Keep the core engine architecture clean rather than preserving a sync edge case

## Deliverables

1. Shared mode redesign or documented deferral plan
2. Async-aligned peripheral subsystem changes for scoped files
3. Cleanup of remaining obvious sync-blocking helpers in scoped paths
4. Subsystem status note describing redesigned vs deferred features

## Acceptance Criteria

- No hidden sync-only shared mode path remains in the async core architecture
- Rebuild/file-reader/peripheral flows in scope align with the async-only model
- Remaining unsupported/deferred areas are explicitly documented
- No blocking waits remain in scoped operational paths unless clearly isolated and justified

## Risks and Traps

1. `SharedEngine` may consume disproportionate effort; be willing to defer it cleanly if necessary.
2. Peripheral flows can quietly reintroduce sync readers or sync file operations.
3. Compatibility shortcuts here can undermine the cleanliness of the earlier redesign.
4. System collections may have unusual edge cases because they are not always backed by normal collection data flows.

## Suggested Execution Order

1. Decide redesign vs deferral for `SharedEngine`
2. Reconcile shared wrappers/readers
3. Audit rebuild/recovery/upgrade/file-reader flows
4. Align system collection/query integration
5. Remove scoped sync blocking helpers
6. Write subsystem status note

## Validation

- Verify scoped subsystems compile against the new async-only contracts
- Verify no blocking-wait patterns remain in redesigned operational paths
- Verify deferred areas are explicitly documented rather than silently broken
- Run focused rebuild/file-reader tests if available

## Copy/Paste Handoff Prompt

> You are working on Phase 6 of the LiteDB async-only redesign. Your task is to handle shared mode and the remaining peripheral subsystems that still depend on synchronous assumptions. Treat `SharedEngine` as a redesign-or-defer decision; do not leave a hidden sync fallback inside the async-only architecture. Align file-reader, rebuild, recovery, upgrade, and system-collection-related flows with the async-only contracts established in earlier phases, and document any intentionally deferred areas clearly.

