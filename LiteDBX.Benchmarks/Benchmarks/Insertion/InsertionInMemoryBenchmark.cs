using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using LiteDbX.Benchmarks.Models;
using LiteDbX.Benchmarks.Models.Generators;

namespace LiteDbX.Benchmarks.Benchmarks.Insertion
{
    [BenchmarkCategory(Constants.Categories.INSERTION)]
    public class InsertionInMemoryBenchmark : BenchmarkBase
    {
        private List<FileMetaBase> _data;
        private ILiteDatabase _databaseInstanceInMemory;
        private ILiteDatabase _databaseInstanceNormal;
        private ILiteCollection<FileMetaBase> _fileMetaInMemoryCollection;
        private ILiteCollection<FileMetaBase> _fileMetaNormalCollection;

        [GlobalSetup(Target = nameof(InsertionNormal))]
        public async Task GlobalSetupNormal()
        {
            File.Delete(DatabasePath);
            _data = FileMetaGenerator<FileMetaBase>.GenerateList(DatasetSize);
            _databaseInstanceNormal = await LiteDatabase.Open(ConnectionString());
            _fileMetaNormalCollection = _databaseInstanceNormal.GetCollection<FileMetaBase>();
        }

        [GlobalSetup(Target = nameof(InsertionInMemory))]
        public async Task GlobalSetupInMemory()
        {
            _data = FileMetaGenerator<FileMetaBase>.GenerateList(DatasetSize);
            _databaseInstanceInMemory = await LiteDatabase.Open(new System.IO.MemoryStream());
            _fileMetaInMemoryCollection = _databaseInstanceInMemory.GetCollection<FileMetaBase>();
        }

        [Benchmark(Baseline = true)]
        public async Task<int> InsertionNormal()
        {
            var count = await _fileMetaNormalCollection.Insert(_data);
            await _databaseInstanceNormal.Checkpoint();

            return count;
        }

        [Benchmark]
        public async Task<int> InsertionInMemory()
        {
            var count = await _fileMetaInMemoryCollection.Insert(_data);
            await _databaseInstanceInMemory.Checkpoint();

            return count;
        }

        [IterationCleanup(Target = nameof(InsertionNormal))]
        public async Task CleanUpNormal()
        {
            const string collectionName = nameof(FileMetaBase);
            var indexesCollection = _databaseInstanceNormal.GetCollection("$indexes");
            var droppedIndexes = await indexesCollection.Query()
                .Where(x => x["collection"] == collectionName && x["name"] != "_id")
                .ToDocuments().ToListAsync();

            await _databaseInstanceNormal.DropCollection(collectionName);

            foreach (var indexInfo in droppedIndexes)
            {
                await _databaseInstanceNormal.GetCollection(collectionName)
                    .EnsureIndex(indexInfo["name"], BsonExpression.Create(indexInfo["expression"]), indexInfo["unique"]);
            }

            await _databaseInstanceNormal.Checkpoint();
            await _databaseInstanceNormal.Rebuild();
        }

        [IterationCleanup(Target = nameof(InsertionInMemory))]
        public async Task CleanUpInMemory()
        {
            const string collectionName = nameof(FileMetaBase);
            var indexesCollection = _databaseInstanceInMemory.GetCollection("$indexes");
            var droppedIndexes = await indexesCollection.Query()
                .Where(x => x["collection"] == collectionName && x["name"] != "_id")
                .ToDocuments().ToListAsync();

            await _databaseInstanceInMemory.DropCollection(collectionName);

            foreach (var indexInfo in droppedIndexes)
            {
                await _databaseInstanceInMemory.GetCollection(collectionName)
                    .EnsureIndex(indexInfo["name"], BsonExpression.Create(indexInfo["expression"]), indexInfo["unique"]);
            }

            await _databaseInstanceInMemory.Checkpoint();
            await _databaseInstanceInMemory.Rebuild();
        }

        [GlobalCleanup(Target = nameof(InsertionNormal))]
        public async Task GlobalCleanupNormal()
        {
            _fileMetaNormalCollection = null;
            if (_databaseInstanceNormal != null)
            {
                await _databaseInstanceNormal.Checkpoint();
                await _databaseInstanceNormal.DisposeAsync();
                _databaseInstanceNormal = null;
            }
            File.Delete(DatabasePath);
        }

        [GlobalCleanup(Target = nameof(InsertionInMemory))]
        public async Task GlobalCleanupInMemory()
        {
            _fileMetaInMemoryCollection = null;
            if (_databaseInstanceInMemory != null)
            {
                await _databaseInstanceInMemory.Checkpoint();
                await _databaseInstanceInMemory.DisposeAsync();
                _databaseInstanceInMemory = null;
            }
        }
    }
}