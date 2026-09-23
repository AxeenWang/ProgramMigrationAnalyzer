using System.Text.RegularExpressions;
using ProgramMigrationAnalyzer.Core;

namespace ProgramMigrationAnalyzer.Parsers;

public sealed partial class Informix4GlParser : ISourceParser
{
    public bool CanParse(SourceDocument source)
    {
        var extension = Path.GetExtension(source.FileName);
        return source.Language switch
        {
            SourceLanguage.Informix4Gl => true,
            SourceLanguage.CSharp => false,
            _ => extension.Equals(".4gl", StringComparison.OrdinalIgnoreCase) ||
                 extension.Equals(".per", StringComparison.OrdinalIgnoreCase)
        };
    }

    public Task<AnalysisResult> ParseAsync(
        SourceDocument source,
        CancellationToken cancellationToken = default)
    {
        var result = new AnalysisResult
        {
            Source = source,
            Language = SourceLanguage.Informix4Gl
        };

        var lines = source.Content.ReplaceLineEndings("\n").Split('\n');
        FunctionBuilder? current = null;

        for (var index = 0; index < lines.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lineNumber = index + 1;
            var line = StripComment(lines[index]).Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var functionMatch = FunctionRegex().Match(line);
            if (functionMatch.Success)
            {
                FinishCurrent(result, current, lineNumber - 1);
                current = new FunctionBuilder(functionMatch.Groups[1].Value, "Function", lineNumber);
                foreach (var name in functionMatch.Groups[2].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    current.Parameters.Add(new ParameterInfo { Name = name, Type = "object?" });
                }

                continue;
            }

            var reportMatch = ReportStartRegex().Match(line);
            if (reportMatch.Success)
            {
                FinishCurrent(result, current, lineNumber - 1);
                current = new FunctionBuilder(reportMatch.Groups[1].Value, "Report", lineNumber);
                foreach (var name in reportMatch.Groups[2].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    current.Parameters.Add(new ParameterInfo { Name = name, Type = "object?" });
                }

                AddDependency(result, "4GL Report", "Report", "Legacy report behavior detected.", "High");
                AddTag(result, "Architecture", "Report");
                result.DetectedBehaviors.Add($"REPORT at line {lineNumber}");
                continue;
            }

            if (MainRegex().IsMatch(line))
            {
                FinishCurrent(result, current, lineNumber - 1);
                current = new FunctionBuilder("MAIN", "Main", lineNumber);
                continue;
            }

            if (EndFunctionRegex().IsMatch(line) || EndMainRegex().IsMatch(line) || EndReportRegex().IsMatch(line))
            {
                FinishCurrent(result, current, lineNumber);
                current = null;
                continue;
            }

            var defineMatch = DefineRegex().Match(line);
            if (defineMatch.Success && current is not null)
            {
                current.LocalDefinitions.Add(defineMatch.Groups[1].Value.Trim());
            }

            var callMatch = CallRegex().Match(line);
            if (callMatch.Success && current is not null)
            {
                SqlParsing.AddUnique(current.CalledFunctions, callMatch.Groups[1].Value);
            }

            if (SqlParsing.LooksLikeSql(line))
            {
                var sql = SqlParsing.Create(line, lineNumber);
                result.SqlStatements.Add(sql);
                if (current is not null)
                {
                    foreach (var table in sql.Tables)
                    {
                        SqlParsing.AddUnique(current.ReferencedTables, table);
                    }
                }
            }

            DetectDependenciesAndTags(line, lineNumber, result);
            DetectControlFlow(line, lineNumber, result);
        }

        FinishCurrent(result, current, lines.Length);

        if (source.FileName.Contains("batch", StringComparison.OrdinalIgnoreCase) ||
            source.Content.Contains("settlement", StringComparison.OrdinalIgnoreCase))
        {
            AddTag(result, "Architecture", "Batch");
        }

        if (result.ProgramUnits.Count == 0 && !string.IsNullOrWhiteSpace(source.Content))
        {
            result.MigrationIssues.Add(new MigrationIssue
            {
                Code = "4GL-STRUCTURE",
                Title = "No MAIN or FUNCTION block detected",
                Description = "The source could not be mapped to a known Informix 4GL program unit.",
                Severity = IssueSeverity.Medium,
                SuggestedAction = "Review the dialect or extend the 4GL parser rules.",
                Location = source.FileName
            });
        }

        result.Summary = $"Detected {result.ProgramUnits.Count} program unit(s), " +
                         $"{result.Functions.Count} function(s), and " +
                         $"{result.SqlStatements.Count} SQL statement(s).";

        return Task.FromResult(result);
    }

    private static void FinishCurrent(AnalysisResult result, FunctionBuilder? current, int endLine)
    {
        if (current is null)
        {
            return;
        }

        var function = new FunctionInfo
        {
            Name = current.Name,
            Kind = current.Kind,
            ReturnType = current.Kind is "Main" or "Report" ? "void" : "object?",
            Summary = current.Kind switch
            {
                "Main" => "4GL application entry point.",
                "Report" => "Informix 4GL report definition.",
                _ => "Informix 4GL function."
            },
            StartLine = current.StartLine,
            EndLine = Math.Max(current.StartLine, endLine)
        };
        function.Parameters.AddRange(current.Parameters);
        function.CalledFunctions.AddRange(current.CalledFunctions);
        function.ReferencedTables.AddRange(current.ReferencedTables);
        result.Functions.Add(function);

        var unit = new ProgramUnit
        {
            Name = current.Name,
            Kind = current.Kind,
            StartLine = current.StartLine,
            EndLine = Math.Max(current.StartLine, endLine)
        };
        unit.Members.AddRange(current.LocalDefinitions.Select(definition => $"DEFINE: {definition}"));
        result.ProgramUnits.Add(unit);
    }

    private static void DetectDependenciesAndTags(string line, int lineNumber, AnalysisResult result)
    {
        if (DatabaseRegex().IsMatch(line))
        {
            AddDependency(result, "Informix Database", "Database", "DATABASE statement or database access detected.", "High");
        }

        if (DisplayInputRegex().IsMatch(line))
        {
            AddDependency(result, "4GL Screen Interaction", "UI", "DISPLAY or INPUT terminal UI behavior detected.", "High");
            AddTag(result, "Architecture", "UI");
        }

        if (TransactionRegex().IsMatch(line))
        {
            AddDependency(result, "Transaction Control", "Database", "Explicit transaction behavior detected.", "Medium");
            AddTag(result, "Database", "Transaction");
        }

        if (CursorRegex().IsMatch(line))
        {
            AddDependency(result, "Database Cursor", "Database", "Cursor-based record processing detected.", "Medium");
            AddTag(result, "Database", "Cursor");
        }

        if (GlobalsRegex().IsMatch(line))
        {
            result.MigrationIssues.Add(new MigrationIssue
            {
                Code = "4GL-GLOBAL",
                Title = "Global state detected",
                Description = "GLOBALS can introduce hidden coupling and should be redesigned.",
                Severity = IssueSeverity.Medium,
                SuggestedAction = "Move shared state to explicit services or immutable request models.",
                Location = $"Line {lineNumber}"
            });
            AddTag(result, "Migration", "Refactor Required");
        }

        if (ReportRegex().IsMatch(line))
        {
            AddDependency(result, "4GL Report", "Report", "Legacy report behavior detected.", "High");
            AddTag(result, "Architecture", "Report");
        }
    }

    private static void DetectControlFlow(string line, int lineNumber, AnalysisResult result)
    {
        var match = ControlFlowRegex().Match(line);
        if (match.Success)
        {
            result.DetectedBehaviors.Add($"{match.Groups[1].Value.ToUpperInvariant()} at line {lineNumber}");
        }
    }

    private static void AddDependency(AnalysisResult result, string name, string category, string description, string risk)
    {
        if (result.Dependencies.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        result.Dependencies.Add(new DependencyInfo
        {
            Name = name,
            Category = category,
            Description = description,
            Risk = risk
        });
    }

    private static void AddTag(AnalysisResult result, string category, string name)
    {
        if (!result.Tags.Any(tag => tag.Category == category && tag.Name == name))
        {
            result.Tags.Add(new AnalysisTag(category, name));
        }
    }

    private static string StripComment(string line)
    {
        var index = line.IndexOf('#');
        return index >= 0 ? line[..index] : line;
    }

    private sealed class FunctionBuilder(string name, string kind, int startLine)
    {
        public string Name { get; } = name;
        public string Kind { get; } = kind;
        public int StartLine { get; } = startLine;
        public List<ParameterInfo> Parameters { get; } = [];
        public List<string> CalledFunctions { get; } = [];
        public List<string> ReferencedTables { get; } = [];
        public List<string> LocalDefinitions { get; } = [];
    }

    [GeneratedRegex(@"^\s*FUNCTION\s+([A-Za-z_][\w]*)\s*(?:\(([^)]*)\))?", RegexOptions.IgnoreCase)]
    private static partial Regex FunctionRegex();

    [GeneratedRegex(@"^\s*MAIN\b", RegexOptions.IgnoreCase)]
    private static partial Regex MainRegex();

    [GeneratedRegex(@"^\s*END\s+FUNCTION\b", RegexOptions.IgnoreCase)]
    private static partial Regex EndFunctionRegex();

    [GeneratedRegex(@"^\s*END\s+MAIN\b", RegexOptions.IgnoreCase)]
    private static partial Regex EndMainRegex();

    [GeneratedRegex(@"^\s*REPORT\s+([A-Za-z_][\w]*)\s*(?:\(([^)]*)\))?", RegexOptions.IgnoreCase)]
    private static partial Regex ReportStartRegex();

    [GeneratedRegex(@"^\s*END\s+REPORT\b", RegexOptions.IgnoreCase)]
    private static partial Regex EndReportRegex();

    [GeneratedRegex(@"^\s*DEFINE\s+(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex DefineRegex();

    [GeneratedRegex(@"\bCALL\s+([A-Za-z_][\w]*)", RegexOptions.IgnoreCase)]
    private static partial Regex CallRegex();

    [GeneratedRegex(@"\bDATABASE\b", RegexOptions.IgnoreCase)]
    private static partial Regex DatabaseRegex();

    [GeneratedRegex(@"\b(DISPLAY|INPUT)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DisplayInputRegex();

    [GeneratedRegex(@"\b(BEGIN\s+WORK|COMMIT\s+WORK|ROLLBACK\s+WORK)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TransactionRegex();

    [GeneratedRegex(@"\b(DECLARE|CURSOR|FOREACH)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CursorRegex();

    [GeneratedRegex(@"^\s*GLOBALS\b", RegexOptions.IgnoreCase)]
    private static partial Regex GlobalsRegex();

    [GeneratedRegex(@"\bREPORT\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReportRegex();

    [GeneratedRegex(@"^\s*((?:END\s+)?(?:IF|FOR|WHILE)|ELSE|RETURN)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ControlFlowRegex();
}
