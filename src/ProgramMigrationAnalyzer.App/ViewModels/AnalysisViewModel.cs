using ProgramMigrationAnalyzer.Core;

namespace ProgramMigrationAnalyzer.App.ViewModels;

public sealed class AnalysisViewModel(AnalysisResult model)
{
    public string Summary => model.Summary;
    public IReadOnlyList<ProgramUnit> ProgramUnits => model.ProgramUnits;
    public IReadOnlyList<FunctionInfo> Functions => model.Functions;
    public IReadOnlyList<SqlStatementInfo> SqlStatements => model.SqlStatements;
    public IReadOnlyList<DependencyInfo> Dependencies => model.Dependencies;
    public IReadOnlyList<MigrationIssue> MigrationIssues => model.MigrationIssues;
    public IReadOnlyList<AnalysisTag> Tags => model.Tags;
    public IReadOnlyList<string> UsingDirectives => model.UsingDirectives;
    public IReadOnlyList<string> ObjectCreations => model.ObjectCreations;
    public IReadOnlyList<string> DetectedBehaviors => model.DetectedBehaviors;
    public MigrationAssessmentViewModel Assessment { get; } = new(model.Assessment);
}
