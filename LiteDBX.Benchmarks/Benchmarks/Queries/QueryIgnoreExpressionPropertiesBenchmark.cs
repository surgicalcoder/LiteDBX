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
    public class QueryIgnoreExpressionPropertiesBenchmark : BenchmarkBase
    {
        private ILiteCollection<FileMetaBase> _fileMetaCollection;
        private ILiteCollection<FileMetaWithExclusion> _fileMetaExclusionCollection;

        [GlobalSetup(Target = nameof(DeserializeBaseline))]
        public async Task GlobalSetup()
        {
            File.Delete(DatabasePath);
            DatabaseInstance = new LiteDatabase(ConnectionString());
            _fileMetaCollection = DatabaseInstance.GetCollection<FileMetaBase>();
            await _fileMetaCollection.EnsureIndex(fileMeta => fileMeta.ShouldBeShown);
            await _fileMetaCollection.Insert(FileMetaGenerator<FileMetaBase>.GenerateList(DatasetSize));
            await DatabaseInstance.Checkpoint();
        }

        [GlobalSetup(Target = nameof(DeserializeWithIgnore))]
        public async Task GlobalIndexSetup()
        {
            File.Delete(DatabasePath);
            DatabaseInstance = new LiteDatabase(ConnectionString());
            _fileMetaExclusionCollection = DatabaseInstance.GetCollection<FileMetaWithExclusion>();
            await _fileMetaExclusionCollection.EnsureIndex(fileMeta => fileMeta.ShouldBeShown);
            await _fileMetaExclusionCollection.Insert(FileMetaGenerator<FileMetaWithExclusion>.GenerateList(DatasetSize));
            await DatabaseInstance.Checkpoint();
        }

        [Benchmark(Baseline = true)]
        public ValueTask<List<FileMetaBase>> DeserializeBaseline()
            => _fileMetaCollection.Find(fileMeta => fileMeta.ShouldBeShown).ToListAsync();

        [Benchmark]
        public ValueTask<List<FileMetaWithExclusion>> DeserializeWithIgnore()
            => _fileMetaExclusionCollection.Find(fileMeta => fileMeta.ShouldBeShown).ToListAsync();

        [GlobalCleanup]
        public async Task GlobalCleanup()
        {
            if (DatabaseInstance != null)
            {
                await DatabaseInstance.DropCollection(nameof(FileMetaBase));
                await DatabaseInstance.DropCollection(nameof(FileMetaWithExclusion));
                await DatabaseInstance.Checkpoint();
                await DatabaseInstance.DisposeAsync();
                DatabaseInstance = null;
            }
            _fileMetaCollection = null;
            _fileMetaExclusionCollection = null;

            File.Delete(DatabasePath);
        }
    }
}