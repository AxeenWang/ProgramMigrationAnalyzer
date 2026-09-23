using ProgramMigrationAnalyzer.Core;

namespace ProgramMigrationAnalyzer.App.ViewModels;

public sealed class MigrationAssessmentViewModel(MigrationAssessment model)
{
    public int FunctionCount => model.FunctionCount;
    public int SqlStatementCount => model.SqlStatementCount;
    public int DependencyCount => model.DependencyCount;
    public int DeprecatedApiCount => model.DeprecatedApiCount;
    public int ManualReviewCount => model.ManualReviewCount;
    public int MigrationScore => model.MigrationScore;
    public MigrationRiskLevel RiskLevel => model.RiskLevel;
}
