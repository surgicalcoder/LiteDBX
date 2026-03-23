using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace LiteDbX.Tests.Issues;

public class Issue2506_Tests
{
    [Fact]
    public async Task Test()
    {
        await using LiteDatabase dataBase = new("demo.db");
        var fileStorage = dataBase.GetStorage<string>("myFiles", "myChunks");

        // Upload empty test file to file storage
        using MemoryStream emptyStream = new();
        await fileStorage.Upload("photos/2014/picture-01.jpg", "picture-01.jpg", emptyStream);

        // Find file reference by its ID
        var file = await fileStorage.FindById("photos/2014/picture-01.jpg");
        Assert.NotNull(file);

        // Download file to disk
        await fileStorage.Download("photos/2014/picture-01.jpg",
            Path.Combine(Path.GetTempPath(), "new-picture.jpg"), true);

        // Find all files matching pattern
        var files = await fileStorage.Find("_id LIKE 'photos/2014/%'").ToListAsync();
        Assert.Single(files);

        // Find all files matching pattern using parameters
        var files2 = await fileStorage.Find("_id LIKE @0", "photos/2014/%").ToListAsync();
        Assert.Single(files2);
    }
}