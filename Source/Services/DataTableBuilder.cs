using System.Data;
using TraceTiger.Models;

namespace TraceTiger.Services;

/// <summary>Turns a <see cref="QueryResultTable"/> into a <see cref="DataView"/> for the WPF DataGrid
/// (auto-generates typed, sortable columns). Null cells become <see cref="DBNull"/>;
/// <see cref="DateTimeOffset"/> is shown in local time.</summary>
internal static class DataTableBuilder
{
    public static DataView Build(QueryResultTable table)
    {
        var dt = new DataTable();
        var colTypes = new Type[table.Columns.Count];

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < table.Columns.Count; i++)
        {
            var c = table.Columns[i];
            colTypes[i] = GridType(c.ClrType);

            // De-duplicate column names (KQL can project two columns with the same name).
            var name = c.Name;
            var n = 1;
            while (!used.Add(name)) name = $"{c.Name}_{n++}";
            dt.Columns.Add(new DataColumn(name, colTypes[i]));
        }

        foreach (var row in table.Rows)
        {
            var values = new object[dt.Columns.Count];
            for (var i = 0; i < dt.Columns.Count; i++)
                values[i] = Convert(i < row.Length ? row[i] : null, colTypes[i]);
            dt.Rows.Add(values);
        }

        return dt.DefaultView;
    }

    // DataColumn copes poorly with DateTimeOffset and Nullable<T>; pick a safe display type per column.
    private static Type GridType(Type clr)
    {
        clr = Nullable.GetUnderlyingType(clr) ?? clr;
        if (clr == typeof(bool) || clr == typeof(int) || clr == typeof(long)
            || clr == typeof(double) || clr == typeof(decimal)) return clr;
        if (clr == typeof(DateTimeOffset) || clr == typeof(DateTime)) return typeof(DateTime);
        return typeof(string);
    }

    private static object Convert(object? value, Type gridType)
    {
        if (value is null) return DBNull.Value;
        if (gridType == typeof(DateTime))
            return value is DateTimeOffset dto ? dto.LocalDateTime : value;
        if (gridType == typeof(string))
            return value as string ?? value.ToString() ?? string.Empty;
        return value;
    }
}
