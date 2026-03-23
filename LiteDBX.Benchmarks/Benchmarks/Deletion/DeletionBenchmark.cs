using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using LiteDbX.Benchmarks.Models;
using LiteDbX.Benchmarks.Models.Generators;

namespace LiteDbX.Benchmarks.Benchmarks.Deletion
{
    [BenchmarkCategory(Constants.Categories.DELETION)]
    public class DeletionBenchmark : BenchmarkBase
    {
        private List<FileMetaBase> _data;
        private ILiteCollection<FileMetaBase> _fileMetaCollection;

        [GlobalSetup]
        public async Task GlobalSetup()
        {
            File.Delete(DatabasePath);
            DatabaseInstance = new LiteDatabase(ConnectionString());
            _fileMetaCollection = DatabaseInstance.GetCollection<FileMetaBase>();
            await _fileMetaCollection.EnsureIndex(file => file.IsFavorite);
            await _fileMetaCollection.EnsureIndex(file => file.ShouldBeShown);
            _data = FileMetaGenerator<FileMetaBase>.GenerateList(DatasetSize);
        }

        [IterationSetup]
        public async Task IterationSetup()
        {
            await _fileMetaCollection.Insert(_data);
            await DatabaseInstance.Checkpoint();
        }

        [Benchmark(Baseline = true)]
        public async Task<int> DeleteAllExpression()
        {
            var count = await _fileMetaCollection.DeleteMany(_ => true);
            await DatabaseInstance.Checkpoint();
            return count;
        }

        [Benchmark]
        public async Task<int> DeleteAllBsonExpression()
        {
            var count = await _fileMetaCollection.DeleteMany("1 = 1");
            await DatabaseInstance.Checkpoint();
            return count;
        }

        [Benchmark]
        public async Task DropCollectionAndRecreate()
        {
            const string collectionName = nameof(FileMetaBase);

            var indexesCollection = DatabaseInstance.GetCollection("$indexes");
            var droppedIndexes = await indexesCollection.Query()
                .Where(x => x["collection"] == collectionName && x["name"] != "_id")
                .ToDocuments().ToListAsync();

            await DatabaseInstance.DropCollection(collectionName);

            foreach (var indexInfo in droppedIndexes)
            {
                await DatabaseInstance.GetCollection(collectionName)
                    .EnsureIndex(indexInfo["name"], BsonExpression.Create(indexInfo["expression"]), indexInfo["unique"]);
            }

            await DatabaseInstance.Checkpoint();
        }

        [GlobalCleanup]
        public async Task GlobalCleanup()
        {
            if (DatabaseInstance != null)
            {
                await DatabaseInstance.Checkpoint();
                await DatabaseInstance.DisposeAsync();
                DatabaseInstance = null;
            }
            File.Delete(DatabasePath);
        }
    }
}