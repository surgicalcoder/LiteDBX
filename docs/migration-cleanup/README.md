# LiteDbX Migration Cleanup Plan Pack

## Purpose

This folder contains the implementation plan for a new `LiteDbX.Migrations` project that can:

- repair legacy documents where `_id` or another id-like field was stored as `string` instead of `ObjectId`
- remove obsolete fields such as empty arrays
- add or modify fields conditionally without changing unrelated document shape
- target one collection, many collections, or collection name patterns such as `*`, prefix, and suffix matches
- support both top-level and nested-field cleanup over time
- provide a fluent, journaled migration runner that operates on raw `BsonDocument` values so unaffected data is preserved exactly

This pack started as a staged implementation handoff bundle and now also serves as a status/reference pack for the shipped `LiteDbX.Migrations` surface.

## Why this needs its own project

The reported failure:

```text
System.InvalidCastException: Unable to cast object of type 'System.String' to type 'LiteDbX.ObjectId'.
```

is happening during typed mapper deserialization. That means the migration layer must avoid typed entity materialization while repairing data and instead operate over raw `BsonDocument` collections.

## Planned deliverables

- `LiteDbX.Migrations/` class library
- `LiteDbX.Migrations.Tests/` test project
- fluent migration API
- in-place and rebuild/swap executors
- migration journal collection
- durable id remap collection for old->new ids generated during repair
- reusable cleanup predicate catalog
- reusable field mutation primitives
- staged support for nested field traversal
- operator-focused execution controls such as dry-run preview, strict path resolution, progress callbacks, and backup lifecycle helpers

## Document index

1. [`00-master-roadmap.md`](./00-master-roadmap.md) - overall architecture, stages, milestones, and collection selector semantics
2. [`01-invalid-objectid-policy.md`](./01-invalid-objectid-policy.md) - detailed behavior for `InvalidObjectIdPolicy`, including `GenerateNewId` and durable old->new id logging
3. [`02-nested-paths-and-array-cleanup.md`](./02-nested-paths-and-array-cleanup.md) - v1 and v2 path traversal and nested cleanup scope; distinct from collection-name wildcards in `ForCollection(...)`
4. [`03-bson-predicates-catalog.md`](./03-bson-predicates-catalog.md) - proposed `BsonPredicates` catalog and composition rules
5. [`04-mutation-primitives.md`](./04-mutation-primitives.md) - design for `AddFieldWhen(...)`, `ModifyFieldWhen(...)`, and other core mutation operations
6. [`05-operational-safety.md`](./05-operational-safety.md) - dry-run execution, backup retention policy, backup cleanup helpers, and migration operator safety features
7. [`MASTER_HANDOFF_PROMPT.md`](./MASTER_HANDOFF_PROMPT.md) - full prompt for another LLM to implement this in stages

## Related context

This pack is complementary to the wider mapper work documented in:

- [`../INHERITED_BSONMAPPER_CONVENTIONS_PLAN.md`](../INHERITED_BSONMAPPER_CONVENTIONS_PLAN.md)

The migration package should not depend on that mapper work being completed first. It should function against raw BSON documents today.

