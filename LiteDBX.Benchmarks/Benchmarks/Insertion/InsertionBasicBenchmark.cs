using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using LiteDbX.Benchmarks.Models;
using LiteDbX.Benchmarks.Models.Generators;

namespace LiteDbX.Benchmarks.Benchmarks.Insertion
{
    [BenchmarkCategory(Constants.Categories.INSERTION)]
    public class InsertionBasicBenchmark : BenchmarkBase
    {
        private List<FileMetaBase> _data;
        private ILiteCollection<FileMetaBase> _fileMetaCollection;

        [GlobalSetup]
        public async Task GlobalSetup()
        {
            File.Delete(DatabasePath);
            DatabaseInstance = new LiteDatabase(ConnectionString());
            _fileMetaCollection = DatabaseInstance.GetCollection<FileMetaBase>();
            _data = FileMetaGenerator<FileMetaBase>.GenerateList(DatasetSize);
        }

        [Benchmark(Baseline = true)]
        public async Task<int> Insertion()
        {
            var count = await _fileMetaCollection.Insert(_data);
            await DatabaseInstance.Checkpoint();
            return count;
        }

        [Benchmark]
        public async Task InsertionWithLoop()
        {
            for (var i = 0; i < _data.Count; i++)
            {
                await _fileMetaCollection.Insert(_data[i]);
            }
            await DatabaseInstance.Checkpoint();
        }

        [Benchmark]
        public async Task<int> Upsertion()
        {
            var count = await _fileMetaCollection.Upsert(_data);
            await DatabaseInstance.Checkpoint();
            return count;
        }

        [Benchmark]
        public async Task UpsertionWithLoop()
        {
            for (var i = 0; i < _data.Count; i++)
            {
                await _fileMetaCollection.Upsert(_data[i]);
            }
            await DatabaseInstance.Checkpoint();
        }

        [IterationCleanup]
        public async Task IterationCleanup()
        {
            await DatabaseInstance.DropCollection(nameof(FileMetaBase));
            await DatabaseInstance.Checkpoint();
            await DatabaseInstance.Rebuild();
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