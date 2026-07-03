using System.Globalization;
using LiveChartsCore;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using TraceTiger.Models;

namespace TraceTiger.Services;

/// <summary>A projected chart ready to bind to a LiveCharts <c>CartesianChart</c>.</summary>
public sealed class ChartModel
{
    public bool Ok { get; init; }
    public ISeries[] Series { get; init; } = Array.Empty<ISeries>();
    public Axis[] XAxes { get; init; } = Array.Empty<Axis>();
    public Axis[] YAxes { get; init; } = Array.Empty<Axis>();
    public DrawMarginFrame? DrawMarginFrame { get; init; }
    public FindingStrategy FindingStrategy { get; init; } = FindingStrategy.CompareOnlyX;
    public TooltipPosition TooltipPosition { get; init; } = TooltipPosition.Top;
    public SolidColorPaint TooltipTextPaint { get; init; } = new(new SKColor(0xE6, 0xED, 0xF3));
    public SolidColorPaint TooltipBackgroundPaint { get; init; } = new(new SKColor(0x16, 0x1B, 0x22));
    public double TooltipTextSize { get; init; } = 11;
    public SolidColorPaint LegendTextPaint { get; init; } = new(new SKColor(0x8B, 0x94, 0x9E));
}

/// <summary>
/// Projects a <see cref="QueryResultTable"/> into LiveCharts series. Typical KQL chart results are
/// <c>summarize &lt;value&gt; by &lt;category?&gt;, bin(timestamp,…)</c> shapes, so the rule is:
/// a datetime column is the X axis; string columns split into one series each; numeric columns
/// supply the Y values. When no datetime is present it falls back to a single categorical chart.
/// Returns <c>Ok=false</c> if it can't make sense of the shape (the pane then shows a hint instead).
/// </summary>
public static class ChartProjector
{
    private static readonly SKColor AxisColor = new(0x8B, 0x94, 0x9E);
    private static readonly SKColor GridColor = new(0x30, 0x36, 0x3D, 0x80);
    private static readonly SKColor[] Palette =
    {
        new(0x58, 0xA6, 0xFF),
        new(0x3F, 0xB9, 0x50),
        new(0xF8, 0x51, 0x49),
        new(0xD2, 0x99, 0x22),
        new(0xBC, 0x8C, 0xFF),
        new(0xF0, 0x88, 0x3E),
    };

    public static ChartModel Project(QueryResultTable table, ChartKind kind)
    {
        if (table.RowCount == 0) return new ChartModel { Ok = false };

        var dateCol = FirstIndex(table, table.IsDateTime);
        return dateCol >= 0
            ? ProjectTimeSeries(table, kind, dateCol)
            : ProjectCategorical(table);
    }

    private static ChartModel ProjectTimeSeries(QueryResultTable table, ChartKind kind, int dateCol)
    {
        var numericCols = AllIndexes(table, table.IsNumeric);
        if (numericCols.Count == 0) return new ChartModel { Ok = false };

        var catCols = AllIndexes(table, i => i != dateCol && !table.IsNumeric(i) && !table.IsDateTime(i));

        // Shared, sorted X axis of distinct timestamps.
        var keys = table.Rows
            .Select(r => AsDateTime(r, dateCol))
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .Distinct()
            .OrderBy(d => d)
            .ToList();
        if (keys.Count == 0) return new ChartModel { Ok = false };

        var keyIndex = keys.Select((k, i) => (k, i)).ToDictionary(p => p.k, p => p.i);
        var series = new List<ISeries>();

        if (catCols.Count >= 1)
        {
            // One series per category tuple and numeric value column.
            var groups = new Dictionary<string, double?[]>();

            foreach (var r in table.Rows)
            {
                var d = AsDateTime(r, dateCol);
                if (d is null || !keyIndex.TryGetValue(d.Value, out var xi)) continue;

                var cat = CategoryLabel(r, catCols);
                foreach (var valueCol in numericCols)
                {
                    var name = numericCols.Count == 1
                        ? cat
                        : $"{cat} · {table.Columns[valueCol].Name}";
                    if (!groups.TryGetValue(name, out var arr))
                    {
                        arr = new double?[keys.Count];
                        groups[name] = arr;
                    }
                    arr[xi] = AsDouble(r, valueCol);
                }
            }

            foreach (var (name, values) in groups.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
                series.Add(MakeSeries(kind, name, values, series.Count));
        }
        else
        {
            // No category column: one series per numeric column (e.g. total vs failed).
            foreach (var vc in numericCols)
            {
                var values = new double?[keys.Count];
                foreach (var r in table.Rows)
                {
                    var d = AsDateTime(r, dateCol);
                    if (d is null || !keyIndex.TryGetValue(d.Value, out var xi)) continue;
                    values[xi] = AsDouble(r, vc);
                }
                series.Add(MakeSeries(kind, table.Columns[vc].Name, values, series.Count));
            }
        }

        var labels = keys.Select(k => k.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture)).ToArray();
        return new ChartModel
        {
            Ok = true,
            Series = series.ToArray(),
            XAxes = new[] { MakeXAxis(labels) },
            YAxes = new[] { MakeYAxis() },
            DrawMarginFrame = MakeDrawMarginFrame(),
        };
    }

    private static ChartModel ProjectCategorical(QueryResultTable table)
    {
        var catCol = FirstIndex(table, i => !table.IsNumeric(i) && !table.IsDateTime(i));
        var valueCol = FirstIndex(table, table.IsNumeric);
        if (catCol < 0 || valueCol < 0) return new ChartModel { Ok = false };

        var labels = new List<string>();
        var values = new List<double?>();
        foreach (var r in table.Rows)
        {
            labels.Add(CellText(r, catCol));
            values.Add(AsDouble(r, valueCol));
        }

        // A categorical view reads best as columns regardless of the requested kind.
        var series = new ISeries[] { MakeSeries(ChartKind.Column, table.Columns[valueCol].Name, values.ToArray(), 0) };
        return new ChartModel
        {
            Ok = true,
            Series = series,
            XAxes = new[] { MakeXAxis(labels.ToArray()) },
            YAxes = new[] { MakeYAxis() },
            DrawMarginFrame = MakeDrawMarginFrame(),
        };
    }

    private static ISeries MakeSeries(ChartKind kind, string name, double?[] values, int index)
    {
        var color = Palette[index % Palette.Length];
        return kind == ChartKind.Column
            ? new ColumnSeries<double?>
            {
                Name = name,
                Values = values,
                Fill = Paint(color.WithAlpha(0xCC)),
                Stroke = Paint(color, 1.5f),
            }
            : new LineSeries<double?>
            {
                Name = name,
                Values = values,
                Stroke = Paint(color, 2.5f),
                Fill = null,
                GeometryFill = Paint(color),
                GeometryStroke = Paint(color, 1.5f),
                GeometrySize = 8,
                LineSmoothness = 0.2,
            };
    }

    private static Axis MakeXAxis(string[] labels) => new()
    {
        Labels = labels,
        LabelsRotation = 30,
        TextSize = 11,
        LabelsPaint = Paint(AxisColor),
        NamePaint = Paint(AxisColor),
        SeparatorsPaint = Paint(GridColor),
    };

    private static Axis MakeYAxis() => new()
    {
        TextSize = 11,
        LabelsPaint = Paint(AxisColor),
        NamePaint = Paint(AxisColor),
        SeparatorsPaint = Paint(GridColor),
        MinLimit = 0,
    };

    private static SolidColorPaint Paint(SKColor color, float strokeThickness = 1) =>
        new(color) { StrokeThickness = strokeThickness };

    private static DrawMarginFrame MakeDrawMarginFrame() => new()
    {
        Fill = Paint(new SKColor(0x16, 0x1B, 0x22)),
        Stroke = Paint(new SKColor(0x30, 0x36, 0x3D)),
    };

    private static int FirstIndex(QueryResultTable t, Func<int, bool> pred)
    {
        for (var i = 0; i < t.Columns.Count; i++) if (pred(i)) return i;
        return -1;
    }

    private static List<int> AllIndexes(QueryResultTable t, Func<int, bool> pred)
    {
        var list = new List<int>();
        for (var i = 0; i < t.Columns.Count; i++) if (pred(i)) list.Add(i);
        return list;
    }

    private static DateTime? AsDateTime(object?[] row, int i)
    {
        if (i >= row.Length) return null;
        return row[i] switch
        {
            DateTimeOffset dto => dto.LocalDateTime,
            DateTime dt => dt,
            _ => null,
        };
    }

    private static double? AsDouble(object?[] row, int i)
    {
        if (i >= row.Length || row[i] is null) return null;
        return row[i] switch
        {
            double d => d,
            long l => l,
            int n => n,
            decimal m => (double)m,
            float f => f,
            _ => double.TryParse(row[i]!.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null,
        };
    }

    private static string CellText(object?[] row, int i) =>
        i < row.Length ? row[i]?.ToString() ?? "" : "";

    private static string CategoryLabel(object?[] row, IReadOnlyList<int> catCols)
    {
        // Long values (dynamic/JSON columns) make unreadable series names — clip them.
        var parts = catCols
            .Select(i => CellText(row, i))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Length <= 32 ? s : s[..29] + "…");
        var label = string.Join(" · ", parts);
        return string.IsNullOrWhiteSpace(label) ? "(blank)" : label;
    }
}
