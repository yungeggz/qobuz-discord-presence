using Microsoft.Data.Sqlite;

namespace QobuzPresence.Helpers;

internal static class SqliteDataReaderHelper
{
    public static string? GetString(SqliteDataReader reader, string columnName)
    {
        int ordinal = TryGetOrdinal(reader, columnName);

        if (ordinal < 0 || reader.IsDBNull(ordinal))
        {
            return null;
        }

        return Convert.ToString(reader.GetValue(ordinal));
    }

    public static int? GetInt32(SqliteDataReader reader, string columnName)
    {
        int ordinal = TryGetOrdinal(reader, columnName);

        if (ordinal < 0 || reader.IsDBNull(ordinal))
        {
            return null;
        }

        return Convert.ToInt32(reader.GetValue(ordinal));
    }

    public static long? GetInt64(SqliteDataReader reader, string columnName)
    {
        int ordinal = TryGetOrdinal(reader, columnName);

        if (ordinal < 0 || reader.IsDBNull(ordinal))
        {
            return null;
        }

        return Convert.ToInt64(reader.GetValue(ordinal));
    }

    public static double? GetDouble(SqliteDataReader reader, string columnName)
    {
        int ordinal = TryGetOrdinal(reader, columnName);

        if (ordinal < 0 || reader.IsDBNull(ordinal))
        {
            return null;
        }

        return Convert.ToDouble(reader.GetValue(ordinal));
    }

    private static int TryGetOrdinal(SqliteDataReader reader, string columnName)
    {
        for (int i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(reader.GetName(i), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}
