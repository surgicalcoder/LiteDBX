using System;

namespace LiteDbX.Benchmarks.Benchmarks.WAL;

public sealed class WalBenchmarkDocument
{
    [BsonId]
    public int Id { get; set; }

    public int WriterId { get; set; }

    public int TransactionGroup { get; set; }

    public int Ordinal { get; set; }

    public DateTime CreatedUtc { get; set; }

    public string Category { get; set; }

    public string Payload { get; set; }
}

