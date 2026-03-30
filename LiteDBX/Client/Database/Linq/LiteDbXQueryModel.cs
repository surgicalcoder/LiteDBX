using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using LiteDbX.Engine;

namespace LiteDbX;

internal interface ILiteDbXQueryableAccessor
{
    LiteDbXQueryState State { get; }
}

internal enum LiteDbXQueryMethodKind
{
    Where,
    GroupBy,
    Select,
    OrderBy,
    OrderByDescending,
    ThenBy,
    ThenByDescending,
    Skip,
    Take
}

internal enum LiteDbXQueryTerminalKind
{
    None,
    ToDocuments,
    ToEnumerable,
    ToAsyncEnumerable,
    ToList,
    ToArray,
    First,
    FirstOrDefault,
    Single,
    SingleOrDefault,
    Any,
    Count,
    LongCount,
    ExecuteReader,
    GetPlan,
    Into
}

internal sealed class LiteDbXQueryRoot
{
    public LiteDbXQueryRoot(
        ILiteEngine engine,
        BsonMapper mapper,
        string collectionName,
        Type entityType,
        IEnumerable<BsonExpression> includes,
        ILiteTransaction transaction)
    {
        Engine = engine ?? throw new ArgumentNullException(nameof(engine));
        Mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        CollectionName = string.IsNullOrWhiteSpace(collectionName)
            ? throw new ArgumentNullException(nameof(collectionName))
            : collectionName;
        EntityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
        Includes = (includes ?? Enumerable.Empty<BsonExpression>()).ToArray();
        Transaction = transaction;
    }

    public ILiteEngine Engine { get; }

    public BsonMapper Mapper { get; }

    public string CollectionName { get; }

    public Type EntityType { get; }

    public IReadOnlyList<BsonExpression> Includes { get; }

    public ILiteTransaction Transaction { get; }
}

internal sealed class LiteDbXQueryOperator
{
    public LiteDbXQueryOperator(
        LiteDbXQueryMethodKind kind,
        MethodCallExpression call,
        LambdaExpression lambda = null,
        Expression valueExpression = null,
        Type resultType = null)
    {
        Kind = kind;
        Call = call ?? throw new ArgumentNullException(nameof(call));
        Lambda = lambda;
        ValueExpression = valueExpression;
        ResultType = resultType;
    }

    public LiteDbXQueryMethodKind Kind { get; }

    public MethodCallExpression Call { get; }

    public LambdaExpression Lambda { get; }

    public Expression ValueExpression { get; }

    public Type ResultType { get; }
}

internal sealed class LiteDbXQueryState
{
    private readonly LiteDbXQueryOperator[] _operators;

    private LiteDbXQueryState(
        LiteDbXQueryRoot root,
        Type currentElementType,
        IEnumerable<LiteDbXQueryOperator> operators,
        bool hasProjection,
        bool isScalarProjection,
        bool isDocumentProjection,
        bool isGrouped,
        LiteDbXQueryTerminalKind terminalKind,
        Type terminalResultType,
        Expression queryExpression)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
        RootEntityType = root.EntityType;
        CurrentElementType = currentElementType ?? throw new ArgumentNullException(nameof(currentElementType));
        _operators = (operators ?? Enumerable.Empty<LiteDbXQueryOperator>()).ToArray();
        HasProjection = hasProjection;
        IsScalarProjection = isScalarProjection;
        IsDocumentProjection = isDocumentProjection;
        IsGrouped = isGrouped;
        TerminalKind = terminalKind;
        TerminalResultType = terminalResultType;
        QueryExpression = queryExpression;
    }

    public LiteDbXQueryRoot Root { get; }

    public Type RootEntityType { get; }

    public Type CurrentElementType { get; }

    public IReadOnlyList<LiteDbXQueryOperator> Operators => _operators;

    public bool HasPrimaryOrdering => _operators.Any(x =>
        x.Kind == LiteDbXQueryMethodKind.OrderBy ||
        x.Kind == LiteDbXQueryMethodKind.OrderByDescending);

    public bool HasAnyOrdering => HasPrimaryOrdering || _operators.Any(x =>
        x.Kind == LiteDbXQueryMethodKind.ThenBy ||
        x.Kind == LiteDbXQueryMethodKind.ThenByDescending);

    public bool HasOffset => _operators.Any(x => x.Kind == LiteDbXQueryMethodKind.Skip);

    public bool HasLimit => _operators.Any(x => x.Kind == LiteDbXQueryMethodKind.Take);

    public bool HasPaging => HasOffset || HasLimit;

    public bool HasProjection { get; }

    public bool HasGroupedProjection => IsGrouped && HasProjection;

    public bool IsScalarProjection { get; }

    public bool IsDocumentProjection { get; }

    public bool IsGrouped { get; }

    public LiteDbXQueryTerminalKind TerminalKind { get; }

    public Type TerminalResultType { get; }

    public Expression QueryExpression { get; }

    public static LiteDbXQueryState CreateRoot(LiteDbXQueryRoot root)
    {
        if (root == null) throw new ArgumentNullException(nameof(root));

        return new LiteDbXQueryState(
            root,
            root.EntityType,
            Array.Empty<LiteDbXQueryOperator>(),
            hasProjection: false,
            isScalarProjection: IsScalarType(root.EntityType),
            isDocumentProjection: root.EntityType == typeof(BsonDocument),
            isGrouped: false,
            terminalKind: LiteDbXQueryTerminalKind.None,
            terminalResultType: null,
            queryExpression: null);
    }

    public LiteDbXQueryState AppendOperator(
        LiteDbXQueryOperator operation,
        Type currentElementType,
        bool? hasProjection = null,
        bool? isGrouped = null,
        Expression queryExpression = null)
    {
        if (operation == null) throw new ArgumentNullException(nameof(operation));
        if (currentElementType == null) throw new ArgumentNullException(nameof(currentElementType));

        var operators = new LiteDbXQueryOperator[_operators.Length + 1];
        Array.Copy(_operators, operators, _operators.Length);
        operators[_operators.Length] = operation;

        var projection = hasProjection ?? HasProjection || operation.Kind == LiteDbXQueryMethodKind.Select;
        var grouped = isGrouped ?? IsGrouped;

        return new LiteDbXQueryState(
            Root,
            currentElementType,
            operators,
            projection,
            IsScalarType(currentElementType),
            currentElementType == typeof(BsonDocument),
            grouped,
            TerminalKind,
            TerminalResultType,
            queryExpression ?? QueryExpression);
    }

    public LiteDbXQueryState WithTerminal(LiteDbXQueryTerminalKind terminalKind, Type terminalResultType)
    {
        return new LiteDbXQueryState(
            Root,
            CurrentElementType,
            _operators,
            HasProjection,
            IsScalarProjection,
            IsDocumentProjection,
            IsGrouped,
            terminalKind,
            terminalResultType,
            QueryExpression);
    }

    public LiteDbXQueryState WithQueryExpression(Expression queryExpression)
    {
        return new LiteDbXQueryState(
            Root,
            CurrentElementType,
            _operators,
            HasProjection,
            IsScalarProjection,
            IsDocumentProjection,
            IsGrouped,
            TerminalKind,
            TerminalResultType,
            queryExpression);
    }

    public string Describe()
    {
        var operators = _operators.Length == 0
            ? "root"
            : string.Join(" -> ", _operators.Select(x => x.Kind.ToString()));

        var terminal = TerminalKind == LiteDbXQueryTerminalKind.None
            ? "none"
            : TerminalKind.ToString();

        return $"LiteDbXQueryable(Collection={Root.CollectionName}, Root={RootEntityType.Name}, Current={CurrentElementType.Name}, Operators={operators}, Terminal={terminal})";
    }

    private static bool IsScalarType(Type type)
    {
        var effectiveType = Nullable.GetUnderlyingType(type) ?? type;

        return effectiveType == typeof(BsonValue) || Reflection.IsSimpleType(effectiveType);
    }
}

