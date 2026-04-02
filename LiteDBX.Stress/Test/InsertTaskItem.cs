using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;

namespace LiteDbX.Stress;

public class InsertTaskItem : ITestItem
{
    private readonly Random _rnd = new();

    private ILiteCollection<BsonDocument> _collection;

    public InsertTaskItem(XmlElement el)
    {
        Name = string.IsNullOrEmpty(el.GetAttribute("name")) ? "INSERT_" + el.GetAttribute("collection").ToUpper() : el.GetAttribute("name");
        Sleep = string.IsNullOrEmpty(el.GetAttribute("sleep")) ? TimeSpan.FromSeconds(1) : TimeSpanEx.Parse(el.GetAttribute("sleep"));
        AutoId = string.IsNullOrEmpty(el.GetAttribute("autoId")) ? BsonAutoId.ObjectId : (BsonAutoId)Enum.Parse(typeof(BsonAutoId), el.GetAttribute("autoId"), true);
        Collection = el.GetAttribute("collection");
        TaskCount = string.IsNullOrEmpty(el.GetAttribute("tasks")) ? 1 : int.Parse(el.GetAttribute("tasks"));
        MinRange = string.IsNullOrEmpty(el.GetAttribute("docs")) ? 1 : int.Parse(el.GetAttribute("docs").Split('~').First());
        MaxRange = string.IsNullOrEmpty(el.GetAttribute("docs")) ? 1 : int.Parse(el.GetAttribute("docs").Split('~').Last());

        Fields = new List<InsertField>();

        foreach (XmlElement child in el.SelectNodes("*"))
        {
            Fields.Add(new InsertField(child));
        }
    }

    public string Collection { get; }
    public BsonAutoId AutoId { get; }
    public int MinRange { get; }
    public int MaxRange { get; }
    public List<InsertField> Fields { get; }

    public string Name { get; }
    public int TaskCount { get; }
    public TimeSpan Sleep { get; }

    public async ValueTask<BsonValue> Execute(LiteDatabase db)
    {
        _collection ??= db.GetCollection(Collection, AutoId);

        var count = _rnd.Next(MinRange, MaxRange);

        for (var i = 0; i < count; i++)
        {
            var doc = new BsonDocument();

            foreach (var field in Fields)
            {
                doc[field.Name] = field.GetValue();
            }

            await _collection.Insert(doc).ConfigureAwait(false);
        }

        return count;
    }
}