using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace LiteDbX;

internal static class LiteDbXGroupedQueryTranslator
{
    private static readonly HashSet<string> SupportedAggregateMethods = new(StringComparer.Ordinal)
    {
        nameof(Enumerable.Count),
        nameof(Enumerable.Sum),
        nameof(Enumerable.Min),
        nameof(Enumerable.Max),
        nameof(Enumerable.Average)
    };

    public static BsonExpression TranslateSelect(BsonMapper mapper, LambdaExpression lambda)
    {
        var context = GroupedTranslationContext.Create(mapper, lambda);
        var fragment = TranslateProjection(context, lambda.Body, isTopLevel: true);
        return fragment.ToExpression();
    }

    public static BsonExpression TranslateHaving(BsonMapper mapper, LambdaExpression lambda)
    {
        var context = GroupedTranslationContext.Create(mapper, lambda);
        var fragment = TranslatePredicate(context, lambda.Body);
        return fragment.ToExpression();
    }

    private static GroupedFragment TranslateProjection(GroupedTranslationContext context, Expression expression, bool isTopLevel)
    {
        expression = StripConvert(expression);

        switch (expression)
        {
            case NewExpression @new:
                return TranslateNewProjection(context, @new);

            case MemberInitExpression init:
                return TranslateMemberInitProjection(context, init);

            case MemberExpression member when IsGroupKey(member, context.GroupParameter):
                return GroupedFragment.Raw("@key");

            case MethodCallExpression call when IsSupportedAggregate(call, context.GroupParameter):
                return TranslateAggregate(context, call);

            case UnaryExpression unary when unary.NodeType == ExpressionType.Convert || unary.NodeType == ExpressionType.ConvertChecked:
                return TranslateProjection(context, unary.Operand, isTopLevel);
        }

        if (!ContainsParameter(expression, context.GroupParameter))
        {
            if (isTopLevel)
            {
                throw UnsupportedGroupedShape(
                    "Top-level grouped projection must project the group key, grouped aggregates, or a document composed from those values",
                    context.Lambda,
                    "Use GroupBy(...).Select(g => new { g.Key, Count = g.Count() })-style projections, or fall back to collection.Query() for advanced grouped queries.");
            }

            return TranslateViaMapper(context, expression);
        }

        throw UnsupportedGroupedShape(
            "This grouped projection shape is not supported by the LiteDbX LINQ provider",
            expression,
            "Supported grouped projections are limited to g.Key, direct grouped aggregates, and document projections composed from those values. Fall back to collection.Query() for raw group contents or nested grouped composition.");
    }

    private static GroupedFragment TranslatePredicate(GroupedTranslationContext context, Expression expression)
    {
        expression = StripConvert(expression);

        switch (expression)
        {
            case BinaryExpression binary:
                return TranslateBinaryPredicate(context, binary);

            case UnaryExpression unary when unary.NodeType == ExpressionType.Not:
            {
                var operand = TranslatePredicate(context, unary.Operand);
                return GroupedFragment.Composite($"({operand.Source} = false)", operand.Parameters);
            }

            case UnaryExpression unary when unary.NodeType == ExpressionType.Convert || unary.NodeType == ExpressionType.ConvertChecked:
                return TranslatePredicate(context, unary.Operand);

            case MemberExpression member when IsGroupKey(member, context.GroupParameter):
                return GroupedFragment.Raw("@key");

            case MethodCallExpression call when IsSupportedAggregate(call, context.GroupParameter):
                return TranslateAggregate(context, call);
        }

        if (!ContainsParameter(expression, context.GroupParameter))
        {
            return TranslateViaMapper(context, expression);
        }

        throw UnsupportedGroupedShape(
            "This grouped HAVING predicate shape is not supported by the LiteDbX LINQ provider",
            expression,
            "Supported grouped predicates are limited to comparisons and boolean combinations over g.Key and direct grouped aggregates. Fall back to collection.Query().");
    }

    private static GroupedFragment TranslateBinaryPredicate(GroupedTranslationContext context, BinaryExpression binary)
    {
        var left = IsPredicateOperand(binary.Left, context.GroupParameter)
            ? TranslatePredicate(context, binary.Left)
            : TranslateViaMapper(context, binary.Left);

        var right = IsPredicateOperand(binary.Right, context.GroupParameter)
            ? TranslatePredicate(context, binary.Right)
            : TranslateViaMapper(context, binary.Right);

        return GroupedFragment.Combine(left, right, $"({left.Source}{GetBinaryOperator(binary.NodeType)}{right.Source})");
    }

    private static bool IsPredicateOperand(Expression expression, ParameterExpression groupParameter)
    {
        expression = StripConvert(expression);

        return expression switch
        {
            BinaryExpression => true,
            UnaryExpression unary when unary.NodeType == ExpressionType.Not => true,
            MemberExpression member when IsGroupKey(member, groupParameter) => true,
            MethodCallExpression call when IsSupportedAggregate(call, groupParameter) => true,
            _ => ContainsParameter(expression, groupParameter)
        };
    }

    private static GroupedFragment TranslateNewProjection(GroupedTranslationContext context, NewExpression expression)
    {
        if (expression.Members == null || expression.Members.Count != expression.Arguments.Count)
        {
            throw UnsupportedGroupedShape(
                "Grouped projection constructors must expose member names",
                expression,
                "Use an anonymous-object or object-initializer projection, or fall back to collection.Query().");
        }

        var bindings = expression.Members.Zip(expression.Arguments, (member, value) => (member, value));
        return BuildDocumentProjection(context, expression.Type, bindings);
    }

    private static GroupedFragment TranslateMemberInitProjection(GroupedTranslationContext context, MemberInitExpression expression)
    {
        var bindings = new List<(MemberInfo member, Expression value)>();

        foreach (var binding in expression.Bindings)
        {
            if (binding is not MemberAssignment assignment)
            {
                throw UnsupportedGroupedShape(
                    "Grouped object-initializer projections only support simple member assignments",
                    expression,
                    "Use a projection like new { Key = g.Key, Count = g.Count() }, or fall back to collection.Query().");
            }

            bindings.Add((assignment.Member, assignment.Expression));
        }

        return BuildDocumentProjection(context, expression.Type, bindings);
    }

    private static GroupedFragment BuildDocumentProjection(
        GroupedTranslationContext context,
        Type resultType,
        IEnumerable<(MemberInfo member, Expression value)> bindings)
    {
        var builder = new StringBuilder("{ ");
        var combinedParameters = new BsonDocument();
        var first = true;
        var parameterIndex = 0;

        foreach (var (member, valueExpression) in bindings)
        {
            if (!first)
            {
                builder.Append(", ");
            }

            var fieldName = ResolveProjectionFieldName(context.Mapper, resultType, member);
            var value = TranslateProjection(context, valueExpression, isTopLevel: false);
            var valueSource = MergeParameters(combinedParameters, value.Parameters, value.Source, ref parameterIndex);

            builder.Append(fieldName);
            builder.Append(": ");
            builder.Append(valueSource);

            first = false;
        }

        builder.Append(" }");

        return GroupedFragment.Composite(builder.ToString(), combinedParameters);
    }

    private static GroupedFragment TranslateViaMapper(GroupedTranslationContext context, Expression expression)
    {
        var lambda = Expression.Lambda(expression, context.GroupParameter);
        var translated = LiteDbXLambdaTranslator.Translate(context.Mapper, lambda);
        return GroupedFragment.FromExpression(translated);
    }

    private static GroupedFragment TranslateAggregate(GroupedTranslationContext context, MethodCallExpression expression)
    {
        if (expression.Arguments.Count == 1)
        {
            return GroupedFragment.Raw($"{GetAggregateFunctionName(expression.Method.Name)}(*)");
        }

        var selector = LiteDbXQueryExpressionHelper.StripQuotes(expression.Arguments[1]);
        if (selector is not LambdaExpression selectorLambda)
        {
            throw UnsupportedGroupedShape(
                "Grouped aggregate selectors must be lambda expressions",
                expression,
                "Use direct grouped aggregate selectors such as g.Sum(x => x.Age), or fall back to collection.Query().");
        }

        var translatedSelector = LiteDbXLambdaTranslator.Translate(context.Mapper, selectorLambda);
        var selectorSource = RewriteSelectorSourceForGroupMap(translatedSelector.Source);

        return GroupedFragment.Composite(
            $"{GetAggregateFunctionName(expression.Method.Name)}(MAP(*=>{selectorSource}))",
            translatedSelector.Parameters ?? new BsonDocument());
    }

    private static string RewriteSelectorSourceForGroupMap(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return source;
        }

        var tokenizer = new Tokenizer(source);
        var builder = new StringBuilder(source.Length);

        while (true)
        {
            var token = tokenizer.ReadToken(false);

            if (token.Type == TokenType.EOF)
            {
                break;
            }

            if (token.Type == TokenType.Dollar)
            {
                var next = tokenizer.LookAhead(false);

                if (next.Type == TokenType.Period)
                {
                    tokenizer.ReadToken(false);

                    var afterPeriod = tokenizer.LookAhead(false);

                    if (afterPeriod.Type == TokenType.OpenBracket)
                    {
                        builder.Append("@.");
                    }

                    continue;
                }

                if (next.Type == TokenType.OpenBracket)
                {
                    builder.Append("@.");
                    continue;
                }

                builder.Append("@");
                continue;
            }

            builder.Append(token.Value);
        }

        return builder.ToString();
    }


    private static string GetAggregateFunctionName(string methodName)
    {
        return methodName switch
        {
            nameof(Enumerable.Count) => "COUNT",
            nameof(Enumerable.Sum) => "SUM",
            nameof(Enumerable.Min) => "MIN",
            nameof(Enumerable.Max) => "MAX",
            nameof(Enumerable.Average) => "AVG",
            _ => throw new NotSupportedException($"Grouped aggregate {methodName} is not supported by the LiteDbX LINQ provider.")
        };
    }

    private static string ResolveProjectionFieldName(BsonMapper mapper, Type resultType, MemberInfo member)
    {
        try
        {
            var entity = mapper.GetEntityMapper(resultType);
            entity.WaitForInitialization();

            var mapped = entity.Members.FirstOrDefault(x => x.MemberName == member.Name);
            if (mapped != null)
            {
                return mapped.FieldName;
            }
        }
        catch
        {
            // fall back to the CLR member name when the result type is not a mapper-backed entity shape.
        }

        return member.Name;
    }

    private static bool IsGroupKey(MemberExpression expression, ParameterExpression groupParameter)
    {
        return expression.Expression == groupParameter && expression.Member.Name == nameof(IGrouping<int, int>.Key);
    }

    private static bool IsSupportedAggregate(MethodCallExpression expression, ParameterExpression groupParameter)
    {
        if (!SupportedAggregateMethods.Contains(expression.Method.Name))
        {
            return false;
        }

        if (expression.Method.DeclaringType != typeof(Enumerable))
        {
            return false;
        }

        var source = expression.Arguments.FirstOrDefault();
        source = StripConvert(LiteDbXQueryExpressionHelper.StripQuotes(source));

        return source == groupParameter;
    }

    private static Expression StripConvert(Expression expression)
    {
        while (expression is UnaryExpression unary &&
               (unary.NodeType == ExpressionType.Convert || unary.NodeType == ExpressionType.ConvertChecked || unary.NodeType == ExpressionType.Quote))
        {
            expression = unary.Operand;
        }

        return expression;
    }

    private static bool ContainsParameter(Expression expression, ParameterExpression parameter)
    {
        var visitor = new GroupParameterVisitor(parameter);
        visitor.Visit(expression);
        return visitor.HasParameterReference;
    }

    private static string GetBinaryOperator(ExpressionType nodeType)
    {
        return nodeType switch
        {
            ExpressionType.Equal => " = ",
            ExpressionType.NotEqual => " != ",
            ExpressionType.GreaterThan => " > ",
            ExpressionType.GreaterThanOrEqual => " >= ",
            ExpressionType.LessThan => " < ",
            ExpressionType.LessThanOrEqual => " <= ",
            ExpressionType.And => " AND ",
            ExpressionType.AndAlso => " AND ",
            ExpressionType.Or => " OR ",
            ExpressionType.OrElse => " OR ",
            _ => throw UnsupportedGroupedShape(
                $"Binary operator {nodeType} is not supported in grouped predicates",
                null,
                "Use simple comparisons and boolean combinations over g.Key and grouped aggregates, or fall back to collection.Query().")
        };
    }

    private static string MergeParameters(BsonDocument target, BsonDocument source, string expressionSource, ref int parameterIndex)
    {
        if (source == null || source.Count == 0)
        {
            return expressionSource;
        }

        var rewritten = expressionSource;

        foreach (var key in source.Keys.OrderByDescending(x => x.Length).ToArray())
        {
            var newKey = "gp" + parameterIndex++;
            rewritten = rewritten.Replace("@" + key, "@" + newKey);
            target[newKey] = source[key];
        }

        return rewritten;
    }

    private static NotSupportedException UnsupportedGroupedShape(string message, Expression expression, string guidance)
    {
        var detail = expression == null ? string.Empty : $" ({expression})";
        return new NotSupportedException($"{message}{detail}. {guidance}");
    }

    private sealed class GroupedTranslationContext
    {
        private GroupedTranslationContext(BsonMapper mapper, LambdaExpression lambda, ParameterExpression groupParameter)
        {
            Mapper = mapper;
            Lambda = lambda;
            GroupParameter = groupParameter;
        }

        public BsonMapper Mapper { get; }

        public LambdaExpression Lambda { get; }

        public ParameterExpression GroupParameter { get; }

        public static GroupedTranslationContext Create(BsonMapper mapper, LambdaExpression lambda)
        {
            if (mapper == null) throw new ArgumentNullException(nameof(mapper));
            if (lambda == null) throw new ArgumentNullException(nameof(lambda));
            if (lambda.Parameters.Count != 1) throw new NotSupportedException($"Grouped LINQ translation requires a single grouping parameter ({lambda}).");
            if (!IsGroupingType(lambda.Parameters[0].Type)) throw new NotSupportedException($"Grouped LINQ translation requires an IGrouping<TKey, TElement> parameter ({lambda}).");

            return new GroupedTranslationContext(mapper, lambda, lambda.Parameters[0]);
        }

        private static bool IsGroupingType(Type type)
        {
            if (type == null || !type.IsGenericType)
            {
                return false;
            }

            return type.GetGenericTypeDefinition() == typeof(IGrouping<,>) ||
                   type.GetInterfaces().Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IGrouping<,>));
        }
    }

    private sealed class GroupParameterVisitor : ExpressionVisitor
    {
        private readonly ParameterExpression _parameter;

        public GroupParameterVisitor(ParameterExpression parameter)
        {
            _parameter = parameter;
        }

        public bool HasParameterReference { get; private set; }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node == _parameter)
            {
                HasParameterReference = true;
            }

            return base.VisitParameter(node);
        }
    }

    private readonly struct GroupedFragment
    {
        public GroupedFragment(string source, BsonDocument parameters)
        {
            Source = source;
            Parameters = parameters ?? new BsonDocument();
        }

        public string Source { get; }

        public BsonDocument Parameters { get; }

        public static GroupedFragment Raw(string source) => new(source, new BsonDocument());

        public static GroupedFragment Composite(string source, BsonDocument parameters) => new(source, parameters);

        public static GroupedFragment FromExpression(BsonExpression expression)
            => new(expression.Source, expression.Parameters ?? new BsonDocument());

        public static GroupedFragment Combine(GroupedFragment left, GroupedFragment right, string source)
        {
            var parameters = new BsonDocument();
            var parameterIndex = 0;
            var mergedSource = source;

            mergedSource = MergeInto(parameters, left, mergedSource, ref parameterIndex);
            mergedSource = MergeInto(parameters, right, mergedSource, ref parameterIndex);

            return new GroupedFragment(mergedSource, parameters);
        }

        public BsonExpression ToExpression() => BsonExpression.Create(Source, Parameters);

        private static string MergeInto(BsonDocument parameters, GroupedFragment fragment, string source, ref int parameterIndex)
        {
            if (fragment.Parameters == null || fragment.Parameters.Count == 0)
            {
                return source;
            }

            foreach (var key in fragment.Parameters.Keys.OrderByDescending(x => x.Length).ToArray())
            {
                var newKey = "gp" + parameterIndex++;
                source = source.Replace("@" + key, "@" + newKey);
                parameters[newKey] = fragment.Parameters[key];
            }

            return source;
        }
    }
}


