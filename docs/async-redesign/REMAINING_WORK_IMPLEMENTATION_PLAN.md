# LiteDbX Async Redesign — Remaining Work Implementation Plan

## Purpose

This document consolidates the remaining work needed to finish the async-redesign endgame in LiteDbX.

It is intended to replace scattered “deferred” notes across the phase decision docs with one practical finishing plan that another engineer or LLM session can execute.

---

## Executive Summary

A large portion of the redesign is already in place:

- async-only public CRUD/query/storage contracts
- explicit `ILiteTransaction` scopes
- async query execution and async reader flow
- provider-backed LINQ async terminals
- async file-storage handles
- optional AES-GCM support
- major `SharedEngine` redesign work

What remains is mostly **finish work** rather than greenfield redesign.

The biggest remaining areas are:

1. **Lifecycle/open cleanup**
   - constructors still hide startup/open work
   - `LiteDatabase` / `LiteEngine` still have sync bridge behavior in lifecycle-sensitive paths

2. **Startup / recovery bridge removal**
   - `Recovery`, `TryUpgrade`, rebuild startup flow, and WAL restore still rely on sync startup assumptions in some paths

3. **System collection async migration**
   - `SystemCollection.Input` has been migrated to an async shape
   - the query pipeline still buffers system sources once before entering the synchronous CPU pipeline

4. **Shared / lock-file support boundaries**
   - `SharedEngine` and `LockFileEngine` still have important limitations around explicit transactions and support posture

5. **Phase 7 consumer migration**
   - tests, shell, benchmarks, stress tools, and sample apps still need final migration/verification work

---

## Important Observation: the roadmap currently has a gap

Some decision docs still defer work to **“Phase 8”**, but the `docs/async-redesign/` folder only defines Phases 1–7.

That should be treated as a documentation inconsistency, not as a signal to invent a whole new architecture track.

### Recommendation

Do **not** create a broad new “Phase 8” redesign unless absolutely necessary.
Instead, treat the remaining work as a **finish plan** with the workstreams below.

---

## Remaining Workstreams

## Workstream 1 — Normalize the roadmap and status docs

### Goal
Make the documentation internally consistent so a follow-on implementation session has one coherent source of truth.

### Why this must happen first
Right now the repository contains:

- implemented phase docs
- deferred-item notes
- some references to nonexistent future phases

That creates avoidable ambiguity.

### Tasks

1. Update references in:
   - `docs/async-redesign/phase-3-decisions.md`
   - `docs/async-redesign/phase-6-decisions.md`
   - any other phase decision docs that defer work to “Phase 8”

2. Normalize those deferrals into:
   - **remaining lifecycle/open work**
   - **remaining system-collection work**
   - **remaining consumer migration work**

3. Add a short note in `docs/async-redesign/README.md` pointing readers to this file as the finishing roadmap.

### Acceptance criteria

- no decision doc still points at an undefined finishing phase
- the remaining work is categorized consistently
- future implementation sessions can start from this file without guessing what is actually left

---

## Workstream 2 — Finish lifecycle/open cleanup

### Goal
Stop relying on constructors as the hidden startup boundary for the primary supported async path.

### Why this matters
The biggest remaining mismatch with the async-only intent is that startup/open is still constructor-driven in important places.
That keeps some startup flows inherently synchronous.

### Primary files/symbols

- `LiteDbX/Engine/LiteEngine.cs`
- `LiteDbX/Client/Database/LiteDatabase.cs`
- `LiteDbX/Client/Structures/ConnectionString.cs`
- any related builders/factories introduced to support opening

### Tasks

1. Introduce a coherent async-open path for `LiteEngine`
   - likely `OpenAsync()` or a static/factory-based equivalent
   - ensure the official supported path is explicit and awaitable

2. Introduce a coherent async-open path for `LiteDatabase`
   - likely a static open/factory shape rather than relying only on `new LiteDatabase(...)`
   - preserve any temporary compatibility constructors only if clearly marked transitional

3. Rework connection-string creation flow
   - `ConnectionString.CreateEngine()` currently hides constructor/open behavior
   - it should align with the new async-open model

4. Revisit `LiteDatabase` configuration properties that currently bridge async engine calls synchronously:
   - `UserVersion`
   - `Timeout`
   - `UtcDate`
   - `LimitSize`
   - `CheckpointSize`
   - `Collation`

5. Decide whether those properties should:
   - remain as explicit transitional sync bridges and be clearly documented, or
   - be replaced by async-first access patterns only

6. Ensure shutdown semantics are centered on `DisposeAsync()` for supported paths
   - sync `Dispose()` may remain temporarily as a bridge, but should no longer be the assumed primary path

### Risks

- touches public creation patterns
- can ripple into tests, shell, and samples
- can create churn if consumer migration starts before this stabilizes

### Acceptance criteria

- `LiteEngine` has a real supported async-open lifecycle
- `LiteDatabase` has a real supported async-open lifecycle
- constructor-only startup is no longer the primary official model
- lifecycle-sensitive sync bridges are either removed or clearly isolated/documented as transitional

---

## Workstream 3 — Remove remaining startup/recovery sync bridges

### Goal
Move startup-sensitive recovery/rebuild/upgrade work onto the new async-open lifecycle.

### Why this matters
A lot of the remaining sync-over-async debt exists only because startup still happens inside constructors/open-time sync paths.

### Primary files/symbols

- `LiteDbX/Engine/Engine/Recovery.cs`
- `LiteDbX/Engine/Engine/Upgrade.cs`
- `LiteDbX/Engine/Engine/Rebuild.cs`
- `LiteDbX/Engine/Services/RebuildService.cs`
- WAL restore/index restore code paths
- startup initialization paths in `DiskService` and related services

### Tasks

1. Move `Recovery()` onto the async-open path
2. Move `TryUpgrade()` onto the async-open path
3. Remove remaining constructor/startup-only `GetAwaiter().GetResult()` patterns from:
   - rebuild startup flow
   - rebuild content startup bridge
   - recovery/upgrade startup bridge

4. Convert WAL restore/index restore to the supported async startup lifecycle
5. Review startup-only sync notes documented in Phase 3 and Phase 6 and close them one by one

### Specific items called out by the docs

- `WalIndexService.RestoreIndex` async version still needed
- startup initialization in disk/open flow is still partially sync-bound
- rebuild constructor-path bridges still exist

### Acceptance criteria

- recovery/upgrade/rebuild startup no longer depend on sync constructor-only orchestration in the primary path
- remaining sync startup bridges are eliminated or reduced to clearly documented non-primary compatibility paths
- the docs no longer need to describe startup/recovery as deferred because of constructor limitations

---

## Workstream 4 — Finish async system collections and re-enable `$query`

### Goal
Make the system-collection pipeline async-compatible end-to-end and restore `$query`.

### Why this matters
`$query` is one of the most visible explicit regressions still documented in the redesign.
Right now it is intentionally disabled because `SystemCollection.Input()` still returns `IEnumerable<BsonDocument>`.

### Primary files/symbols

- `LiteDbX/Engine/SystemCollections/SystemCollection.cs`
- `LiteDbX/Engine/SystemCollections/SysQuery.cs`
- any query pipeline code that consumes system-collection input
- `SqlParser.Execute(...)`
- `IBsonDataReader`

### Tasks

1. Redesign `SystemCollection.Input()` to an async shape
   - likely `IAsyncEnumerable<BsonDocument>`

2. Update all callers/consumers in the query pipeline to support async system-collection sources
3. Re-enable `$query` in `SysQuery`
4. Re-audit other system collections to ensure they still behave correctly under the final async input contract
5. Re-test ambient/current transaction access for system collections that rely on monitor-provided context

### Risks

- broader than it looks because it affects pipeline source contracts
- can touch core query-path assumptions

### Acceptance criteria

- `SystemCollection.Input` is async-compatible ✅
- `$query` no longer throws `NotSupportedException` ✅
- query pipeline supports system collection sources without sync bridges ✅

---

## Workstream 5 — Finalize `SharedEngine` and `LockFileEngine` support boundaries

### Goal
Make the support posture for shared access modes explicit and consistent before final consumer migration.

### Current state

#### `SharedEngine`
Already significantly redesigned:

- async-safe in-process lease model
- no old blocking named mutex in operational paths
- explicit transactions still unsupported
- cross-process coordination intentionally out of scope for this milestone

#### `LockFileEngine`
Already present as a distinct connection mode with a limited but explicit support shape:

- physical-file databases only
- cross-process write coordination via lock file
- explicit transactions unsupported

### Tasks

1. Decide the intended support statement for `SharedEngine`
   - in-process only?
   - explicit transactions permanently unsupported?
   - cross-process explicitly out of scope for the async milestone?

2. Decide the intended support statement for `LockFileEngine`
   - fully supported limited mode?
   - interim compatibility mode?
   - deferred from the async milestone?

3. Reflect those decisions in:
   - docs
   - XML comments
   - README/release notes/user guidance

4. If any remaining implementation changes are required to match the chosen support posture, do them before consumer migration

### Recommendation

Prefer a **clear support boundary** over indefinite ambiguity.
If cross-process semantics are not part of the near-term async milestone, document that explicitly.

### Completed support statement

- `SharedEngine`: supported async-safe <b>in-process only</b> serialized mode; no cross-process guarantee; no explicit transactions.
- `LockFileEngine`: supported <b>physical-file cross-process coordination</b> mode; no streams/`:memory:`/`:temp:`; no explicit transactions.

### Acceptance criteria

- `SharedEngine` and `LockFileEngine` have unambiguous documented guarantees ✅
- transaction limitations are clearly stated ✅
- no README/doc wording implies guarantees the runtime does not actually provide ✅

---

## Workstream 6 — Complete Phase 7 migration of tests, tools, and downstream consumers

### Goal
Finish the migration of all repository consumers to the async-only model and validate the redesign end to end.

### Primary projects

- `LiteDbX.Tests/`
- `LiteDbX.Shell/`
- `LiteDbX.Benchmarks/`
- `LiteDbX.Stress/`
- `ConsoleApp1/`

### Tasks

1. Update tests first
   - convert remaining sync usage
   - add focused coverage for async lifecycle, disposal, cancellation, transactions, and streaming

2. Update shell and sample apps
   - remove `.Wait()`, `.Result`, and hidden sync assumptions
   - demonstrate intended async usage patterns

3. Update benchmarks
   - avoid fake async benchmarking
   - ensure setup/teardown and execution reflect real async use

4. Update stress tooling
   - validate concurrency behavior
   - validate disposal and transaction boundaries under load

5. Run end-to-end verification and produce a short verification note

### Acceptance criteria

- downstream projects compile against the final async-only model
- no sync compatibility shim was added just to keep old consumers working
- tests cover the most important async correctness paths
- shell/sample/benchmark/stress tooling reflects intended usage

---

## Recommended Execution Order

1. **Roadmap normalization**
2. **Lifecycle/open cleanup**
3. **Startup/recovery bridge removal**
4. **System collections + `$query`**
5. **Shared/LockFile support finalization**
6. **Tests/tools/consumer migration**

### Why this order

- lifecycle/open cleanup is the keystone; it unlocks most startup bridge removal
- system-collection work should follow a stable runtime/lifecycle contract
- consumer migration should come last to avoid repeated churn

---

## Suggested Milestone Framing

If you want this split into implementation handoffs, the cleanest breakdown is:

### Milestone A — Runtime finish
Includes:

- Workstream 1
- Workstream 2
- Workstream 3
- Workstream 4
- Workstream 5

### Milestone B — Consumer finish
Includes:

- Workstream 6
- verification note
- final README/release-note polish

This split is useful because Milestone A stabilizes contracts and runtime behavior before broad consumer churn begins.

---

## Cross-Cutting Rules

Any follow-on implementation should preserve these rules:

1. Do **not** add sync compatibility shims just to make migration easier.
2. Do **not** wrap sync code in `Task.Run` and call that async.
3. Prefer explicit async lifecycle boundaries over hidden constructor work.
4. Keep public async method names suffix-free only where that is already a deliberate project rule.
5. Treat README/user-facing wording as part of the runtime contract.
6. Validate each edited phase/workstream before moving on.

---

## Definition of Done for the redesign endgame

The remaining async-redesign work should be considered complete when all of the following are true:

- the repository no longer depends on an undefined “Phase 8” to explain remaining work
- startup/open/recovery/rebuild primary paths are async-native
- constructor-only startup is no longer the primary supported lifecycle model
- `$query` is re-enabled under an async system-collection pipeline
- `SharedEngine` / `LockFileEngine` guarantees are explicit and accurate
- downstream tools/tests/samples are migrated and validated against the final async-only model

---

## Ready-to-paste handover message for another LLM session

You are continuing the LiteDbX async-redesign finish work.

Start with `docs/async-redesign/REMAINING_WORK_IMPLEMENTATION_PLAN.md` and use it as the primary roadmap.

Current state summary:
- async-only public CRUD/query/storage contracts are largely implemented
- explicit `ILiteTransaction` scopes are in place
- async query execution and async file-storage handles are implemented
- optional AES-GCM support is implemented
- `SharedEngine` has already been redesigned for async-safe in-process usage

Your remaining scope is the redesign endgame:
1. normalize any docs that still defer work to a nonexistent “Phase 8”
2. introduce/finalize a real async-open lifecycle for `LiteEngine` / `LiteDatabase`
3. remove startup/recovery/rebuild/WAL restore sync bridges from the primary supported path
4. migrate `SystemCollection.Input` to an async shape and re-enable `$query`
5. finalize the documented support boundaries of `SharedEngine` and `LockFileEngine`
6. only after runtime stabilization, migrate tests, shell, samples, benchmarks, and stress tools

Constraints:
- do not add sync compatibility shims just to preserve old consumers
- do not use fake async (`Task.Run` around sync implementations is not acceptable)
- keep changes scoped to the active workstream where possible
- validate edited files and run relevant tests after each workstream
- if a runtime support guarantee is not truly implemented, document the limitation explicitly rather than implying support

Recommended order:
- lifecycle/open first
- startup/recovery next
- system collections / `$query` next
- shared/lock-file support finalization next
- consumer migration last

Expected output from the session:
- code changes for the active workstream
- updated docs where required
- validation results
- a short summary of what is now complete vs still deferred

---

## Optional short handover variant

Continue the LiteDbX async-redesign finish work using `docs/async-redesign/REMAINING_WORK_IMPLEMENTATION_PLAN.md` as the source of truth. Focus first on async lifecycle/open, then remove startup/recovery sync bridges, then migrate `SystemCollection.Input` to async and re-enable `$query`, then finalize `SharedEngine`/`LockFileEngine` guarantees, and only then migrate tests/tools/consumers. Do not add sync shims or fake async.

