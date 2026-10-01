namespace ProgramMigrationAnalyzer.Infrastructure.Authentication;

public enum LocalAccountConfigurationFailure { Missing, InvalidData, UnsafeAccess, StorageUnavailable, Busy }
public sealed class LocalAccountConfigurationException(LocalAccountConfigurationFailure failure)
    : Exception($"Local account configuration: {failure}.")
{
    public LocalAccountConfigurationFailure Failure { get; } = failure;
}
