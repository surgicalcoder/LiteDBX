using System;
using System.Linq;
using System.Threading.Tasks;

namespace LiteDbX.Tests.QueryTest;

/// <summary>
/// Async-only test fixture for query tests.
/// Use <see cref="CreateAsync"/> rather than the constructor.
/// Implements <see cref="IAsyncDisposable"/> — use <c>await using</c>.
/// </summary>
public class PersonQueryData : IAsyncDisposable
{
    private readonly ILiteCollection<Person> _collection;
    private readonly ILiteDatabase _db;
    private readonly Person[] _local;

    private PersonQueryData(ILiteDatabase db, ILiteCollection<Person> collection, Person[] local)
    {
        _db = db;
        _collection = collection;
        _local = local;
    }

    /// <summary>Create and populate the in-memory test database.</summary>
    public static async ValueTask<PersonQueryData> CreateAsync()
    {
        var local = DataGen.Person().ToArray();
        var db = new LiteDatabase(":memory:");
        var collection = db.GetCollection<Person>("person");
        await collection.Insert(local);
        return new PersonQueryData(db, collection, local);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
    }

    public (ILiteCollection<Person>, Person[]) GetData()
    {
        return (_collection, _local);
    }
}