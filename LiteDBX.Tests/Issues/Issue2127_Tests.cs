using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace LiteDbX.Tests.Issues;

public class Issue2127_Tests
{
    public class ReproTests
    {
        [Fact(Skip = "To slow for a unit test in a build process")]
        public async Task InsertItemBackToBack_Test()
        {
            var databaseDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DatabaseLocation");
            var databasePath = Path.Combine(databaseDirectory, "SampleDatabase.db");
            var databaseLogPath = Path.Combine(databaseDirectory, "SampleDatabase-log.db");

            // Repeat in a loop to hit that scenario where queue processing thread exists the loop but the task still remains incomplete.
            for (var i = 0; i < 100; i++)
            {
                if (Directory.Exists(databaseDirectory))
                {
                    Directory.Delete(databaseDirectory, true);
                }

                Directory.CreateDirectory(databaseDirectory);

                await using var subject = await ExampleItemRepository.CreateAsync(databasePath);

                var item1 = new ExampleItem
                {
                    Id = Guid.NewGuid(),
                    SomeProperty = Guid.NewGuid().ToString()
                };
                var item2 = new ExampleItem
                {
                    Id = Guid.NewGuid(),
                    SomeProperty = Guid.NewGuid().ToString()
                };

                await subject.Insert(item1);
                await subject.Insert(item2);

                // Allow items to be processed.
                await Task.Delay(1000);

                var liteDbPath = databasePath;
                var liteDbLogPath = databaseLogPath;
                var copiedLiteDbPath = liteDbPath + ".copy";
                var copiedLiteDbLogPath = liteDbLogPath + ".copy";
                File.Copy(liteDbPath, copiedLiteDbPath);
                File.Copy(liteDbLogPath, copiedLiteDbLogPath);
                var liteDbContent = File.ReadAllText(copiedLiteDbPath, Encoding.UTF8);
                liteDbContent += File.ReadAllText(copiedLiteDbLogPath, Encoding.UTF8);

                Assert.True(liteDbContent.Contains(item1.SomeProperty, StringComparison.OrdinalIgnoreCase), $"Could not find item 1 property. {item1.SomeProperty}, Iteration: {i}");
                Assert.True(liteDbContent.Contains(item2.SomeProperty, StringComparison.OrdinalIgnoreCase), $"Could not find item 2 property. {item2.SomeProperty}, Iteration: {i}");
            }
        }
    }

    public class ExampleItem
    {
        [BsonId]
        public Guid Id { get; set; }

        public string SomeProperty { get; set; }
    }

    public sealed class ExampleItemRepository : IAsyncDisposable
    {
        public const string DatabaseFileName = "SampleDb";

        private readonly LiteDatabase _liteDb;

        private ExampleItemRepository(LiteDatabase liteDb)
        {
            _liteDb = liteDb;
        }

        public static async ValueTask<ExampleItemRepository> CreateAsync(string databasePath)
        {
            var connectionString = new ConnectionString
            {
                Filename = databasePath,
                Connection = ConnectionType.Direct
            };

            return new ExampleItemRepository(await LiteDatabase.Open(connectionString));
        }

        public ValueTask DisposeAsync()
        {
            return _liteDb.DisposeAsync();
        }

        public async ValueTask Insert(ExampleItem item)
        {
            var collection = _liteDb.GetCollection<ExampleItem>();
            _ = await collection.Insert(item);
        }
    }
}