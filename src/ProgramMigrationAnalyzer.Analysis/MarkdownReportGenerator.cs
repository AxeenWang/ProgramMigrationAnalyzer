using System.Text;
using ProgramMigrationAnalyzer.Core;

namespace ProgramMigrationAnalyzer.Analysis;

public sealed class MarkdownReportGenerator : IMarkdownReportGenerator
{
    public string Generate(AnalysisResult result, MigrationTargetFramework targetFramework)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# 程式分析報告");
        builder.AppendLine();
        builder.AppendLine("## 基本資訊");
        builder.AppendLine();
        builder.AppendLine($"- 原始檔案：`{result.Source.FileName}`");
        builder.AppendLine($"- 語言：{FormatLanguage(result.Language)}");
        builder.AppendLine($"- 分析時間：{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine($"- 建議目標框架：{FormatTarget(targetFramework)}");
        builder.AppendLine();
        builder.AppendLine("## 程式用途摘要");
        builder.AppendLine();
        builder.AppendLine(result.Summary);
        builder.AppendLine();

        builder.AppendLine("## 程式架構");
        builder.AppendLine();
        if (result.ProgramUnits.Count == 0)
        {
            builder.AppendLine("未辨識到可支援的程式單元。");
        }
        else
        {
            foreach (var unit in result.ProgramUnits)
            {
                builder.AppendLine($"- **{unit.Kind}** `{unit.Name}`，Lines {unit.StartLine}-{unit.EndLine}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Functions / Subroutines");
        builder.AppendLine();
        foreach (var function in result.Functions)
        {
            builder.AppendLine($"### {function.Name}");
            builder.AppendLine();
            builder.AppendLine($"- 用途：{function.Summary}");
            builder.AppendLine($"- Parameters：{FormatParameters(function.Parameters)}");
            builder.AppendLine($"- Return：`{function.ReturnType}`");
            builder.AppendLine($"- Calls：{FormatCodeList(function.CalledFunctions)}");
            builder.AppendLine($"- Tables：{FormatCodeList(function.ReferencedTables)}");
            builder.AppendLine($"- Source Lines：{function.StartLine}-{function.EndLine}");
            builder.AppendLine();
        }

        if (result.Functions.Count == 0)
        {
            builder.AppendLine("未辨識到 function 或 method。");
            builder.AppendLine();
        }

        builder.AppendLine("## SQL / Database Access");
        builder.AppendLine();
        if (result.SqlStatements.Count == 0)
        {
            builder.AppendLine("未偵測到 SQL。");
        }
        else
        {
            builder.AppendLine("| Command | Tables | Line | Risk |");
            builder.AppendLine("| --- | --- | ---: | --- |");
            foreach (var sql in result.SqlStatements)
            {
                builder.AppendLine($"| {sql.CommandType} | {EscapeCell(string.Join(", ", sql.Tables))} | {sql.StartLine} | {sql.Risk} |");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## External Dependencies");
        builder.AppendLine();
        foreach (var dependency in result.Dependencies)
        {
            builder.AppendLine($"- **{dependency.Name}** ({dependency.Category}, Risk: {dependency.Risk})：{dependency.Description}");
        }

        if (result.Dependencies.Count == 0)
        {
            builder.AppendLine("未偵測到已知外部相依性。");
        }

        builder.AppendLine();
        builder.AppendLine("## Business Rules / Detected Behavior");
        builder.AppendLine();
        foreach (var tag in result.Tags.Where(tag => tag.Category is "Business" or "Architecture" or "Database"))
        {
            builder.AppendLine($"- `{tag.Category}` / `{tag.Name}`");
        }

        foreach (var behavior in result.DetectedBehaviors)
        {
            builder.AppendLine($"- Detected behavior：{behavior}");
        }

        if (result.UsingDirectives.Count > 0)
        {
            builder.AppendLine($"- Using directives：{FormatCodeList(result.UsingDirectives)}");
        }

        if (result.ObjectCreations.Count > 0)
        {
            builder.AppendLine($"- Object creation：{FormatCodeList(result.ObjectCreations)}");
        }

        builder.AppendLine();
        builder.AppendLine("## Migration Issues");
        builder.AppendLine();
        if (result.MigrationIssues.Count == 0)
        {
            builder.AppendLine("未偵測到目前規則涵蓋的移植問題。");
        }
        else
        {
            foreach (var issue in result.MigrationIssues.OrderByDescending(issue => issue.Severity))
            {
                builder.AppendLine($"### [{issue.Severity}] {issue.Title}");
                builder.AppendLine();
                builder.AppendLine($"- Code：`{issue.Code}`");
                builder.AppendLine($"- Location：{issue.Location}");
                builder.AppendLine($"- 說明：{issue.Description}");
                builder.AppendLine($"- 建議：{issue.SuggestedAction}");
                builder.AppendLine();
            }
        }

        var assessment = result.Assessment;
        builder.AppendLine("## Migration Assessment");
        builder.AppendLine();
        builder.AppendLine("> 規則式評估指標，不代表實際工時，也不保證轉譯後與來源語意等價。");
        builder.AppendLine();
        builder.AppendLine($"- Functions：{assessment.FunctionCount}");
        builder.AppendLine($"- SQL Statements：{assessment.SqlStatementCount}");
        builder.AppendLine($"- External Dependencies：{assessment.DependencyCount}");
        builder.AppendLine($"- Deprecated APIs：{assessment.DeprecatedApiCount}");
        builder.AppendLine($"- Manual Review：{assessment.ManualReviewCount}");
        builder.AppendLine($"- Migration Score：**{assessment.MigrationScore}/100**");
        builder.AppendLine($"- Risk Level：**{assessment.RiskLevel}**");
        builder.AppendLine();
        builder.AppendLine("## 建議移植方向");
        builder.AppendLine();
        builder.AppendLine(result.Language == SourceLanguage.Informix4Gl
            ? "以 service、repository 與明確 DTO 邊界重建流程。先保留 SQL 與商業規則的可追溯性，再逐步取代 4GL UI、global state 與 transaction 控制。"
            : "先建立可重複驗證的行為基線，再以 dependency injection、IConfiguration、HttpClient 與明確 data-access boundary 逐步替換 legacy API。");

        return builder.ToString();
    }

    private static string FormatLanguage(SourceLanguage language) => language switch
    {
        SourceLanguage.CSharp => "C#",
        SourceLanguage.Informix4Gl => "Informix 4GL",
        _ => "Unknown"
    };

    private static string FormatTarget(MigrationTargetFramework target) =>
        target == MigrationTargetFramework.Net8 ? ".NET 8" : ".NET 10";

    private static string FormatParameters(IEnumerable<ParameterInfo> parameters)
    {
        var values = parameters.Select(parameter => $"`{parameter.Type} {parameter.Name}`").ToList();
        return values.Count == 0 ? "None" : string.Join(", ", values);
    }

    private static string FormatCodeList(IEnumerable<string> values)
    {
        var list = values.Select(value => $"`{value}`").ToList();
        return list.Count == 0 ? "None" : string.Join(", ", list);
    }

    private static string EscapeCell(string value) => value.Replace("|", "\\|");
}
