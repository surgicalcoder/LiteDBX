using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LiteDbX.Tests.QueryTest;

/// <summary>
/// Async-only test fixture for group-by query tests.
/// Use <see cref="CreateAsync"/> rather than the constructor.
/// </summary>
public class PersonGroupByData : IAsyncDisposable
{
    private readonly ILiteCollection<Person> _collection;
    private readonly ILiteDatabase _db;
    private readonly Person[] _local;

    private PersonGroupByData(ILiteDatabase db, ILiteCollection<Person> collection, Person[] local)
    {
        _db = db;
        _collection = collection;
        _local = local;
    }

    public static async ValueTask<PersonGroupByData> CreateAsync()
    {
        var local = DataGen.Person(1, 1000).ToArray();
        var db = await LiteDatabase.Open(new MemoryStream());
        var collection = db.GetCollection<Person>();
        await collection.Insert(local);
        await collection.EnsureIndex(x => x.Age);
        return new PersonGroupByData(db, collection, local);
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