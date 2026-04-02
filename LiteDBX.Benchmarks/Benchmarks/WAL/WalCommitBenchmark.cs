using System.Collections.Generic;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

namespace LiteDbX.Benchmarks.Benchmarks.WAL
{
    [BenchmarkCategory(Constants.Categories.WAL)]
    public class WalCommitBenchmark : WalBenchmarkBase
    {
        private ILiteCollection<WalBenchmarkDocument> _collection;
        private List<WalBenchmarkDocument> _documents;

        [Params(1, 32)]
        public int DocumentsPerTransaction;

        [Params(512, 4096)]
        public int PayloadBytes;

        [GlobalSetup]
        public void GlobalSetup()
        {
            _documents = CreateDocuments(DocumentsPerTransaction, PayloadBytes, 1);
        }

        [IterationSetup]
        public async Task IterationSetup()
        {
            await OpenDatabaseAsync(checkpointSize: 0);
            _collection = GetCollection();
        }

        [Benchmark(Baseline = true)]
        public async Task<int> SingleTransactionCommit()
        {
            await using var transaction = await DatabaseInstance.BeginTransaction();
            var count = await _collection.Insert(_documents, transaction);
            await transaction.Commit();
            return count;
        }

        [IterationCleanup]
        public async Task IterationCleanup()
        {
            _collection = null;
            await CloseDatabaseAsync();
        }
    }
}


