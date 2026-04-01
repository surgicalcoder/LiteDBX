using System.Collections.Generic;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

namespace LiteDbX.Benchmarks.Benchmarks.WAL
{
    [BenchmarkCategory(Constants.Categories.WAL)]
    public class WalCheckpointBenchmark : WalBenchmarkBase
    {
        private List<List<WalBenchmarkDocument>> _batches;
        private ILiteCollection<WalBenchmarkDocument> _collection;

        [Params(32, 128)]
        public int TransactionsBeforeCheckpoint;

        [Params(8)]
        public int DocumentsPerTransaction;

        [Params(512, 4096)]
        public int PayloadBytes;

        [GlobalSetup]
        public void GlobalSetup()
        {
            _batches = CreateBatches(TransactionsBeforeCheckpoint, DocumentsPerTransaction, PayloadBytes);
        }

        [IterationSetup]
        public void IterationSetup()
        {
            IterationSetupAsync().GetAwaiter().GetResult();
        }

        [Benchmark]
        public async Task ManualCheckpointAfterCommittedBurst()
        {
            await DatabaseInstance.Checkpoint();
        }

        [IterationCleanup]
        public void IterationCleanup()
        {
            _collection = null;
            CloseDatabaseAsync().GetAwaiter().GetResult();
        }

        private async Task IterationSetupAsync()
        {
            await OpenDatabaseAsync(checkpointSize: 0);
            _collection = GetCollection();

            foreach (var batch in _batches)
            {
                await using var transaction = await DatabaseInstance.BeginTransaction();
                await _collection.Insert(batch, transaction);
                await transaction.Commit();
            }
        }
    }
}


