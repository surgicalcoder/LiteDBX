# WAL PipeWriter Pilot Architecture Sketch

## Status

Proposal / architecture sketch.

## Purpose

This document sketches a **bounded, WAL-only, code-free pilot design** for introducing `PipeWriter` into LiteDbX.

It is intentionally narrower than `00-wal-pipelines-recommendation.md`.

The goal here is not to justify pipelines in general. The goal is to describe what a *safe first experiment* could look like if LiteDbX chooses to test whether a `PipeWriter`-backed WAL append path improves:

- write-side contention;
- batching;
- backpressure;
- commit latency under concurrent writers.

---

## Scope

## In scope

This sketch is scoped to the WAL append path currently centered around:

- `LiteDbX/Engine/Disk/DiskService.cs`
- `LiteDbX/Engine/Services/TransactionService.cs`
- `LiteDbX/Engine/Services/WalIndexService.cs`

It specifically targets the work that is currently done by:

- `DiskService.WriteLogDisk(...)`
- `TransactionService.CommitAsync(...)`
- `TransactionService.ReturnNewPagesAsync(...)`

## Out of scope

This sketch does **not** propose replacing or redesigning:

- `DiskReader.ReadPageAsync(...)`;
- the page cache;
- the direct data-file checkpoint write path;
- encryption wrappers such as `AesStream` or `AesGcmStream`;
- the public database API.

---

## Design intent

The pilot should preserve the current engine model as much as possible while isolating the part that changes.

The intended effect is:

- producers build WAL page batches;
- one internal writer owns the WAL stream;
- batches are written in order;
- transactions receive completion only after the chosen acknowledgment point is reached;
- the rest of the engine continues to reason in terms of pages, transactions, and WAL positions.

This means the pilot should be treated as a **writer-coordination change**, not a broader storage rewrite.

---

## Current flow to preserve semantically

Today the key write-side sequence is:

1. `TransactionService` collects writable pages;
2. pages are converted to final buffer form;
3. `DiskService.WriteLogDisk(...)` assigns WAL positions and writes the pages;
4. the transaction resumes;
5. `WalIndexService.ConfirmTransactionAsync(...)` records the committed WAL positions.

The pilot should preserve the same core invariants:

- WAL order is stable;
- page positions are deterministic;
- confirmation happens only after the correct persistence point;
- header-page behavior remains correct;
- rollback/new-page-return flows remain valid;
- checkpoint never races with ambiguous in-flight WAL work.

---

## Proposed internal actors

## 1. WAL append caller

This is the existing transaction-side producer logic.

Its responsibilities should remain:

- prepare the logical page batch;
- identify commit vs non-commit behavior;
- collect the page-position map used by WAL indexing;
- await the append result before confirming the transaction.

The caller should **not** own the WAL stream directly in the pilot design.

## 2. WAL batch descriptor

The pipeline should operate on a transaction- or operation-scoped batch descriptor rather than arbitrary raw bytes.

A batch descriptor would conceptually contain:

- ordered page references or stable page payloads;
- the reserved WAL position span;
- transaction metadata needed for confirmation or diagnostics;
- a completion handle / acknowledgment channel;
- fault/cancellation state.

The purpose of this abstraction is to keep the unit of work page-oriented and explicit.

## 3. WAL writer coordinator

This is the component that accepts batches from producers and feeds them into the bounded write pipeline.

Its responsibilities would be:

- accept or reject new batches;
- apply backpressure when the system is saturated;
- ensure batches enter the writer in a well-defined order;
- expose queue depth / status for diagnostics;
- coordinate with shutdown and checkpoint behavior.

## 4. WAL stream owner / writer loop

This component should be the **only owner of the WAL writer stream**.

Its responsibilities would be:

- drain batches in order;
- write each page to the WAL stream;
- flush according to the chosen semantics;
- complete or fault acknowledgments;
- stop cleanly during shutdown/drain.

This is the main place where a `PipeWriter`-backed implementation would live.

---

## Key design principle: reserve positions before physical write

One of the most important pilot rules should be:

**WAL positions should be reserved explicitly before the batch is handed to the background writer.**

This matters because the current direct-write flow allows page positions to be assigned as pages are consumed during writing. That is workable in a synchronous caller-to-writer flow, but it becomes harder to reason about once a background writer is introduced.

The pilot should instead follow this conceptual sequence:

1. build ordered page batch;
2. reserve a contiguous WAL span;
3. stamp each page with its final WAL position;
4. build the page-position map for later WAL index confirmation;
5. hand the batch to the writer;
6. await completion;
7. call `WalIndexService.ConfirmTransactionAsync(...)`.

Benefits of this rule:

- the transaction side gets deterministic page positions immediately;
- the writer becomes a pure ordered sink;
- the acknowledgment boundary becomes much clearer;
- the code no longer depends on side effects during enumeration to communicate page positions.

---

## Recommended write lifecycle

A pipeline-backed WAL append pilot should use a lifecycle roughly like this.

## Step 1 — Prepare batch

The transaction flow builds the logical list of pages exactly as it does today:

- writable pages;
- transaction metadata applied;
- header clone when needed;
- rollback/new-page-return pages when relevant.

## Step 2 — Reserve WAL span

Before enqueueing, reserve enough contiguous WAL space for the full batch.

That reservation should define:

- the batch start position;
- each page's final logical WAL position;
- the final `_logLength` advancement associated with the batch.

## Step 3 — Submit batch to the coordinator

The transaction submits the batch to the bounded pipeline/coordinator.

Possible outcomes:

- accepted immediately;
- delayed by backpressure;
- rejected because shutdown/drain has started;
- faulted because the writer loop is no longer healthy.

## Step 4 — Writer drains and persists

The writer loop writes batch pages in order to the WAL stream.

The writer should remain the sole place that:

- touches the stream position;
- issues writes;
- issues flushes;
- translates stream failures into batch failure.

## Step 5 — Completion / acknowledgment

When the chosen persistence point is reached, the batch completion is signaled back to the caller.

Only after this should the transaction continue to:

- discard or transition page cache state as needed;
- call `WalIndexService.ConfirmTransactionAsync(...)`;
- mark the transaction committed.

---

## Acknowledgment model

The pilot must define a crisp acknowledgment contract.

At minimum, these internal states should be distinguished conceptually:

- **accepted** — the batch is now owned by the WAL subsystem;
- **queued** — waiting for the writer loop;
- **writing** — currently being persisted;
- **written** — bytes issued to the WAL stream;
- **flushed** — flush point reached, if flush is part of the contract;
- **completed** — caller may proceed;
- **faulted** — batch will never complete successfully.

## Recommended acknowledgment point

For a first pilot, the simplest safe approach is:

- caller completion occurs only after the same effective persistence point that the current `WriteLogDisk(...)` path provides.

That keeps semantics conservative and avoids introducing group-commit semantics too early.

A later optimization phase could explore whether acknowledgment should be widened or coalesced, but the pilot should begin with correctness-first behavior.

---

## Backpressure model

The pilot should use a **bounded** queue/pipe rather than an unbounded one.

Reasons:

- WAL bursts should not translate into unbounded memory growth;
- overload should become visible as queueing delay rather than silent buffer growth;
- queue depth becomes a useful metric;
- shutdown/drain logic remains more predictable.

Backpressure behavior should be explicit and documented:

- if the queue is full, producers wait asynchronously;
- if the engine is closing, producers are rejected/faulted clearly;
- if the writer faults, future append attempts fail fast.

---

## Checkpoint coordination

Checkpoint is one of the most important interaction points.

The pilot should define a simple rule set such as:

1. checkpoint must not begin against an ambiguous in-flight WAL state;
2. the WAL writer must expose whether batches are still queued or writing;
3. checkpoint entry may need to wait for a drain point;
4. once checkpoint drain begins, new append submissions may need to pause or be rejected depending on the lock/engine state.

The exact coordination mechanism can vary, but the principle should be:

**checkpoint observes a well-defined WAL boundary, never a half-drained one.**

---

## Shutdown and disposal coordination

A long-lived writer loop introduces lifecycle responsibilities that the current direct-call model does not have.

The pilot should define:

- how shutdown stops new submissions;
- whether shutdown waits for queued work to drain;
- what happens to waiting callers if drain fails;
- how writer faults are propagated during dispose / close;
- whether abnormal-close paths bypass the queue entirely.

The simplest safe rule is usually:

- stop accepting new batches;
- drain what is already accepted;
- propagate any fault to all waiting callers;
- only then release writer resources.

---

## Failure handling responsibilities

The architecture should make it obvious who owns each class of failure.

## Producer-side failures

Examples:

- cancellation before successful submission;
- inability to reserve space;
- batch construction failure.

These should fail before the WAL subsystem claims ownership.

## Writer-side failures

Examples:

- stream write exception;
- flush exception;
- encryption wrapper exception;
- writer-loop crash.

These should:

- fault the affected batch;
- mark the writer as unhealthy;
- fail subsequent submissions predictably;
- integrate with the engine's invalid-state / close behavior.

## Coordination failures

Examples:

- checkpoint attempts during a broken writer state;
- shutdown while work is still queued;
- drain timeout or forced teardown.

These must produce deterministic engine-level behavior rather than silent best-effort ambiguity.

---

## Non-goals for the pilot

To keep the experiment safe and measurable, the pilot should explicitly avoid trying to solve all storage concerns at once.

Non-goals should include:

- redesigning the entire disk layer around pipelines;
- replacing `Stream` as the base storage abstraction everywhere;
- removing page-based boundaries in WAL processing;
- changing checkpoint into a pipeline-first data-file writer;
- introducing new public commit semantics;
- solving every future group-commit optimization up front.

---

## Risks specific to this pilot shape

Even with narrow scope, the design still carries real risks:

- extra byte copying may offset gains;
- background coordination may increase debugging complexity;
- incorrect acknowledgment boundaries could break commit correctness;
- checkpoint integration can become subtle;
- shutdown/drain logic can become more complicated than expected;
- encryption-backed WALs may behave differently enough to reduce benefit.

This is why the pilot should remain narrowly scoped and measurement-driven.

---

## Suggested success metrics for the pilot

The architecture should be evaluated against metrics that match its purpose.

Recommended metrics:

- p95 / p99 commit latency under concurrent writers;
- time spent waiting to submit WAL work;
- queue depth and queue wait time;
- write batch size distribution;
- flush frequency / duration;
- throughput stability under write bursts;
- correctness under failure injection;
- behavior with plain, ECB, and GCM-backed stores.

---

## Open questions to settle before implementation

1. What exact completion point should unblock the transaction flow?
2. Should the pilot use a true `PipeWriter`, a bounded channel, or a hybrid abstraction under the same coordinator API?
3. How should queued-but-not-yet-written pages interact with cache state transitions?
4. Should checkpoint wait for a full drain or only for a logically committed boundary?
5. What metrics and diagnostics should be available from the writer coordinator?
6. Should rollback/new-page-return batches use the same pipeline path from day one?
7. How should the sync bridge (`WriteLogDiskSync`) behave if the async pilot exists concurrently?

---

## Recommended implementation stance

If LiteDbX proceeds, the most conservative and useful first experiment is:

- one internal WAL writer coordinator;
- explicit WAL position reservation;
- bounded append queue / pipeline;
- one stream-owning writer loop;
- acknowledgment semantics matched to current behavior;
- no changes to data-file I/O or random-access reads.

This gives the project the best chance of learning whether a `PipeWriter`-style WAL actually helps without committing to a large storage rewrite too early.

