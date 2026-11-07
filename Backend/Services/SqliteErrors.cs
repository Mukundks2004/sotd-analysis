using Microsoft.Data.Sqlite;

namespace Sotd.Backend.Services;

/// <summary>Turns SQLite's terse constraint text into something a person can act on.</summary>
public static class SqliteErrors
{
    public static string Describe(SqliteException ex)
    {
        var message = ex.Message;
        if (message.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase))
            return "That would break a foreign key: one of the IDs does not point at an existing row, or another table still refers to this row.";
        if (message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase))
            return $"That value is already taken: {Between(message, "constraint failed: ") ?? "a unique column"} must be unique.";
        if (message.Contains("NOT NULL", StringComparison.OrdinalIgnoreCase))
            return $"A required value is missing: {Between(message, "constraint failed: ") ?? "a column"} cannot be empty.";
        return message;
    }

    /// <summary>The annotation screen only ever links rows, so its wording is about the link, not the column.</summary>
    public static string DescribeLink(SqliteException ex) =>
        ex.Message.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase)
            ? "That would break a foreign key: one of the rows involved no longer exists."
            : ex.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
                ? "That name is already taken."
                : ex.Message;

    static string? Between(string text, string after)
    {
        var index = text.IndexOf(after, StringComparison.Ordinal);
        if (index < 0) return null;
        var rest = text[(index + after.Length)..].Trim();
        var end = rest.IndexOfAny(new[] { '\r', '\n', '\'' });
        return (end < 0 ? rest : rest[..end]).TrimEnd('.', ' ');
    }
}
