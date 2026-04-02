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
    public class QueryCompoundIndexBenchmark : BenchmarkBase
    {
        private const string COMPOUND_INDEX_NAME = "CompoundIndex1";
        private ILiteCollection<FileMetaBase> _fileMetaCollection;

        [GlobalSetup(Target = nameof(Query_SimpleIndex_Baseline))]
        public async Task GlobalSetupSimpleIndexBaseline()
        {
            File.Delete(DatabasePath);
            DatabaseInstance = await LiteDatabase.Open(ConnectionString());
            _fileMetaCollection = DatabaseInstance.GetCollection<FileMetaBase>();
            await _fileMetaCollection.EnsureIndex(fileMeta => fileMeta.ShouldBeShown);
            await _fileMetaCollection.EnsureIndex(fileMeta => fileMeta.IsFavorite);
            await _fileMetaCollection.Insert(FileMetaGenerator<FileMetaBase>.GenerateList(DatasetSize));
            await DatabaseInstance.Checkpoint();
        }

        [GlobalSetup(Target = nameof(Query_CompoundIndexVariant))]
        public async Task GlobalSetupCompoundIndexVariant()
        {
            DatabaseInstance = await LiteDatabase.Open(ConnectionString());
            _fileMetaCollection = DatabaseInstance.GetCollection<FileMetaBase>();
            await _fileMetaCollection.EnsureIndex(COMPOUND_INDEX_NAME, $"$.{nameof(FileMetaBase.IsFavorite)};$.{nameof(FileMetaBase.ShouldBeShown)}");
            await _fileMetaCollection.Insert(FileMetaGenerator<FileMetaBase>.GenerateList(DatasetSize));
            await DatabaseInstance.Checkpoint();
        }

        [Benchmark(Baseline = true)]
        public ValueTask<List<FileMetaBase>> Query_SimpleIndex_Baseline()
            => _fileMetaCollection.Find(Query.And(
                Query.EQ(nameof(FileMetaBase.IsFavorite), false),
                Query.EQ(nameof(FileMetaBase.ShouldBeShown), true))).ToListAsync();

        [Benchmark]
        public ValueTask<List<FileMetaBase>> Query_CompoundIndexVariant()
            => _fileMetaCollection.Find(Query.EQ(COMPOUND_INDEX_NAME, $"{false};{true}")).ToListAsync();

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