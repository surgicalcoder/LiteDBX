using FluentAssertions;
using System.Threading.Tasks;
using Xunit;

namespace LiteDbX.Tests.Database;

public class MissingIdDocTest
{
    [Fact]
    public async Task MissingIdDoc_Test()
    {
        using var file = new TempFile();
        await using var db = new LiteDatabase(file.Filename);
        var col = db.GetCollection<MissingIdDoc>("col");

        var p = new MissingIdDoc { Name = "John", Age = 39 };

        // ObjectID will be generated
        var id = await col.Insert(p);

        p.Age = 41;

        await col.Update(id, p);

        var r = await col.FindById(id);

        r.Name.Should().Be(p.Name);
    }

    #region Model

    public class MissingIdDoc
    {
        public string Name { get; set; }
        public int Age { get; set; }
    }

    #endregion
}