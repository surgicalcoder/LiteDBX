# WAL Pipeline Upgrade Recommendation — PipeReader/PipeWriter Suitability, Risks, and Rollout

## Status

Proposal / design note.

## Summary

`System.IO.Pipelines` appears to be a **selective fit for the WAL path**, especially the append/write side, but **not a strong fit for the rest of the storage engine** as it currently exists.

The recommended direction is:

- consider a **WAL-only pilot** first;
- treat `PipeWriter` as more promising than `PipeReader` initially;
- keep the data-file and random page-read paths on the current stream + page-cache model;
- only continue beyond a pilot if there is a measurable concurrency or batching benefit.

This is **not** a recommendation to migrate the entire disk layer to pipelines.

---

## Context

LiteDbX already moved the main storage paths to an async-first stream model in Phase 3:

- `LiteDbX/Engine/Disk/DiskService.cs`
- `LiteDbX/Engine/Services/TransactionService.cs`
- `LiteDbX/Engine/Services/WalIndexService.cs`
- `LiteDbX/Engine/Disk/DiskReader.cs`
- `docs/ASYNC_ONLY_REDESIGN_PLAN.md`
- `docs/async-redesign/03-phase-3-disk-and-streams.md`
- `docs/async-redesign/phase-3-decisions.md`

The current WAL implementation already has several properties that make it the best candidate for any pipeline-style upgrade:

- append-oriented writes;
- single-writer serialization via `DiskService._writerGate`;
- fixed-size page framing;
- async file I/O already in place;
- sequential full-log reads for checkpoint and restore.

By contrast, the data-file and page-reader paths remain primarily:

- positional / seek-based;
- cache-driven;
- random-access rather than streaming;
- tied to page-specific encryption behavior.

---

## Current WAL flow

## Commit / rollback write path

Today, `TransactionService` produces logical pages and sends them to `DiskService.WriteLogDisk(...)`.

`DiskService.WriteLogDisk(...)` currently:

1. acquires `_writerGate`;
2. assigns WAL positions page-by-page;
3. marks pages as log-origin pages;
4. writes each page sequentially to the WAL stream;
5. flushes the stream at the end of the batch;
6. returns to the transaction flow so `WalIndexService.ConfirmTransactionAsync(...)` can update the in-memory WAL index.

This is already a **single-consumer append pipeline in spirit**, just implemented directly on top of `Stream.WriteAsync(...)` rather than through `PipeWriter`.

## WAL read / checkpoint path

`WalIndexService.CheckpointInternalAsync(...)` scans the WAL via `DiskService.ReadFullAsync(...)`, filters confirmed transactions, clones page bytes, and writes final page images to the data file through `DiskService.WriteDataDisk(...)`.

This path is sequential, but it is not the hottest operational path compared with transaction commit.

---

## Suitability assessment

## 1. WAL append path: good candidate

The WAL append path is the best fit for pipelines because it is:

- naturally sequential;
- already serialized through one writer gate;
- page framed;
- fed by multiple producers under concurrent write transactions;
- a place where batching and backpressure could have real value.

A `PipeWriter`-based design could provide:

- a bounded producer/consumer queue for WAL batches;
- a dedicated single writer that owns the WAL stream;
- better control over batching and flush behavior;
- a cleaner place to measure contention, queue depth, and latency.

## 2. WAL sequential scan path: possible, but secondary

A `PipeReader` could be useful for sequential WAL scans, especially if LiteDbX eventually wants a more explicit framed-reader model for:

- checkpoint;
- restore-index;
- future validation / repair tools.

However, the immediate benefit is smaller than on the write side because:

- checkpoint is not the primary contention point;
- the current `ReadFullAsync(...)` path is already sequential and async;
- downstream checkpoint logic still needs per-page inspection and cloning.

This makes `PipeReader` a possible later phase, not the best first move.

## 3. Data-file write path: poor candidate

`DiskService.WriteDataDisk(...)` writes pages directly to final positions in the data file.

That work is:

- positional;
- seek-based;
- checkpoint-specific;
- not append-oriented.

A generic `PipeWriter` does not naturally improve this shape. The engine would still have to:

- compute exact target positions;
- preserve page ordering and correctness;
- issue writes to those positions;
- preserve checkpoint durability semantics.

This is not where pipelines are likely to pay for their added complexity.

## 4. Random page reads: not a good fit

`DiskReader.ReadPageAsync(...)` is a page-cache-backed random-access reader. That is fundamentally a seek + exact-page-load model, not a sequential streaming model.

`PipeReader` is the wrong abstraction for this path.

---

## Expected improvements if WAL adopts pipelines

## 1. Better concurrency behavior under write pressure

The most credible gain is **reduced contention at the WAL writer bottleneck**.

Instead of every committing transaction waiting to enter the full write loop directly, a pipeline-backed WAL could:

- reserve or submit batches quickly;
- hand off the batch to a single long-lived writer;
- let the writer drain work in order.

This is most likely to improve:

- p95 / p99 commit latency under concurrent writes;
- fairness under bursts of small transactions;
- throughput stability rather than raw single-thread speed.

## 2. Better batching and group-commit opportunities

A single writer draining a bounded pipeline creates a natural place to:

- coalesce adjacent WAL work;
- reduce syscall frequency;
- reduce flush frequency where semantics allow;
- evolve toward a future group-commit strategy.

Even if LiteDbX does not implement group commit immediately, pipelines could make that option easier later.

## 3. Explicit backpressure

The current gate serializes writers, but it does not express buffering pressure very clearly.

A bounded pipe would allow the engine to make backpressure a first-class design choice:

- producers wait because the WAL queue is full;
- the writer catches up;
- memory growth stays bounded and visible.

## 4. Cleaner ownership of the WAL stream

Pipelines work best when one component clearly owns the output stream. That maps well to WAL append semantics.

This can simplify later additions such as:

- WAL writer metrics;
- queue length diagnostics;
- centralized flush policy;
- drain-on-checkpoint behavior;
- controlled shutdown of in-flight WAL work.

---

## What improvements should *not* be assumed

The proposal should **not** assume that pipelines will automatically deliver:

- dramatically faster raw disk throughput;
- lower CPU usage in all cases;
- benefits for random-access data I/O;
- benefits for page-cache reads;
- zero-copy behavior by default.

In particular, if a `PipeWriter` design copies bytes from each `PageBuffer` into pipe-owned buffers before writing them to the stream, it may add overhead rather than remove it.

---

## Main risks and issues

## 1. Commit acknowledgment becomes more complicated

Today the WAL write call is also the place where page positions are assigned and where the transaction waits for write completion.

If a background writer is introduced, LiteDbX must define precisely when a transaction may proceed to `WalIndexService.ConfirmTransactionAsync(...)`:

- after enqueue;
- after copy into the pipe;
- after actual stream write;
- after flush.

This needs a crisp contract.

## 2. Position assignment should become explicit

The current flow relies on `DiskService.WriteLogDisk(...)` assigning `PageBuffer.Position` as it consumes the enumerated pages.

A pipeline-backed design should avoid hidden consumer-side mutation as the mechanism that communicates WAL positions back to the transaction flow. The safer model is:

- reserve WAL positions explicitly before background writing begins;
- stamp the batch with final positions;
- let the writer act as an ordered sink.

That is a useful cleanup, but it is also a real refactor.

## 3. Extra copies may erase gains

If the implementation becomes:

`PageBuffer.Array -> Pipe buffer -> Stream`

then the pipeline may add memory bandwidth and allocation pressure rather than reducing them.

This is one of the biggest reasons to treat the change as an experiment rather than a guaranteed win.

## 4. Flush and durability semantics get harder to reason about

The existing async redesign notes already document platform/runtime caveats around flush durability.

A background writer adds more states to reason about:

- accepted;
- queued;
- written;
- flushed;
- confirmed.

These states must be documented clearly so correctness and recovery behavior remain obvious.

## 5. Failure handling is more complex

The design must handle:

- cancellation before enqueue;
- cancellation after enqueue but before write completion;
- partial batch failure;
- writer task failure;
- engine shutdown with queued but unwritten WAL data;
- checkpoint requests while the writer still has pending batches.

The current direct-call model is simpler.

## 6. Encryption wrappers constrain the design

LiteDbX encryption wrappers are page-oriented and position-sensitive:

- `AesStream` expects page-aligned logical writes;
- `AesGcmStream` stores each logical page as an encrypted record with page-specific layout and metadata.

Because of this, any pipeline upgrade should preserve **logical page framing** and should sit **above** the logical stream boundary, not try to replace page-aware encryption behavior with a raw byte-stream model.

## 7. Checkpoint coordination becomes a design concern

A pipeline-backed WAL writer means checkpoint/exclusive-lock code must define whether it needs to:

- stop accepting new WAL batches;
- drain the queue first;
- wait for all acknowledged writes;
- distinguish written vs merely enqueued work.

This is manageable, but it must be planned explicitly.

---

## Recommendation

## Recommended decision

Proceed only with a **WAL-only pilot**, focused first on the append/write side.

### Recommended scope

- **Yes**: `PipeWriter` pilot for WAL append / commit handoff.
- **Maybe later**: `PipeReader` for sequential WAL scans in checkpoint / restore.
- **No for now**: data-file writes, page cache reads, and general random-access disk I/O.

## Why this is the recommended boundary

This boundary captures the part of the storage engine that is most stream-like while avoiding a broad rewrite of paths that are still fundamentally random-access and page-cache driven.

---

## Proposed migration principles

If LiteDbX runs a WAL pipeline pilot, the design should follow these principles.

## 1. Keep the public/internal transaction semantics stable

`TransactionService` should not need to know whether WAL persistence is direct-stream or pipeline-backed.

Introduce an internal WAL append boundary that can be implemented either way.

## 2. Keep the unit of work page-oriented

Do not redesign the WAL as an arbitrary byte stream first.

The natural unit remains:

- page buffers;
- page batches;
- transaction-scoped WAL append batches.

This keeps the design aligned with:

- current `PageBuffer` usage;
- encryption assumptions;
- WAL index confirmation;
- checkpoint logic.

## 3. Make position reservation explicit

Before work is handed to a background writer, reserve a contiguous WAL span and stamp each page with its final logical WAL position.

That gives the transaction flow a deterministic position map before the writer performs physical I/O.

## 4. Use a single long-lived writer owner

One component should own the WAL writer stream and drain work in order.

That component becomes the only place responsible for:

- stream writes;
- flush policy;
- failure propagation;
- queue-drain on shutdown;
- checkpoint coordination hooks.

## 5. Bound memory and queue depth

Do not use an unbounded pipeline.

A bounded queue/pipe is preferable so the engine gets:

- predictable memory usage;
- explicit backpressure;
- measurable overload behavior.

## 6. Keep the pipeline above stream/encryption wrappers

The proposal should preserve the existing stream abstraction boundary as much as possible.

The pipeline should feed logical page batches into the WAL stream path, not replace page-aware wrappers such as `AesStream` or `AesGcmStream`.

---

## Proposed phased plan

## Phase 0 — Baseline and goals

Before building anything, measure the current WAL path under representative workloads.

Suggested measurements:

- commit throughput;
- p95 / p99 commit latency;
- average pages per transaction;
- time waiting on `_writerGate`;
- WAL flush time;
- checkpoint duration after sustained writes;
- behavior with plain, ECB, and GCM-backed stores.

Define success criteria up front.

## Phase 1 — Semantic design note

Write down the exact contract for a pipeline-backed WAL:

- when positions are assigned;
- when a batch is considered accepted;
- when commit may continue;
- when confirmation may happen;
- what checkpoint must wait for;
- how cancellation and faults propagate.

Do this before implementation.

## Phase 2 — WAL write-side pilot only

Prototype only the append/write path.

Do not change:

- random page reads;
- data-file direct writes;
- existing checkpoint data-file write logic;
- encryption wrappers.

This keeps the experiment narrow and testable.

## Phase 3 — Correctness testing

Focus on:

- single-page commits;
- multi-page commits;
- header-changing commits;
- rollback/new-page return flows;
- concurrent commit storms;
- checkpoint interaction;
- abnormal close / restart recovery;
- writer-loop fault injection;
- encrypted WAL cases.

## Phase 4 — Performance review / go-no-go gate

Keep the design only if the pilot demonstrates meaningful value, such as:

- lower high-percentile commit latency under concurrent writes;
- better throughput stability;
- clearer, bounded backpressure;
- acceptable complexity and failure handling.

If the improvement is marginal, retain the current direct async stream design.

## Phase 5 — Optional reader-side evaluation

Only if the writer-side pilot succeeds, evaluate whether sequential WAL readers should gain a pipeline-based scan layer.

This should remain scoped to:

- checkpoint;
- restore-index;
- diagnostic or maintenance scans.

It should not be treated as a reason to convert `DiskReader` or the data file path.

---

## Go / no-go criteria

## Continue with WAL pipelines if

- the main bottleneck is WAL writer contention rather than raw storage bandwidth;
- queueing and batching improve p95 / p99 commit latency;
- explicit backpressure is operationally useful;
- correctness remains easy enough to reason about;
- encryption compatibility remains straightforward.

## Stop at the current design if

- gains are negligible;
- the pipeline adds extra copies that erase benefits;
- failure and drain semantics become too complex;
- checkpoint integration becomes fragile;
- the operational benefit does not justify the new moving parts.

---

## Final recommendation

At the current architecture stage, `System.IO.Pipelines` is best viewed as a **targeted WAL optimization / coordination experiment**, not a new foundation for all storage I/O.

The recommended decision is therefore:

1. **Pilot `PipeWriter` on the WAL append path only**.
2. **Do not migrate data-file writes or random page reads to pipelines**.
3. **Treat `PipeReader` for WAL scans as optional and later**.
4. **Require measurable concurrency or batching gains before adopting the design permanently**.

That gives LiteDbX the part of pipelines most likely to help, while avoiding a broad storage abstraction change where the fit is weak.

