using System;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX;

public partial class LiteCollection<T>
{
    /// <summary>
    /// Create a new permanent index in all documents inside this collections if index not exists already. Returns true if
    /// index was created or false if already exits
    /// </summary>
    /// <param name="name">Index name - unique name for this collection</param>
    /// <param name="expression">Create a custom expression function to be indexed</param>
    /// <param name="unique">If is a unique index</param>
    public ValueTask<bool> EnsureIndex(string name, BsonExpression expression, bool unique = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentNullException(nameof(name));
        }

        if (expression == null)
        {
            throw new ArgumentNullException(nameof(expression));
        }

        return _engine.EnsureIndex(Name, name, expression, unique, cancellationToken);
    }

    /// <summary>
    /// Create a new permanent index in all documents inside this collections if index not exists already. Returns true if
    /// index was created or false if already exits
    /// </summary>
    /// <param name="expression">Document field/expression</param>
    /// <param name="unique">If is a unique index</param>
    public ValueTask<bool> EnsureIndex(BsonExpression expression, bool unique = false, CancellationToken cancellationToken = default)
    {
        if (expression == null)
        {
            throw new ArgumentNullException(nameof(expression));
        }

        var name = Regex.Replace(expression.Source, @"[^a-z0-9]", "", RegexOptions.IgnoreCase);

        return EnsureIndex(name, expression, unique, cancellationToken);
    }

    /// <summary>
    /// Create a new permanent index in all documents inside this collections if index not exists already.
    /// </summary>
    /// <param name="keySelector">LinqExpression to be converted into BsonExpression to be indexed</param>
    /// <param name="unique">Create a unique keys index?</param>
    public ValueTask<bool> EnsureIndex<K>(Expression<Func<T, K>> keySelector, bool unique = false, CancellationToken cancellationToken = default)
    {
        return EnsureIndex(GetIndexExpression(keySelector), unique, cancellationToken);
    }

    /// <summary>
    /// Create a new permanent index in all documents inside this collections if index not exists already.
    /// </summary>
    /// <param name="name">Index name - unique name for this collection</param>
    /// <param name="keySelector">LinqExpression to be converted into BsonExpression to be indexed</param>
    /// <param name="unique">Create a unique keys index?</param>
    public ValueTask<bool> EnsureIndex<K>(string name, Expression<Func<T, K>> keySelector, bool unique = false, CancellationToken cancellationToken = default)
    {
        return EnsureIndex(name, GetIndexExpression(keySelector), unique, cancellationToken);
    }

    /// <summary>
    /// Drop index and release slot for another index
    /// </summary>
    public ValueTask<bool> DropIndex(string name, CancellationToken cancellationToken = default)
        => _engine.DropIndex(Name, name, cancellationToken);

    /// <summary>
    /// Get index expression based on LINQ expression. Convert IEnumerable in MultiKey indexes
    /// </summary>
    private BsonExpression GetIndexExpression<K>(Expression<Func<T, K>> keySelector)
    {
        var expression = _mapper.GetIndexExpression(keySelector);

        if (typeof(K).IsEnumerable() && expression.IsScalar)
        {
            if (expression.Type == BsonExpressionType.Path)
            {
                // convert LINQ expression that returns an IEnumerable but expression returns a single value
                // `x => x.Phones` --> `$.Phones[*]`
                // works only if expression is a simple path
                expression = expression.Source + "[*]";
            }
            else
            {
                throw new LiteException(0, $"Expression `{expression.Source}` must return a enumerable expression");
            }
        }

        return expression;
    }
}