using LiteDbX;
using LiteDbX.Engine;
using LiteDbX.Migrations;

if (args.Length > 0 && string.Equals(args[0], "migrations-demo", StringComparison.OrdinalIgnoreCase))
{
    await RunMigrationsDemoAsync();
    return;
}

var password = "46jLz5QWd5fI3m4LiL2r";
var path = $"C:\\LiteDB\\Examples\\CrashDB_{DateTime.Now.Ticks}.db";

var settings = new EngineSettings
{
    AutoRebuild = true,
    Filename = path,
    Password = password
};

var data = Enumerable.Range(1, 10_000).Select(i => new BsonDocument
{
    ["_id"] = i,
    ["name"] = Faker.Fullname(),
    ["age"] = Faker.Age(),
    ["created"] = Faker.Birthday(),
    ["lorem"] = Faker.Lorem(5, 25)
}).ToArray();

try
{
    await using (var db = await LiteEngine.Open(settings))
    {
#if DEBUG
        db.SimulateDiskWriteFail = (page) =>
        {
            var p = new BasePage(page);

            if (p.PageID == 248)
            {
                page.Write((uint)123123123, 8192-4);
            }
        };
#endif

        await db.Pragma("USER_VERSION", 123);

        await db.EnsureIndex("col1", "idx_age", BsonExpression.Create("$.age"), false);

        await db.Insert("col1", data, BsonAutoId.Int32);
        await db.Insert("col2", data, BsonAutoId.Int32);

        var col1 = await CountAsync(db.Query("col1", Query.All()));
        var col2 = await CountAsync(db.Query("col2", Query.All()));

        Console.WriteLine("Inserted Col1: " + col1);
        Console.WriteLine("Inserted Col2: " + col2);
    }
}
catch (Exception ex)
{
    Console.WriteLine("ERROR: " + ex.Message);
}

Console.WriteLine("Recovering database...");

await using (var db = await LiteEngine.Open(settings))
{
    var col1 = await CountAsync(db.Query("col1", Query.All()));
    var col2 = await CountAsync(db.Query("col2", Query.All()));

    Console.WriteLine($"Col1: {col1}");
    Console.WriteLine($"Col2: {col2}");

    var errors = new BsonArray(await ToListAsync(db.Query("_rebuild_errors", Query.All()))).ToString();

    Console.WriteLine("Errors: " + errors);

}

/*
var errors = new List<FileReaderError>();
var fr = new FileReaderV8(settings, errors);

fr.Open();
var pragmas = fr.GetPragmas();
var cols = fr.GetCollections().ToArray();
var indexes = fr.GetIndexes(cols[0]);

var docs1 = fr.GetDocuments("col1").ToArray();
var docs2 = fr.GetDocuments("col2").ToArray();


Console.WriteLine("Recovered Col1: " + docs1.Length);
Console.WriteLine("Recovered Col2: " + docs2.Length);

Console.WriteLine("# Errors: ");
errors.ForEach(x => Console.WriteLine($"PageID: {x.PageID}/{x.Origin}/#{x.Position}[{x.Collection}]: " + x.Message));
*/

Console.WriteLine("\n\nEnd.");
Console.ReadKey();

static async Task<int> CountAsync(IAsyncEnumerable<BsonDocument> source)
{
    var count = 0;

    await foreach (var _ in source)
    {
        count++;
    }

    return count;
}

static async Task<List<BsonDocument>> ToListAsync(IAsyncEnumerable<BsonDocument> source)
{
    var results = new List<BsonDocument>();

    await foreach (var item in source)
    {
        results.Add(item);
    }

    return results;
}

static async Task RunMigrationsDemoAsync()
{
    await using var db = await LiteDatabase.Open(":memory:");
    var customers = db.GetCollection("customers");

    await customers.Insert(new BsonDocument
    {
        ["_id"] = new BsonValue(1),
        ["Name"] = new BsonValue("  Alice  "),
        ["Profile"] = new BsonDocument()
    });

    await customers.Insert(new BsonDocument
    {
        ["_id"] = new BsonValue(2),
        ["Name"] = new BsonValue("Bob"),
        ["Orders"] = new BsonArray
        {
            new BsonDocument
            {
                ["Legacy"] = new BsonDocument()
            }
        }
    });

    var report = await db.Migrations()
        .OnProgress(progress =>
            Console.WriteLine($"[{progress.Stage}] migration={progress.MigrationName}, collection={progress.CollectionName ?? "-"}, scanned={progress.DocumentsScanned}, modified={progress.DocumentsModified}, inserted={progress.DocumentsInserted}"))
        .Migration("demo-cleanup", m => m.ForCollection("customers", c =>
        {
            c.ModifyFieldWhen("Name", ctx => new BsonValue(ctx.Value.AsString.Trim()), BsonPredicates.IsString);
            c.SetFieldWhen("**.Touched", new BsonValue(true), BsonPredicates.Always);
            c.InsertDocumentWhen(new BsonDocument
            {
                ["_id"] = new BsonValue(3),
                ["Name"] = new BsonValue("Seeded")
            });
        }))
        .RunAsync(new MigrationRunOptions { StrictPathResolution = true });

    Console.WriteLine($"Inserted by migration: {report.Migrations[0].DocumentsInserted}");

    foreach (var doc in await ToListAsync(customers.FindAll()))
    {
        Console.WriteLine(doc.ToString());
    }
}

