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
    public class QueryAllBenchmark : BenchmarkBase
    {
        private ILiteCollection<FileMetaBase> _fileMetaCollection;

        [GlobalSetup]
        public async Task GlobalSetup()
        {
            File.Delete(DatabasePath);
            DatabaseInstance = await LiteDatabase.Open(ConnectionString());
            _fileMetaCollection = DatabaseInstance.GetCollection<FileMetaBase>();
            await _fileMetaCollection.Insert(FileMetaGenerator<FileMetaBase>.GenerateList(DatasetSize));
            await DatabaseInstance.Checkpoint();
        }

        [Benchmark(Baseline = true)]
        public async Task<List<FileMetaBase>> FindAll()
        {
            return await _fileMetaCollection.FindAll().ToListAsync();
        }

        [Benchmark]
        public async Task<List<FileMetaBase>> FindAllWithExpression()
        {
            return await _fileMetaCollection.Find(_ => true).ToListAsync();
        }

        [Benchmark]
        public async Task<List<FileMetaBase>> FindAllWithQuery()
        {
            return await _fileMetaCollection.Find(Query.All()).ToListAsync();
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