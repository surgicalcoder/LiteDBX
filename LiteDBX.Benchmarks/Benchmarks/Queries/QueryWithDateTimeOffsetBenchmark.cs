using System;
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
    public class QueryWithDateTimeOffsetBenchmark : BenchmarkBase
    {
        private DateTime _dateTimeConstraint;
        private BsonValue _dateTimeConstraintBsonValue;
        private ILiteCollection<FileMetaBase> _fileMetaCollection;

        [GlobalSetup]
        public async Task GlobalSetup()
        {
            File.Delete(DatabasePath);
            DatabaseInstance = new LiteDatabase(ConnectionString());
            _fileMetaCollection = DatabaseInstance.GetCollection<FileMetaBase>();
            await _fileMetaCollection.EnsureIndex(fileMeta => fileMeta.ValidFrom);
            await _fileMetaCollection.EnsureIndex(fileMeta => fileMeta.ValidTo);
            await _fileMetaCollection.EnsureIndex(fileMeta => fileMeta.ShouldBeShown);
            await _fileMetaCollection.Insert(FileMetaGenerator<FileMetaBase>.GenerateList(DatasetSize));
            await DatabaseInstance.Checkpoint();
            _dateTimeConstraint = DateTime.Now;
            _dateTimeConstraintBsonValue = new BsonValue(_dateTimeConstraint);
        }

        [Benchmark(Baseline = true)]
        public ValueTask<List<FileMetaBase>> Expression_Normal_Baseline()
            => _fileMetaCollection.Find(fileMeta =>
                (fileMeta.ValidFrom > _dateTimeConstraint || fileMeta.ValidTo < _dateTimeConstraint) && fileMeta.ShouldBeShown)
                .ToListAsync();

        [Benchmark]
        public ValueTask<List<FileMetaBase>> Query_Normal()
            => _fileMetaCollection.Find(Query.And(
                Query.Or(
                    Query.GT(nameof(FileMetaBase.ValidFrom), _dateTimeConstraintBsonValue),
                    Query.LT(nameof(FileMetaBase.ValidTo), _dateTimeConstraintBsonValue)),
                Query.EQ(nameof(FileMetaBase.ShouldBeShown), true)))
                .ToListAsync();

        [Benchmark]
        public ValueTask<List<FileMetaBase>> Expression_ParametersSwitched()
            => _fileMetaCollection.Find(fileMeta =>
                fileMeta.ShouldBeShown && (fileMeta.ValidFrom > _dateTimeConstraint || fileMeta.ValidTo < _dateTimeConstraint))
                .ToListAsync();

        [Benchmark]
        public ValueTask<List<FileMetaBase>> Query_ParametersSwitched()
            => _fileMetaCollection.Find(Query.And(
                Query.EQ(nameof(FileMetaBase.ShouldBeShown), true),
                Query.Or(
                    Query.GT(nameof(FileMetaBase.ValidFrom), _dateTimeConstraintBsonValue),
                    Query.LT(nameof(FileMetaBase.ValidTo), _dateTimeConstraintBsonValue))))
                .ToListAsync();

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