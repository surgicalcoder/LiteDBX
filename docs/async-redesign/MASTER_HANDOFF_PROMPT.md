# Master Handoff Prompt for LiteDB Async-Only Redesign

Use this prompt when handing one redesign phase to another LLM.

It is designed to work with:

- `ASYNC_ONLY_REDESIGN_PLAN.md`
- `docs/async-redesign/README.md`
- one or more phase documents in `docs/async-redesign/`

---

## How To Use This File

1. Pick the target phase document, for example:
   - `docs/async-redesign/01-phase-1-contracts.md`
   - `docs/async-redesign/02-phase-2-transactions-and-locking.md`
2. Attach or paste the following documents into the new LLM session:
   - `ASYNC_ONLY_REDESIGN_PLAN.md`
   - `docs/async-redesign/README.md`
   - this file: `docs/async-redesign/MASTER_HANDOFF_PROMPT.md`
   - the target phase document
3. Optionally include any files already changed by previous phases.
4. Replace the placeholders in the prompt template below.

---

## Master Prompt Template

Copy and paste the following prompt into the next LLM session:

```text
You are continuing a large-scale async-only redesign of LiteDB.

Project context:
- Repository: LiteDB
- Redesign goal: async-only architecture, async by default, no synchronous public API
- Naming rule: do not use the `Async` suffix on public methods because async is the default and only execution model

You have been given the following design documents:
- `ASYNC_ONLY_REDESIGN_PLAN.md`
- `docs/async-redesign/README.md`
- `docs/async-redesign/MASTER_HANDOFF_PROMPT.md`
- `[PHASE_DOC]`

Your assignment is to execute only the work for `[PHASE_NAME]`, unless a minimal dependency adjustment is required to keep the solution coherent.

## Non-negotiable rules

1. Do not preserve synchronous public APIs.
2. Do not introduce public `*Async` suffixes.
3. Do not use `Task.Run` to wrap synchronous implementations as a substitute for real async behavior.
4. Do not reintroduce thread-affine transaction assumptions.
5. Do not leave blocking synchronization primitives in async runtime paths unless the phase document explicitly says a subsystem may be deferred and documented.
6. Prefer `ValueTask<T>`, `Task<T>`, `IAsyncEnumerable<T>`, and `IAsyncDisposable` where appropriate.
7. Keep changes scoped to the phase.
8. Preserve existing behavior and correctness unless the redesign intentionally changes the contract.
9. Validate every edited file and run targeted build/tests where practical.
10. If part of the phase cannot be completed cleanly, document exactly what is deferred and why.

## Working instructions

1. Read all attached design documents before making changes.
2. Inspect all in-scope files listed in the phase document.
3. Follow the architecture decisions in the phase doc exactly.
4. Implement the phase completely enough that the next phase can build on it.
5. Add concise documentation notes where the phase doc asks for them.
6. Do not perform unrelated cleanup or refactors.
7. After edits, validate the changed files and summarize:
   - what was changed
   - what was intentionally not changed
   - any risks or deferred items

## Deliverable expectations

Your output should include:
- the implemented code changes for the current phase
- any new abstraction/interface files required by the phase
- any requested markdown/design note updates
- a short completion summary aligned to the phase acceptance criteria

## Additional context for this run

Phase document: `[PHASE_DOC]`
Phase name: `[PHASE_NAME]`
Primary goal for this run: `[PRIMARY_GOAL]`
Known upstream completed phases: `[COMPLETED_PHASES]`
Known constraints or deferred items: `[SPECIAL_CONSTRAINTS]`

Now execute the phase.
```

---

## Suggested Placeholder Values

### For Phase 1

- `[PHASE_DOC]` → `docs/async-redesign/01-phase-1-contracts.md`
- `[PHASE_NAME]` → `Phase 1 — Define the Async-Only Contracts`
- `[PRIMARY_GOAL]` → `Redesign the public and engine-facing contracts so async is the only supported model.`
- `[COMPLETED_PHASES]` → `None`
- `[SPECIAL_CONSTRAINTS]` → `Focus on contracts and supporting abstractions only; do not attempt the full engine refactor in this run.`

### For Phase 2

- `[PHASE_DOC]` → `docs/async-redesign/02-phase-2-transactions-and-locking.md`
- `[PHASE_NAME]` → `Phase 2 — Redesign Transactions and Locking`
- `[PRIMARY_GOAL]` → `Replace per-thread transaction ownership and blocking synchronization with async-safe models.`
- `[COMPLETED_PHASES]` → `Phase 1`
- `[SPECIAL_CONSTRAINTS]` → `Do not rely on ThreadLocal, current thread id, ReaderWriterLockSlim, Monitor, or sync lock ownership assumptions.`

### For Phase 3

- `[PHASE_DOC]` → `docs/async-redesign/03-phase-3-disk-and-streams.md`
- `[PHASE_NAME]` → `Phase 3 — Make Disk and Stream Infrastructure Async-First`
- `[PRIMARY_GOAL]` → `Make file access, WAL persistence, page reads/writes, and stream wrappers genuinely asynchronous.`
- `[COMPLETED_PHASES]` → `Phase 1, Phase 2`
- `[SPECIAL_CONSTRAINTS]` → `Do not add async-looking methods that still rely on sync I/O internally.`

### For Phase 4

- `[PHASE_DOC]` → `docs/async-redesign/04-phase-4-query-pipeline.md`
- `[PHASE_NAME]` → `Phase 4 — Convert the Query Pipeline to Async Streaming`
- `[PRIMARY_GOAL]` → `Replace sync enumeration/cursor behavior with async query execution and result streaming.`
- `[COMPLETED_PHASES]` → `Phase 1, Phase 2, Phase 3`
- `[SPECIAL_CONSTRAINTS]` → `Prefer IAsyncEnumerable<T> unless a minimal async cursor abstraction is clearly justified.`

### For Phase 5

- `[PHASE_DOC]` → `docs/async-redesign/05-phase-5-file-storage.md`
- `[PHASE_NAME]` → `Phase 5 — Redesign File Storage as Async-Only`
- `[PRIMARY_GOAL]` → `Refactor LiteDB file storage so reads, writes, upload, download, and lifecycle are async-only.`
- `[COMPLETED_PHASES]` → `Phase 1, Phase 2, Phase 3, Phase 4`
- `[SPECIAL_CONSTRAINTS]` → `If zero sync API surface is required, remove public Stream inheritance and replace it with an async-only abstraction.`

### For Phase 6

- `[PHASE_DOC]` → `docs/async-redesign/06-phase-6-shared-mode-and-peripherals.md`
- `[PHASE_NAME]` → `Phase 6 — Redesign Shared Mode and Peripheral Subsystems`
- `[PRIMARY_GOAL]` → `Handle SharedEngine and remaining subsystem edges that still depend on synchronous assumptions.`
- `[COMPLETED_PHASES]` → `Phase 1, Phase 2, Phase 3, Phase 4, Phase 5`
- `[SPECIAL_CONSTRAINTS]` → `Treat SharedEngine as redesign-or-defer; do not leave a hidden sync fallback in place.`

### For Phase 7

- `[PHASE_DOC]` → `docs/async-redesign/07-phase-7-tests-and-consumers.md`
- `[PHASE_NAME]` → `Phase 7 — Update Tests, Tools, and Downstream Consumers`
- `[PRIMARY_GOAL]` → `Migrate the rest of the solution to the async-only API and validate end-to-end behavior.`
- `[COMPLETED_PHASES]` → `Phase 1, Phase 2, Phase 3, Phase 4, Phase 5, Phase 6`
- `[SPECIAL_CONSTRAINTS]` → `Do not add sync compatibility shims just to preserve existing test or tool behavior.`

---

## Short Version Prompt

If you want a smaller prompt, use this:

```text
You are continuing the LiteDB async-only redesign. Async is the default and only execution model. Do not preserve synchronous public APIs. Do not introduce public `Async` suffixes. Do not use `Task.Run` as fake async. Follow `ASYNC_ONLY_REDESIGN_PLAN.md`, `docs/async-redesign/README.md`, and `[PHASE_DOC]` exactly. Keep work scoped to `[PHASE_NAME]`, inspect all in-scope files listed in the phase doc, validate all edited files, and summarize changes, deferred items, and risks at the end.
```

---

## Recommended Attachment Set Per Handoff

Minimum:

- `ASYNC_ONLY_REDESIGN_PLAN.md`
- `docs/async-redesign/README.md`
- `docs/async-redesign/MASTER_HANDOFF_PROMPT.md`
- one phase document

Recommended if prior work already exists:

- all of the above
- any edited files from prior phases
- any design notes produced during those phases

---

## Final Reminder

If another LLM starts drifting, restate these three rules:

1. Async-only means no synchronous public API remains.
2. No public `Async` suffixes.
3. No fake async or hidden sync fallback paths.

