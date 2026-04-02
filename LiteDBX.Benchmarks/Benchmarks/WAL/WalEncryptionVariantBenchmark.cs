using System.Collections.Generic;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

namespace LiteDbX.Benchmarks.Benchmarks.WAL;

[BenchmarkCategory(Constants.Categories.WAL)]
public class WalEncryptionVariantBenchmark : WalBenchmarkBase
{
    private ILiteCollection<WalBenchmarkDocument> _collection;
    private List<WalBenchmarkDocument> _documents;

    [Params(WalEncryptionMode.None, WalEncryptionMode.Ecb, WalEncryptionMode.Gcm)]
    public WalEncryptionMode EncryptionMode;

    [Params(16)]
    public int DocumentsPerTransaction;

    [Params(4096)]
    public int PayloadBytes;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _documents = CreateDocuments(DocumentsPerTransaction, PayloadBytes, 1);
    }

    [IterationSetup]
    public async Task IterationSetup()
    {
        await OpenDatabaseAsync(EncryptionMode, checkpointSize: 0);
        _collection = GetCollection();
    }

    [Benchmark]
    public async Task<int> CommitBatchWithEncryptionMode()
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

