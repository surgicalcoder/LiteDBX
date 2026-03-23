using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using LiteDbX.Benchmarks.Models;
using LiteDbX.Benchmarks.Models.Generators;

namespace LiteDbX.Benchmarks.Benchmarks.Insertion
{
    [BenchmarkCategory(Constants.Categories.INSERTION)]
    public class InsertionIgnoreExpressionPropertyBenchmark : BenchmarkBase
    {
        private List<FileMetaBase> _baseData;
        private List<FileMetaWithExclusion> _baseDataWithBsonIgnore;

        private ILiteCollection<FileMetaBase> _fileMetaCollection;
        private ILiteCollection<FileMetaWithExclusion> _fileMetaExclusionCollection;

        [GlobalSetup(Target = nameof(Insertion))]
        public async Task GlobalBsonIgnoreSetup()
        {
            File.Delete(DatabasePath);

            DatabaseInstance = new LiteDatabase(ConnectionString());
            _fileMetaCollection = DatabaseInstance.GetCollection<FileMetaBase>();
            await _fileMetaCollection.EnsureIndex(fileMeta => fileMeta.ShouldBeShown);

            _baseData = FileMetaGenerator<FileMetaBase>.GenerateList(DatasetSize); // executed once per each N value
        }

        [GlobalSetup(Target = nameof(InsertionWithBsonIgnore))]
        public async Task GlobalIgnorePropertySetup()
        {
            File.Delete(DatabasePath);

            DatabaseInstance = new LiteDatabase(ConnectionString());
            _fileMetaExclusionCollection = DatabaseInstance.GetCollection<FileMetaWithExclusion>();
            await _fileMetaExclusionCollection.EnsureIndex(fileMeta => fileMeta.ShouldBeShown);

            _baseDataWithBsonIgnore = FileMetaGenerator<FileMetaWithExclusion>.GenerateList(DatasetSize); // executed once per each N value
        }

        [Benchmark(Baseline = true)]
        public async ValueTask<int> Insertion()
        {
            var count = await _fileMetaCollection.Insert(_baseData);
            await DatabaseInstance.Checkpoint();

            return count;
        }

        [Benchmark]
        public async ValueTask<int> InsertionWithBsonIgnore()
        {
            var count = await _fileMetaExclusionCollection.Insert(_baseDataWithBsonIgnore);
            await DatabaseInstance.Checkpoint();

            return count;
        }

        [IterationCleanup]
        public async Task IterationCleanup()
        {
            var indexesCollection = DatabaseInstance.GetCollection("$indexes");
            var droppedCollectionIndexes = await indexesCollection.Query()
                .Where(x => x["name"] != "_id")
                .ToList();

            await foreach (var name in DatabaseInstance.GetCollectionNames())
            {
                await DatabaseInstance.DropCollection(name);
            }

            foreach (var indexInfo in droppedCollectionIndexes)
            {
                await DatabaseInstance.GetCollection(indexInfo["collection"].AsString)
                    .EnsureIndex(indexInfo["name"].AsString,
                        BsonExpression.Create(indexInfo["expression"]),
                        indexInfo["unique"].AsBoolean);
            }

            await DatabaseInstance.Checkpoint();
            await DatabaseInstance.Rebuild();
        }

        [GlobalCleanup]
        public async Task GlobalCleanup()
        {
            _baseData?.Clear();
            _baseData = null;
            _baseDataWithBsonIgnore?.Clear();
            _baseDataWithBsonIgnore = null;

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