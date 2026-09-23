using System.Text.RegularExpressions;
using ProgramMigrationAnalyzer.Core;

namespace ProgramMigrationAnalyzer.Parsers;

internal static partial class SqlParsing
{
    [GeneratedRegex(@"\b(SELECT|INSERT|UPDATE|DELETE)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CommandRegex();

    [GeneratedRegex(@"\b(?:FROM|JOIN)\s+([A-Za-z_][\w.$]*)", RegexOptions.IgnoreCase)]
    private static partial Regex ReadTableRegex();

    [GeneratedRegex(@"\bINSERT\s+INTO\s+([A-Za-z_][\w.$]*)", RegexOptions.IgnoreCase)]
    private static partial Regex InsertTableRegex();

    [GeneratedRegex(@"\bUPDATE\s+([A-Za-z_][\w.$]*)", RegexOptions.IgnoreCase)]
    private static partial Regex UpdateTableRegex();

    public static bool LooksLikeSql(string text) => CommandRegex().IsMatch(text);

    public static SqlStatementInfo Create(string sql, int line)
    {
        var normalized = Regex.Replace(sql.Trim(), @"\s+", " ");
        var command = CommandRegex().Match(normalized).Groups[1].Value.ToUpperInvariant();
        var result = new SqlStatementInfo
        {
            CommandType = command,
            RawSql = normalized,
            StartLine = line,
            Risk = command == "SELECT" ? "Read" : "Write"
        };

        foreach (Match match in ReadTableRegex().Matches(normalized))
        {
            AddUnique(result.Tables, match.Groups[1].Value.TrimEnd(',', ';'));
        }

        var writeTableMatch = command switch
        {
            "INSERT" => InsertTableRegex().Match(normalized),
            "UPDATE" => UpdateTableRegex().Match(normalized),
            _ => Match.Empty
        };
        if (writeTableMatch.Success)
        {
            AddUnique(result.Tables, writeTableMatch.Groups[1].Value.TrimEnd(',', ';'));
        }

        return result;
    }

    public static void AddUnique(ICollection<string> target, string value)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            !target.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            target.Add(value);
        }
    }
}
