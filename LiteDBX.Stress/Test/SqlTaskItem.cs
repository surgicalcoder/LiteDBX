using System;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;

namespace LiteDbX.Stress;

public class SqlTaskItem : ITestItem
{
    public SqlTaskItem(XmlElement el)
    {
        Name = string.IsNullOrEmpty(el.GetAttribute("name")) ? el.InnerText.Split(' ').First() : el.GetAttribute("name");
        TaskCount = string.IsNullOrEmpty(el.GetAttribute("tasks")) ? 1 : int.Parse(el.GetAttribute("tasks"));
        Sleep = TimeSpanEx.Parse(el.GetAttribute("sleep"));
        Sql = el.InnerText;
    }

    public string Sql { get; }
    public string Name { get; }
    public int TaskCount { get; }
    public TimeSpan Sleep { get; }

    public async ValueTask<BsonValue> Execute(LiteDatabase db)
    {
        await using var reader = await db.Execute(Sql).ConfigureAwait(false);
        return await reader.FirstOrDefault().ConfigureAwait(false);
    }
}