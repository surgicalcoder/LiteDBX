# LiteDbX - async-first evolution of LiteDB

[![NuGet Version](https://img.shields.io/nuget/v/LiteDbX)](https://www.nuget.org/packages/LiteDbX/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/LiteDbX)](https://www.nuget.org/packages/LiteDbX/)
[![NuGet Version](https://img.shields.io/nuget/vpre/LiteDbX)](https://www.nuget.org/packages/LiteDbX/absoluteLatest)

LiteDbX is a modern rewrite/evolution of LiteDB focused on an async-first embedded document database for .NET.

Today the project includes:

- async-first database and collection APIs built around `ValueTask` and `IAsyncEnumerable<T>`
- explicit `ILiteTransaction` scopes instead of thread-bound `BeginTrans` / `Commit` / `Rollback`
- a native `Query()` builder plus a provider-backed `AsQueryable()` LINQ surface
- async-only file storage handles via `ILiteStorage<TFileId>` and `ILiteFileHandle<TFileId>`
- optional AES-GCM encryption through the separate `LiteDbX.Encryption.Gcm` package
- updated shared access modes for async-aware direct, shared, and lock-file usage

> **Current status**
>
> A large portion of the async redesign has landed in the codebase already: public CRUD/query contracts, explicit transactions, async query execution, async file-storage handles, and the optional GCM provider.
> Some lifecycle and peripheral cleanup work is still tracked in `docs/async-redesign/`.

## Install

Core package:

```powershell
Install-Package LiteDbX
```

Optional AES-GCM provider:

```powershell
Install-Package LiteDbX.Encryption.Gcm
```

Current targets in this repository are:

- `netstandard2.0`
- `netstandard2.1`
- `net10.0`

## Quick start

LiteDbX operations are async-first today. Prefer the explicit open lifecycle and use `await` / `await using` for database work and disposal.

The canonical entry points are:

- `await LiteDatabase.Open(...)`
- `await LiteRepository.Open(...)`
- `await LiteEngine.Open(...)`

Constructor-based open still exists in a few public types as a compatibility bridge, but it is no longer the recommended lifecycle.

```csharp
public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int Age { get; set; }
    public string[] Phones { get; set; }
    public bool IsActive { get; set; }
}

await using var db = await LiteDatabase.Open(@"MyData.db");

var customers = db.GetCollection<Customer>("customers");

await customers.EnsureIndex(x => x.Name, unique: true);

var customer = new Customer
{
    Name = "John Doe",
    Phones = new[] { "8000-0000", "9000-0000" },
    Age = 39,
    IsActive = true
};

var id = await customers.Insert(customer);

var activeAdults = await customers.Query()
    .Where(x => x.IsActive && x.Age >= 18)
    .OrderBy(x => x.Name)
    .ToList();

await foreach (var row in customers.Find(x => x.IsActive))
{
    Console.WriteLine($"{row.Name} ({row.Age})");
}
```

## Explicit async transactions

LiteDbX no longer centers transactions around thread affinity.
Use `BeginTransaction()` and the returned `ILiteTransaction` scope.

```csharp
await using var db = await LiteDatabase.Open(@"MyData.db");
var customers = db.GetCollection<Customer>("customers");

await using var tx = await db.BeginTransaction();

await customers.Insert(new Customer
{
    Name = "Ana",
    Age = 28,
    IsActive = true
}, tx);

var names = await customers.Query(tx)
    .Where(x => x.IsActive)
    .Select(x => x.Name)
    .ToList();

await tx.Commit();
```

If the transaction scope is disposed before `Commit()`, LiteDbX rolls it back.

## Lifecycle guidance

Prefer this pattern everywhere:

```csharp
await using var db = await LiteDatabase.Open("filename=my-data.db");
```

For repository-style code, prefer:

```csharp
await using var repo = await LiteRepository.Open("filename=my-data.db");
```

Compatibility notes:

- constructor-based open remains available in `LiteDatabase`, `LiteRepository`, and `LiteEngine` for legacy callers
- synchronous pragma-backed properties such as `UserVersion`, `Timeout`, `LimitSize`, `UtcDate`, `CheckpointSize`, and `Collation` remain convenience bridges over async engine pragmas
- blocking `Dispose()` still exists, but `await using` / `DisposeAsync()` is the supported shutdown path

## Query APIs: native `Query()` and provider-backed LINQ

LiteDbX exposes two complementary query surfaces:

- `collection.Query()` — the native LiteDbX query builder
- `collection.AsQueryable()` — a provider-backed `IQueryable<T>` adapter for supported LINQ shapes

`AsQueryable()` does **not** replace `Query()`.
Provider-backed LINQ lowers back into the same native query model used by `Query()`.

### Use `Query()` when you want

- the full native LiteDbX query surface
- direct `BsonExpression` control
- grouped/manual query composition
- the clearest escape hatch for unsupported LINQ shapes

Native query composition remains synchronous, but execution is async-only.
Typical terminals include:

- `ToEnumerable()`
- `ToDocuments()`
- `ToList()`
- `ToArray()`
- `First()` / `FirstOrDefault()`
- `Single()` / `SingleOrDefault()`
- `Count()` / `LongCount()` / `Exists()`
- `GetPlan()`

### Use `AsQueryable()` when you want

- familiar LINQ composition over a collection root
- supported single-source query shapes
- translation into LiteDbX native execution without leaving LINQ syntax early

```csharp
var rows = await customers
    .AsQueryable()
    .Where(x => x.IsActive)
    .OrderBy(x => x.Name)
    .Select(x => new { x.Id, x.Name })
    .ToListAsync();
```

Transaction-aware roots are available too:

```csharp
await using var tx = await db.BeginTransaction();

var names = await customers
    .AsQueryable(tx)
    .Where(x => x.IsActive)
    .Select(x => x.Name)
    .ToArrayAsync();
```

### Async execution for provider-backed `IQueryable<T>`

Provider-backed LINQ composes synchronously but executes asynchronously via LiteDbX extension methods such as:

- `ToAsyncEnumerable()`
- `ToListAsync()`
- `ToArrayAsync()`
- `FirstAsync()`
- `FirstOrDefaultAsync()`
- `SingleAsync()`
- `SingleOrDefaultAsync()`
- `AnyAsync()`
- `CountAsync()`
- `LongCountAsync()`
- `GetPlanAsync()`

Do **not** rely on synchronous enumeration or synchronous LINQ materialization for provider-backed LiteDbX queries. Those paths are expected to fail clearly rather than silently doing sync-over-async work.

### Supported LINQ subset

The current provider is intentionally narrower than full LINQ-to-Objects or EF-style providers.

Supported core operators include:

- `Where`
- `Select`
- `OrderBy`
- `OrderByDescending`
- `ThenBy`
- `ThenByDescending`
- `Skip`
- `Take`

Supported grouped LINQ is intentionally narrow and engine-aligned:

- `GroupBy(key)`
- optional grouped `Where(...)` lowering to native `HAVING`
- grouped aggregate projections such as:
  - `Select(g => new { g.Key, Count = g.Count() })`
  - `Select(g => new { g.Key, Sum = g.Sum(x => x.SomeField) })`

For unsupported shapes, fall back to `collection.Query()`.

## Async file storage

LiteDbX file storage now exposes async-only handles instead of a public `Stream` subclass.

- `db.FileStorage` gives you the default storage using `_files` / `_chunks`
- `db.GetStorage<TFileId>(...)` gives you a custom file-id type or collection names
- `OpenRead(...)` / `OpenWrite(...)` return `ILiteFileHandle<TFileId>`
- `Upload(...)` / `Download(...)` remain async

```csharp
await using var db = await LiteDatabase.Open(@"MyData.db");

await using var writer = await db.FileStorage.OpenWrite("readme-demo", "demo.txt");
await writer.Write(System.Text.Encoding.UTF8.GetBytes("hello LiteDbX"));
await writer.Flush();
```

## Encryption: built-in ECB and optional AES-GCM

LiteDbX currently supports two AES-based encrypted file modes:

- `ECB` — built into the core `LiteDbX` package
- `GCM` — provided by the optional `LiteDbX.Encryption.Gcm` package

To use GCM:

1. reference `LiteDbX.Encryption.Gcm`
2. call `GcmEncryptionRegistration.Register()` once at startup
3. configure `AESEncryption = AESEncryptionType.GCM`
4. provide a password

```csharp
using LiteDbX.Encryption.Gcm;

GcmEncryptionRegistration.Register();

var cs = new ConnectionString("filename=secure.db;password=secret;encryption=GCM");

await using var db = await LiteDatabase.Open(cs);
```

Important notes:

- if no password is supplied, encryption is not used
- ECB remains built in and needs no extra registration
- GCM is explicit by design; LiteDbX does not auto-load the provider
- existing encrypted files reopen according to their stored format, so older ECB files remain readable and existing GCM files reopen as GCM

For more detail, see:

- `docs/gcm-setup.md`
- `docs/aes-gcm-mode.md`

## Connection modes and shared access

LiteDbX currently supports three connection types:

| Mode | Intended use | Cross-process guarantee | Explicit `ILiteTransaction` support |
|---|---|---|---|
| `ConnectionType.Direct` | normal dedicated engine access | none beyond normal file semantics | ✅ Supported |
| `ConnectionType.Shared` | async-safe serialized access inside one process | ❌ No — in-process only | ❌ Not supported |
| `ConnectionType.LockFile` | physical-file cross-process write coordination | ✅ Yes, via lock file | ❌ Not supported |

Additional notes:

- `Shared` is a supported in-process mode. It no longer implies the old named-mutex cross-process behaviour.
- `LockFile` is supported only for physical filename-based databases; it does not support custom streams, `:memory:`, or `:temp:`.
- Both `Shared` and `LockFile` support nested single-call operations, but not long-lived explicit transaction scope across arbitrary user code.

If you need explicit transaction scopes, prefer `Direct`.
If you need cross-process file coordination, prefer `LockFile` rather than assuming `Shared` provides the old named-mutex behavior.

## Project status and design docs

The repository contains detailed handoff and decision docs for the ongoing redesign work.

Recommended starting points:

- `docs/async-redesign/README.md` — phase-by-phase async redesign index
- `docs/ASYNC_ONLY_REDESIGN_PLAN.md` — high-level redesign plan
- `docs/gcm-setup.md` — practical GCM setup steps
- `docs/aes-gcm-mode.md` — implementation-focused GCM format notes
- `docs/INHERITED_BSONMAPPER_CONVENTIONS_PLAN.md` — mapper convention design work

Important current caveats tracked in those docs include:

- constructor-based open still exists as a transitional compatibility path, but the supported lifecycle is now `await LiteDatabase.Open(...)`
- `await LiteRepository.Open(...)` and `await LiteEngine.Open(...)` follow the same supported async-first lifecycle pattern
- some synchronous convenience surfaces remain as compatibility bridges over async internals, especially constructor-based open and pragma-backed configuration properties
- shared/lock-file modes are intentionally limited relative to direct mode, especially around explicit transaction scope
- provider-backed LINQ is intentionally a supported subset, not a full general-purpose LINQ provider

## Good fit for

- desktop and local applications
- embedded per-user or per-tenant data stores
- application file formats
- tools and services that want a lightweight single-file document database with async-friendly APIs

## License

[MIT](http://opensource.org/licenses/MIT)
