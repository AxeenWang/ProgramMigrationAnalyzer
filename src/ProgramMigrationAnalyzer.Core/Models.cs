namespace ProgramMigrationAnalyzer.Core;

public enum SourceLanguage
{
    Unknown,
    CSharp,
    Informix4Gl
}

public enum AnalysisStatus
{
    Loaded,
    Analyzing,
    Completed,
    Failed
}

public enum IssueSeverity
{
    Info,
    Low,
    Medium,
    High,
    Critical
}

public enum MigrationRiskLevel
{
    Low,
    Medium,
    High,
    Critical
}

public enum MigrationTargetFramework
{
    Net8,
    Net10
}

public sealed class SourceDocument
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required string Content { get; init; }
    public SourceLanguage Language { get; set; }
}

public sealed class ProgramUnit
{
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public string? Namespace { get; init; }
    public string? BaseType { get; init; }
    public List<string> ImplementedInterfaces { get; } = [];
    public List<string> Members { get; } = [];
    public int StartLine { get; init; }
    public int EndLine { get; init; }
}

public sealed class ParameterInfo
{
    public required string Name { get; init; }
    public string Type { get; init; } = "object?";
}

public sealed class FunctionInfo
{
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public string ReturnType { get; init; } = "void";
    public List<ParameterInfo> Parameters { get; } = [];
    public string Summary { get; set; } = string.Empty;
    public List<string> CalledFunctions { get; } = [];
    public List<string> ReferencedTables { get; } = [];
    public int StartLine { get; init; }
    public int EndLine { get; init; }
}

public sealed class SqlStatementInfo
{
    public required string CommandType { get; init; }
    public required string RawSql { get; init; }
    public List<string> Tables { get; } = [];
    public int StartLine { get; init; }
    public string Risk { get; init; } = "Review";
}

public sealed class DependencyInfo
{
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }
    public string Risk { get; init; } = "Low";
}

public sealed class MigrationIssue
{
    public required string Code { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required IssueSeverity Severity { get; init; }
    public required string SuggestedAction { get; init; }
    public string Location { get; init; } = string.Empty;
}

public sealed record AnalysisTag(string Category, string Name);

public sealed class MigrationAssessment
{
    public int FunctionCount { get; set; }
    public int SqlStatementCount { get; set; }
    public int DependencyCount { get; set; }
    public int DeprecatedApiCount { get; set; }
    public int ManualReviewCount { get; set; }
    public int MigrationScore { get; set; }
    public MigrationRiskLevel RiskLevel { get; set; }
}

public sealed class AnalysisResult
{
    public required SourceDocument Source { get; init; }
    public required SourceLanguage Language { get; init; }
    public string Summary { get; set; } = string.Empty;
    public List<ProgramUnit> ProgramUnits { get; } = [];
    public List<FunctionInfo> Functions { get; } = [];
    public List<SqlStatementInfo> SqlStatements { get; } = [];
    public List<DependencyInfo> Dependencies { get; } = [];
    public List<MigrationIssue> MigrationIssues { get; } = [];
    public List<AnalysisTag> Tags { get; } = [];
    public List<string> UsingDirectives { get; } = [];
    public List<string> ObjectCreations { get; } = [];
    public List<string> DetectedBehaviors { get; } = [];
    public MigrationAssessment Assessment { get; set; } = new();
}

public sealed class TranslationResult
{
    public required SourceDocument Source { get; init; }
    public required MigrationTargetFramework TargetFramework { get; init; }
    public required string GeneratedCode { get; init; }
    public List<string> Warnings { get; } = [];
}
