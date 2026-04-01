# Pipeline Upgrade Notes

This folder contains design notes related to possible `System.IO.Pipelines` adoption in LiteDbX.

The current recommendation is intentionally conservative:

- start with the WAL only, if at all;
- prefer a `PipeWriter` pilot before introducing `PipeReader`;
- do not treat pipelines as a storage-wide rewrite by default;
- require measurable benefit before adopting the design permanently.

## Reading order

### 1. `00-wal-pipelines-recommendation.md`

Read this first.

This document answers the main product/architecture question:

- is `PipeReader` / `PipeWriter` a good fit for LiteDbX;
- why the WAL is the best candidate;
- what benefits might be realistic;
- what risks and complexity the change would introduce;
- what rollout and go/no-go criteria should be used.

### 2. `01-wal-pipewriter-pilot-architecture.md`

Read this second.

This document is a code-free architecture sketch for a **WAL writer pilot**. It narrows the discussion from the general recommendation to a concrete internal shape for a possible `PipeWriter`-based experiment.

It focuses on:

- component boundaries;
- batch handoff;
- position reservation;
- acknowledgment states;
- checkpoint and shutdown coordination;
- failure handling responsibilities.

## Scope of this folder

These notes are about **internal engine/storage design only**.

They are not a proposal to:

- replace the page cache with pipelines;
- replace random-access page reads with `PipeReader`;
- migrate data-file checkpoint writes to a pipe-first model;
- change the public API surface.

## Current working recommendation

At the time of writing, the working recommendation is:

1. keep the existing async stream-based storage model as the baseline;
2. evaluate pipelines only as a **targeted WAL append experiment**;
3. continue only if the pilot improves concurrency, batching, or operational backpressure in a measurable way.

