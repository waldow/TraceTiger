namespace TraceTiger.Models;

/// <summary>A column in a <see cref="QueryResultTable"/> — name plus the CLR type its cells hold.</summary>
public sealed class QueryColumn
{
    public required string Name { get; init; }
    public required Type ClrType { get; init; }
}

/// <summary>
/// A provider-agnostic result of running a KQL query. The Azure layer maps a <c>LogsTable</c>
/// into this shape so grid/chart/json projection never depends on the Azure SDK.
/// </summary>
public sealed class QueryResultTable
{
    public required IReadOnlyList<QueryColumn> Columns { get; init; }

    /// <summary>Rows; each is a cell array aligned to <see cref="Columns"/> (cells may be null).</summary>
    public required IReadOnlyList<object?[]> Rows { get; init; }

    public int RowCount => Rows.Count;

    public static QueryResultTable Empty { get; } = new()
    {
        Columns = Array.Empty<QueryColumn>(),
        Rows = Array.Empty<object?[]>(),
    };

    public int IndexOfColumn(string name)
    {
        for (var i = 0; i < Columns.Count; i++)
        {
            if (string.Equals(Columns[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    public bool IsNumeric(int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= Columns.Count) return false;
        var t = Columns[columnIndex].ClrType;
        return t == typeof(long) || t == typeof(int) || t == typeof(double)
            || t == typeof(decimal) || t == typeof(float) || t == typeof(short);
    }

    public bool IsDateTime(int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= Columns.Count) return false;
        var t = Columns[columnIndex].ClrType;
        return t == typeof(DateTimeOffset) || t == typeof(DateTime);
    }
}
