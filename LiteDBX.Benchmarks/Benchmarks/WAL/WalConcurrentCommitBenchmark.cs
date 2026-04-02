using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

namespace LiteDbX.Benchmarks.Benchmarks.WAL
{
    [BenchmarkCategory(Constants.Categories.WAL)]
    public class WalConcurrentCommitBenchmark : WalBenchmarkBase
    {
        private List<List<WalBenchmarkDocument>> _writerBatches;

        [Params(2, 8)]
        public int WriterCount;

        [Params(8)]
        public int DocumentsPerTransaction;

        [Params(512, 4096)]
        public int PayloadBytes;

        [GlobalSetup]
        public void GlobalSetup()
        {
            _writerBatches = new List<List<WalBenchmarkDocument>>(WriterCount);

            var nextId = 1;
            for (var writer = 0; writer < WriterCount; writer++)
            {
                _writerBatches.Add(CreateDocuments(DocumentsPerTransaction, PayloadBytes, nextId, writer, writer));
                nextId += DocumentsPerTransaction;
            }
        }

        [IterationSetup]
        public async Task IterationSetup()
        {
            await OpenDatabaseAsync(checkpointSize: 0);
        }

        [Benchmark]
        public async Task<int> ConcurrentExplicitTransactionCommits()
        {
            var tasks = new Task<int>[WriterCount];

            for (var writer = 0; writer < WriterCount; writer++)
            {
                var writerId = writer;
                tasks[writer] = ExecuteWriterAsync(writerId);
            }

            var results = await Task.WhenAll(tasks);
            return results.Sum();
        }

        [IterationCleanup]
        public async Task IterationCleanup()
        {
            await CloseDatabaseAsync();
        }

        private async Task<int> ExecuteWriterAsync(int writerId)
        {
            var collection = GetCollection();

            await using var transaction = await DatabaseInstance.BeginTransaction();
            var count = await collection.Insert(_writerBatches[writerId], transaction);
            await transaction.Commit();
            return count;
        }
    }
}


