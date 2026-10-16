using System.Text.RegularExpressions;
using GarageStack.Core.Models;
using GarageStack.Data.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GarageStack.Tests;

// The statistics only read rows carrying one of TelemetryHistoryPoint's fields, and a partial
// index written in raw SQL (the AddChartIndex migration) keeps that read cheap. Nothing but this
// test ties the two together: a field added to the record and not to the index would silently
// stop PostgreSQL from using the index, and the statistics would read every row again.
public partial class ChartIndexTests
{
    [GeneratedRegex(@"""(\w+)""\s+IS NOT NULL")]
    private static partial Regex IndexedField();

    [Fact]
    public void ThePartialIndex_CoversExactlyTheFieldsTheStatisticsRead()
    {
        var sql = string.Concat(new AddChartIndex().UpOperations.OfType<SqlOperation>().Select(o => o.Sql));
        var indexed = IndexedField().Matches(sql).Select(m => m.Groups[1].Value).Order().ToList();

        var read = typeof(TelemetryHistoryPoint).GetConstructors().Single().GetParameters()
            .Select(p => p.Name!)
            .Where(name => name != nameof(TelemetryHistoryPoint.RecordedAt))
            .Order()
            .ToList();

        Assert.NotEmpty(indexed);
        Assert.Equal(read, indexed);
    }
}
