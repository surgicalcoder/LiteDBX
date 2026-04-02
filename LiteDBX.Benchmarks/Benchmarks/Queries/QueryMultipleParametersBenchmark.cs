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
    public class QueryMultipleParametersBenchmark : BenchmarkBase
    {
        private ILiteCollection<FileMetaBase> _fileMetaCollection;

        [GlobalSetup]
        public async Task GlobalSetup()
        {
            File.Delete(DatabasePath);
            DatabaseInstance = await LiteDatabase.Open(ConnectionString());
            _fileMetaCollection = DatabaseInstance.GetCollection<FileMetaBase>();
            await _fileMetaCollection.EnsureIndex(fileMeta => fileMeta.IsFavorite);
            await _fileMetaCollection.EnsureIndex(fileMeta => fileMeta.ShouldBeShown);
            await _fileMetaCollection.Insert(FileMetaGenerator<FileMetaBase>.GenerateList(DatasetSize));
            await DatabaseInstance.Checkpoint();
        }

        [Benchmark(Baseline = true)]
        public ValueTask<List<FileMetaBase>> Expression_Normal_Baseline()
            => _fileMetaCollection.Find(fileMeta => fileMeta.IsFavorite && fileMeta.ShouldBeShown).ToListAsync();

        [Benchmark]
        public ValueTask<List<FileMetaBase>> Query_Normal()
            => _fileMetaCollection.Find(Query.And(
                Query.EQ(nameof(FileMetaBase.IsFavorite), true),
                Query.EQ(nameof(FileMetaBase.ShouldBeShown), true))).ToListAsync();

        [Benchmark]
        public ValueTask<List<FileMetaBase>> Expression_ParametersSwitched()
            => _fileMetaCollection.Find(fileMeta => fileMeta.ShouldBeShown && fileMeta.IsFavorite).ToListAsync();

        [Benchmark]
        public ValueTask<List<FileMetaBase>> Query_ParametersSwitched()
            => _fileMetaCollection.Find(Query.And(
                Query.EQ(nameof(FileMetaBase.ShouldBeShown), true),
                Query.EQ(nameof(FileMetaBase.IsFavorite), true))).ToListAsync();

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