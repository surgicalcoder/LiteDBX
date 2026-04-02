using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.Engine;

public class ParallelQuery_Tests
{
    [Fact(Skip = "Must fix parallel query fetch")]
    public async Task Query_Parallel()
    {
        await using var db = await LiteDatabase.Open(new MemoryStream());
        var col  = db.GetCollection<Person>("person");
        var all  = DataGen.Person().ToArray();

        await col.Insert(all);

        var bag = new ConcurrentBag<Person>();
        var people = await col.FindAll().ToListAsync();

        var tasks = people.Select(async person =>
        {
            var col2   = db.GetCollection<Person>("person");
            var exists = await col2.Exists(x => x.Id == person.Id);

            if (exists)
            {
                var col3 = db.GetCollection<Person>("person");
                var item = await col3.FindOne(x => x.Id == person.Id);
                bag.Add(item);
            }
        });

        await Task.WhenAll(tasks);

        all.Length.Should().Be(bag.Count);
    }
}