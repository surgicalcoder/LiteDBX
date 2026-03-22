using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX;

internal partial class SqlParser
{
    /// <summary>
    /// SELECT / EXPLAIN SELECT — synchronous parse, async-stream execution via BsonDataReader.
    /// The returned reader wraps an IAsyncEnumerable from the engine; no await is needed here.
    /// </summary>
    private IBsonDataReader ParseSelect(CancellationToken cancellationToken = default)
    {
        var query = new Query();
        var token = _tokenizer.ReadToken();

        query.ExplainPlan = token.Is("EXPLAIN");
        if (query.ExplainPlan) token = _tokenizer.ReadToken();

        token.Expect("SELECT");

        query.Select = BsonExpression.Create(_tokenizer, BsonExpressionParserMode.SelectDocument, _parameters);

        var from = _tokenizer.ReadToken();

        if (from.Type == TokenType.EOF || from.Type == TokenType.SemiColon)
        {
            // SELECT with no FROM — pure expression, no engine I/O
            var result = query.Select.Execute(_collation);
            var defaultName = "expr";
            var data = System.Linq.Enumerable
                .FirstOrDefault(System.Linq.Enumerable
                    .Select(result, x => x.IsDocument ? x.AsDocument : new BsonDocument { [defaultName] = x }));
            return new BsonDataReader(data);
        }

        if (from.Is("INTO"))
        {
            query.Into = ParseCollection(_tokenizer);
            query.IntoAutoId = ParseWithAutoId();
            _tokenizer.ReadToken().Expect("FROM");
        }
        else
        {
            from.Expect("FROM");
        }

        var collection = ParseCollection(_tokenizer);

        var ahead = _tokenizer.LookAhead().Expect(TokenType.Word, TokenType.EOF, TokenType.SemiColon);

        if (ahead.Is("INCLUDE"))
        {
            _tokenizer.ReadToken();
            foreach (var path in ParseListOfExpressions()) query.Includes.Add(path);
        }

        ahead = _tokenizer.LookAhead().Expect(TokenType.Word, TokenType.EOF, TokenType.SemiColon);

        if (ahead.Is("WHERE"))
        {
            _tokenizer.ReadToken();
            query.Where.Add(BsonExpression.Create(_tokenizer, BsonExpressionParserMode.Full, _parameters));
        }

        ahead = _tokenizer.LookAhead().Expect(TokenType.Word, TokenType.EOF, TokenType.SemiColon);

        if (ahead.Is("GROUP"))
        {
            _tokenizer.ReadToken();
            _tokenizer.ReadToken().Expect("BY");
            query.GroupBy = BsonExpression.Create(_tokenizer, BsonExpressionParserMode.Full, _parameters);

            ahead = _tokenizer.LookAhead().Expect(TokenType.Word, TokenType.EOF, TokenType.SemiColon);

            if (ahead.Is("HAVING"))
            {
                _tokenizer.ReadToken();
                query.Having = BsonExpression.Create(_tokenizer, BsonExpressionParserMode.Full, _parameters);
            }
        }

        ahead = _tokenizer.LookAhead().Expect(TokenType.Word, TokenType.EOF, TokenType.SemiColon);

        if (ahead.Is("ORDER"))
        {
            _tokenizer.ReadToken();
            _tokenizer.ReadToken().Expect("BY");
            query.OrderBy = BsonExpression.Create(_tokenizer, BsonExpressionParserMode.Full, _parameters);

            var orderByOrder = Query.Ascending;
            var orderByToken = _tokenizer.LookAhead();

            if (orderByToken.Is("ASC") || orderByToken.Is("DESC"))
            {
                orderByOrder = _tokenizer.ReadToken().Is("ASC") ? Query.Ascending : Query.Descending;
            }

            query.Order = orderByOrder;
        }

        ahead = _tokenizer.LookAhead().Expect(TokenType.Word, TokenType.EOF, TokenType.SemiColon);
        if (ahead.Is("LIMIT"))  { _tokenizer.ReadToken(); query.Limit  = System.Convert.ToInt32(_tokenizer.ReadToken().Expect(TokenType.Int).Value); }

        ahead = _tokenizer.LookAhead().Expect(TokenType.Word, TokenType.EOF, TokenType.SemiColon);
        if (ahead.Is("OFFSET")) { _tokenizer.ReadToken(); query.Offset = System.Convert.ToInt32(_tokenizer.ReadToken().Expect(TokenType.Int).Value); }

        ahead = _tokenizer.LookAhead().Expect(TokenType.Word, TokenType.EOF, TokenType.SemiColon);
        if (ahead.Is("FOR"))    { _tokenizer.ReadToken(); _tokenizer.ReadToken().Expect("UPDATE"); query.ForUpdate = true; }

        _tokenizer.ReadToken().Expect(TokenType.EOF, TokenType.SemiColon);

        var stream = _engine.Query(collection, query, cancellationToken);
        return new BsonDataReader(stream, collection, cancellationToken);
    }

    /// <summary>
    /// Read collection name and parameter (in case of system collections)
    /// </summary>
    public static string ParseCollection(Tokenizer tokenizer)
    {
        return ParseCollection(tokenizer, out var name, out var options);
    }

    /// <summary>
    /// Read collection name and parameter (in case of system collections)
    /// </summary>
    public static string ParseCollection(Tokenizer tokenizer, out string name, out BsonValue options)
    {
        name = tokenizer.ReadToken().Expect(TokenType.Word).Value;

        // if collection starts with $, check if exist any parameter
        if (name.StartsWith("$"))
        {
            var next = tokenizer.LookAhead();

            if (next.Type == TokenType.OpenParenthesis)
            {
                tokenizer.ReadToken(); // read (

                if (tokenizer.LookAhead().Type == TokenType.CloseParenthesis)
                {
                    options = null;
                }
                else
                {
                    options = new JsonReader(tokenizer).Deserialize();
                }

                tokenizer.ReadToken().Expect(TokenType.CloseParenthesis); // read )
            }
            else
            {
                options = null;
            }
        }
        else
        {
            options = null;
        }

        return name + (options == null ? "" : "(" + JsonSerializer.Serialize(options) + ")");
    }
}