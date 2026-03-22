# Phase 7 — Update Tests, Tools, and Downstream Consumers

## Objective

Migrate the rest of the solution to the async-only LiteDB contract and validate that the redesign works end to end.

This phase focuses on consumers, tooling, tests, and verification rather than core architecture.

## Why This Phase Exists

After the core redesign phases, the repository will still contain tests, examples, shell tools, benchmarks, and stress harnesses written against the old sync API. Those consumers must be updated before the redesign can be considered usable or trustworthy.

## Prerequisites

Read:

- `ASYNC_ONLY_REDESIGN_PLAN.md`
- `docs/async-redesign/README.md`
- all previous phase docs

Inspect downstream projects:

- `LiteDB.Tests/`
- `LiteDB.Shell/`
- `LiteDB.Benchmarks/`
- `LiteDB.Stress/`
- `ConsoleApp1/`

## Non-Negotiable Architecture Decisions

1. Consumers must use the async-only API; do not add sync compatibility layers just to make tests or tools easier.
2. Tests should validate async behavior, disposal, streaming, and cancellation where appropriate.
3. Benchmarks and stress tools should reflect realistic async usage rather than sync emulation.
4. Shell/tooling should not force sync blocking back into the solution.

## In Scope

- update tests to async-only APIs
- add/adjust tests for async lifecycle, streaming, disposal, and transaction semantics
- update shell and sample code
- update benchmarks and stress tools
- fix project-level compile breaks caused by the redesign
- validate end-to-end behavior

## Out of Scope

- large new feature work unrelated to the async migration
- unrelated refactoring for style/cleanup without migration value

## Files and Projects To Inspect

### Test project

- `LiteDB.Tests/**`

### Shell and tooling

- `LiteDB.Shell/**`
- `ConsoleApp1/**`

### Performance and stress

- `LiteDB.Benchmarks/**`
- `LiteDB.Stress/**`

### Supporting docs/project files if needed

- solution/project files affected by signature changes
- any docs/readmes that are clearly outdated after the redesign

## Current Problem Summary

All downstream code currently assumes a synchronous API shape. Typical patterns include:

- direct sync CRUD calls
- sync `Commit()` and transaction usage
- sync `ToList()`, `First()`, `Single()`
- sync storage/file operations
- sync disposal patterns

These must be updated to the async-only model.

## Detailed Work Items

### 1. Update test code to async-only usage

Refactor tests to use:

- async setup/teardown where needed
- `await using` for async-disposable resources
- async materialization and result streaming
- new transaction scope patterns

### 2. Add async-focused correctness tests

Ensure there are explicit tests for:

- async transaction commit and rollback
- async query streaming
- early disposal of query enumerators/readers
- async file upload/download/read/write
- async database open/dispose
- error handling during async operations

### 3. Update shell commands and interactive tooling

Refactor shell command paths to the new async-only APIs.

Decide how command dispatch and console interaction should handle async flows cleanly.

### 4. Update examples and sample app code

Consumers like `ConsoleApp1` should demonstrate the intended async usage patterns.

### 5. Update benchmarks

Benchmarks should reflect:

- realistic async usage
- awaited operations
- correct async setup/teardown

Avoid benchmarking fake async wrappers if the core redesign already removed them.

### 6. Update stress tools

Stress tools are especially important because this redesign changes concurrency, transactions, locks, and async I/O behavior.

Ensure they exercise:

- concurrent async queries
- concurrent async writes
- transaction overlap scenarios
- disposal under load

### 7. Fix compile/build/documentation fallout

As the solution transitions, project files or docs may need light updates to compile and explain the new usage model.

### 8. Summarize verification status

Produce a short note describing:

- what test areas are covered
- what remains weak or deferred
- what manual validation steps were performed

## Preferred Design Direction

- Use async tests and async tooling end to end
- Prefer validating real streaming/disposal behavior rather than materializing everything immediately
- Use stress and benchmark projects to validate concurrency assumptions introduced in earlier phases

## Deliverables

1. Updated tests and downstream project code
2. New or revised async-focused test coverage
3. Updated shell/sample/benchmark/stress usage
4. Verification note summarizing migration and test coverage status

## Acceptance Criteria

- Downstream projects compile against the new async-only API
- Tests cover the most important async correctness paths
- Shell/sample code demonstrates the intended async usage model
- Benchmark and stress projects are aligned with async semantics
- No sync compatibility shim was introduced just to preserve old consumers

## Risks and Traps

1. Updating only signatures without adding async-behavior tests can leave serious bugs undetected.
2. Shell or sample code may accidentally reintroduce sync blocking via `.Wait()` or `.Result`.
3. Benchmarks can become misleading if they benchmark setup overhead or sync emulation instead of real async usage.
4. Stress tools are essential for catching subtle transaction or disposal bugs from earlier phases.

## Suggested Execution Order

1. Fix compile breaks in tests
2. Add/update async correctness tests
3. Update shell and sample apps
4. Update benchmarks and stress tools
5. Run verification and write migration/test summary

## Validation

- Run solution tests where feasible
- Verify no `.Wait()`, `.Result`, or sync fallback patterns remain in migrated consumers
- Exercise query streaming, transaction, and file storage tests specifically
- Run at least one stress or benchmark pass if practical after the redesign

## Copy/Paste Handoff Prompt

> You are working on Phase 7 of the LiteDB async-only redesign. Your task is to migrate the rest of the solution—tests, shell, examples, benchmarks, and stress tools—to the new async-only API. Do not add sync compatibility shims. Use async setup/teardown, async disposal, awaited query/materialization patterns, and the new transaction/storage abstractions. Strengthen test coverage around async correctness, especially transactions, streaming, disposal, and concurrency.

