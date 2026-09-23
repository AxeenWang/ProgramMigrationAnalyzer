using System.Text.RegularExpressions;
using ProgramMigrationAnalyzer.Core;

namespace ProgramMigrationAnalyzer.Analysis;

public sealed class MigrationAnalyzer : ISourceAnalyzer
{
    private static readonly HashSet<string> DeprecatedApiIssueCodes =
    [
        "CS-CONFIG",
        "CS-WEBREQUEST",
        "CS-BINARY",
        "CS-SYSTEMWEB",
        "CS-DATASET",
        "CS-COM"
    ];

    private static readonly DetectionRule[] CSharpRules =
    [
        new("CS-CONFIG", "ConfigurationManager", @"\b(ConfigurationManager|System\.Configuration)\b", IssueSeverity.Medium,
            "Legacy configuration API detected.", "Replace with Microsoft.Extensions.Configuration and IConfiguration."),
        new("CS-WEBREQUEST", "WebRequest", @"\b(HttpWebRequest|WebRequest)\b", IssueSeverity.Medium,
            "Legacy synchronous web request API detected.", "Use HttpClient with asynchronous calls and centralized resilience policies."),
        new("CS-BINARY", "BinaryFormatter", @"\bBinaryFormatter\b", IssueSeverity.Critical,
            "BinaryFormatter is unsafe and unsupported for modern secure serialization.", "Do not port directly. Select a safe, explicit serialization format."),
        new("CS-SYSTEMWEB", "System.Web", @"\bSystem\.Web\b", IssueSeverity.High,
            "System.Web has no direct ASP.NET Core equivalent.", "Redesign the affected web behavior using ASP.NET Core abstractions."),
        new("CS-DATASET", "DataSet or DataTable", @"\b(DataSet|DataTable|SqlDataAdapter)\b", IssueSeverity.Low,
            "Legacy disconnected data containers detected.", "Evaluate typed DTOs with Dapper, EF Core, or a repository boundary."),
        new("CS-COM", "COM interop", @"\b(ComImport|Marshal\.GetActiveObject|Microsoft\.Office\.Interop)\b", IssueSeverity.High,
            "COM interop dependency detected.", "Validate bitness and deployment constraints, then isolate or replace the integration.")
    ];

    public Task<AnalysisResult> AnalyzeAsync(
        AnalysisResult result,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        AddTag(result, "Language", result.Language == SourceLanguage.CSharp ? "C#" : "4GL");

        if (result.Language == SourceLanguage.CSharp)
        {
            AnalyzeCSharp(result);
        }

        AnalyzeSql(result);
        AddBusinessTags(result);
        CalculateAssessment(result);
        return Task.FromResult(result);
    }

    private static void AnalyzeCSharp(AnalysisResult result)
    {
        foreach (var rule in CSharpRules)
        {
            var match = Regex.Match(result.Source.Content, rule.Pattern, RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                continue;
            }

            AddIssue(result, new MigrationIssue
            {
                Code = rule.Code,
                Title = rule.Title,
                Description = rule.Description,
                Severity = rule.Severity,
                SuggestedAction = rule.Action,
                Location = $"Line {GetLineNumber(result.Source.Content, match.Index)}"
            });
            AddTag(result, "Migration", rule.Severity >= IssueSeverity.High ? "High Risk" : "Refactor Required");
        }

        AddDependencyWhenMatched(result, @"\b(SqlConnection|SqlCommand|SqlDataAdapter)\b",
            "ADO.NET", "Database", "Direct ADO.NET data access detected.", "Medium", "Data Access");
        AddDependencyWhenMatched(result, @"\b(ConfigurationManager|System\.Configuration)\b",
            "Legacy Configuration", "Configuration", "System.Configuration dependency detected.", "Medium", "Configuration");
        AddDependencyWhenMatched(result, @"\b(HttpWebRequest|WebRequest|WebClient)\b",
            "Legacy Web Client", "Web Service", "Synchronous or legacy web-service access detected.", "Medium", "Web Service");
        AddDependencyWhenMatched(result, @"\b(File|Directory|StreamReader|StreamWriter)\b",
            "File System", "File System", "Local file-system access detected.", "Medium", "File System");
        AddDependencyWhenMatched(result, @"\b(XmlDocument|XDocument|XmlSerializer|XmlReader|XmlWriter)\b",
            "XML Processing", "XML", "XML parsing or serialization detected.", "Low", "XML");
        AddDependencyWhenMatched(result, @"\b(ComImport|Marshal\.GetActiveObject|Microsoft\.Office\.Interop)\b",
            "COM Interop", "COM", "COM integration detected.", "High", "COM");
    }

    private static void AnalyzeSql(AnalysisResult result)
    {
        if (result.SqlStatements.Count == 0)
        {
            return;
        }

        AddDependency(result, "Database Access", "Database", "Embedded SQL statements detected.", "Medium");
        AddTag(result, "Architecture", "Data Access");
        AddTag(result, "Language", "SQL");
        AddTag(result, "Dependency", "Database");

        foreach (var sql in result.SqlStatements)
        {
            AddTag(result, "Database", sql.CommandType);
        }
    }

    private static void AddBusinessTags(AnalysisResult result)
    {
        var source = result.Source.Content;
        var mappings = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Customer"] = ["customer", "客戶"],
            ["Order"] = ["order", "訂單"],
            ["Inventory"] = ["inventory", "warehouse", "庫存"],
            ["Invoice"] = ["invoice", "發票"],
            ["Settlement"] = ["settlement", "payment", "結算"],
            ["Validation"] = ["validate", "validation", "檢核"],
            ["Calculation"] = ["calculate", "calc_", "total", "計算"],
            ["Approval"] = ["approve", "approval", "核准"]
        };

        foreach (var mapping in mappings)
        {
            if (mapping.Value.Any(keyword => source.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                AddTag(result, "Business", mapping.Key);
            }
        }
    }

    private static void CalculateAssessment(AnalysisResult result)
    {
        var highCount = result.MigrationIssues.Count(issue => issue.Severity == IssueSeverity.High);
        var criticalCount = result.MigrationIssues.Count(issue => issue.Severity == IssueSeverity.Critical);
        var mediumCount = result.MigrationIssues.Count(issue => issue.Severity == IssueSeverity.Medium);
        var globalCount = result.MigrationIssues.Count(issue => issue.Code == "4GL-GLOBAL");

        var score = 100
                    - (highCount * 12)
                    - (criticalCount * 20)
                    - (mediumCount * 5)
                    - (result.Dependencies.Count * 2)
                    - (globalCount * 3);
        score = Math.Clamp(score, 0, 100);

        result.Assessment = new MigrationAssessment
        {
            FunctionCount = result.Functions.Count,
            SqlStatementCount = result.SqlStatements.Count,
            DependencyCount = result.Dependencies.Count,
            DeprecatedApiCount = result.MigrationIssues.Count(issue => DeprecatedApiIssueCodes.Contains(issue.Code)),
            ManualReviewCount = result.MigrationIssues.Count(issue => issue.Severity >= IssueSeverity.Medium),
            MigrationScore = score,
            RiskLevel = score switch
            {
                >= 80 => MigrationRiskLevel.Low,
                >= 60 => MigrationRiskLevel.Medium,
                >= 30 => MigrationRiskLevel.High,
                _ => MigrationRiskLevel.Critical
            }
        };
    }

    private static void AddDependencyWhenMatched(
        AnalysisResult result,
        string pattern,
        string name,
        string category,
        string description,
        string risk,
        string tag)
    {
        if (!Regex.IsMatch(result.Source.Content, pattern, RegexOptions.IgnoreCase))
        {
            return;
        }

        AddDependency(result, name, category, description, risk);
        AddTag(result, "Dependency", tag);
    }

    private static void AddDependency(AnalysisResult result, string name, string category, string description, string risk)
    {
        if (!result.Dependencies.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            result.Dependencies.Add(new DependencyInfo
            {
                Name = name,
                Category = category,
                Description = description,
                Risk = risk
            });
        }
    }

    private static void AddIssue(AnalysisResult result, MigrationIssue issue)
    {
        if (!result.MigrationIssues.Any(item => item.Code == issue.Code && item.Location == issue.Location))
        {
            result.MigrationIssues.Add(issue);
        }
    }

    private static void AddTag(AnalysisResult result, string category, string name)
    {
        if (!result.Tags.Any(tag => tag.Category == category && tag.Name == name))
        {
            result.Tags.Add(new AnalysisTag(category, name));
        }
    }

    private static int GetLineNumber(string content, int position) =>
        content.Take(position).Count(character => character == '\n') + 1;

    private sealed record DetectionRule(
        string Code,
        string Title,
        string Pattern,
        IssueSeverity Severity,
        string Description,
        string Action);
}
