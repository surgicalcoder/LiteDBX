using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using LiteDbX.Benchmarks.Models;
using LiteDbX.Benchmarks.Models.Generators;

namespace LiteDbX.Benchmarks.Benchmarks.Queries
{
    [BenchmarkCategory(Constants.Categories.QUERIES)]
    public class QuerySimpleIndexBenchmarks : BenchmarkBase
    {
        private ILiteCollection<FileMetaBase> _fileMetaCollection;

        [GlobalSetup(Targets = new[] { nameof(FindWithExpression), nameof(FindWithQuery) })]
        public async Task GlobalSetup()
        {
            File.Delete(DatabasePath);
            DatabaseInstance = new LiteDatabase(ConnectionString());
            _fileMetaCollection = DatabaseInstance.GetCollection<FileMetaBase>();
            await _fileMetaCollection.Insert(FileMetaGenerator<FileMetaBase>.GenerateList(DatasetSize));
        }

        [GlobalSetup(Targets = new[] { nameof(FindWithIndexExpression), nameof(FindWithIndexQuery) })]
        public async Task GlobalIndexSetup()
        {
            File.Delete(DatabasePath);
            DatabaseInstance = new LiteDatabase(ConnectionString());
            _fileMetaCollection = DatabaseInstance.GetCollection<FileMetaBase>();
            await _fileMetaCollection.EnsureIndex(fileMeta => fileMeta.IsFavorite);
            await _fileMetaCollection.Insert(FileMetaGenerator<FileMetaBase>.GenerateList(DatasetSize));
            await DatabaseInstance.Checkpoint();
        }

        [Benchmark(Baseline = true)]
        public ValueTask<List<FileMetaBase>> FindWithExpression()
            => _fileMetaCollection.Find(fileMeta => fileMeta.IsFavorite).ToListAsync();

        [Benchmark]
        public ValueTask<List<FileMetaBase>> FindWithQuery()
            => _fileMetaCollection.Find(Query.EQ(nameof(FileMetaBase.IsFavorite), true)).ToListAsync();

        [Benchmark]
        public ValueTask<List<FileMetaBase>> FindWithIndexExpression()
            => _fileMetaCollection.Find(fileMeta => fileMeta.IsFavorite).ToListAsync();

        [Benchmark]
        public ValueTask<List<FileMetaBase>> FindWithIndexQuery()
            => _fileMetaCollection.Find(Query.EQ(nameof(FileMetaBase.IsFavorite), true)).ToListAsync();

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