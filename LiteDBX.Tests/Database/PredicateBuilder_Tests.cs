using System;
using System.IO;
using System.Linq.Expressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.Database;

public class PredicateBuilder_Tests
{
    [Fact(Skip = "Need review")]
    public async Task Usage_PredicateBuilder()
    {
        var p = PredicateBuilder.True<User>();

        p = p.And(x => x.Active);
        p = p.And(x => x.Age > 10);

        await using var db = await LiteDatabase.Open(new MemoryStream());
        var col = db.GetCollection<User>("user");

        await col.Insert(new User { Active = true, Age = 11, Name = "user" });

        var r1 = await col.FindOne(x => x.Active && x.Age > 10);
        r1.Name.Should().Be("user");

        var r2 = await col.FindOne(p);
        r2.Name.Should().Be("user");
    }

    #region Model

    public class User
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public bool Active { get; set; }
        public int Age { get; set; }
    }

    #endregion
}

// from http://www.albahari.com/nutshell/predicatebuilder.aspx
public static class PredicateBuilder
{
    public static Expression<Func<T, bool>> True<T>()
    {
        return f => true;
    }

    public static Expression<Func<T, bool>> False<T>()
    {
        return f => false;
    }

    public static Expression<Func<T, bool>> Or<T>(this Expression<Func<T, bool>> expr1,
        Expression<Func<T, bool>> expr2)
    {
        var invokedExpr = Expression.Invoke(expr2, expr1.Parameters);

        return Expression.Lambda<Func<T, bool>>
            (Expression.OrElse(expr1.Body, invokedExpr), expr1.Parameters);
    }

    public static Expression<Func<T, bool>> And<T>(this Expression<Func<T, bool>> expr1,
        Expression<Func<T, bool>> expr2)
    {
        var invokedExpr = Expression.Invoke(expr2, expr1.Parameters);

        return Expression.Lambda<Func<T, bool>>
            (Expression.AndAlso(expr1.Body, invokedExpr), expr1.Parameters);
    }
}