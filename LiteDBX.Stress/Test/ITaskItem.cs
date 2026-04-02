using System;
using System.Threading.Tasks;

namespace LiteDbX.Stress;

public interface ITestItem
{
    string Name { get; }
    int TaskCount { get; }
    TimeSpan Sleep { get; }
    ValueTask<BsonValue> Execute(LiteDatabase db);
}