using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using LiteDbX.Benchmarks.Models;
using LiteDbX.Benchmarks.Models.Generators;

namespace LiteDbX.Benchmarks.Benchmarks.Queries
{
    [BenchmarkCategory(Constants.Categories.QUERIES)]
    public class QueryCountBenchmark : BenchmarkBase
    {
        private ILiteCollection<FileMetaBase> _fileMetaCollection;

        [GlobalSetup]
        public async Task GlobalSetup()
        {
            File.Delete(DatabasePath);
            DatabaseInstance = new LiteDatabase(ConnectionString());
            _fileMetaCollection = DatabaseInstance.GetCollection<FileMetaBase>();
            await _fileMetaCollection.EnsureIndex(fileMeta => fileMeta.ShouldBeShown);
            await _fileMetaCollection.Insert(FileMetaGenerator<FileMetaBase>.GenerateList(DatasetSize));
            await DatabaseInstance.Checkpoint();
        }

        [Benchmark(Baseline = true)]
        public async Task<int> CountWithLinq()
        {
            return await _fileMetaCollection.Find(Query.EQ(nameof(FileMetaBase.ShouldBeShown), true)).CountAsync();
        }

        [Benchmark]
        public async Task<int> CountWithExpression()
        {
            return await _fileMetaCollection.Count(fileMeta => fileMeta.ShouldBeShown);
        }

        [Benchmark]
        public async Task<int> CountWithQuery()
        {
            return await _fileMetaCollection.Count(Query.EQ(nameof(FileMetaBase.ShouldBeShown), true));
        }

        [GlobalCleanup]
        public async Task GlobalCleanup()
        {
            if (DatabaseInstance != null)
            {
                await DatabaseInstance.DropCollection(nameof(FileMetaBase));
                await DatabaseInstance.Checkpoint();
                await DatabaseInstance.DisposeAsync();
                DatabaseInstance = null;
            }
            File.Delete(DatabasePath);
        }
    }
}