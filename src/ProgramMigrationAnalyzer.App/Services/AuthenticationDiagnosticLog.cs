using ProgramMigrationAnalyzer.Core.Authentication;

namespace ProgramMigrationAnalyzer.App.Services;

public enum AuthenticationDiagnosticKind { Succeeded, Failed, Cancelled, LoggedOut, InitializationFailed }

public sealed record AuthenticationDiagnosticEntry(DateTimeOffset Time, AuthenticationDiagnosticKind Kind,
    Guid? UserId, AuthenticationFailure? Failure);

public sealed class AuthenticationDiagnosticLog
{
    private readonly TimeProvider _timeProvider;
    private readonly List<AuthenticationDiagnosticEntry> _entries = [];
    public AuthenticationDiagnosticLog(TimeProvider? timeProvider = null) => _timeProvider = timeProvider ?? TimeProvider.System;
    public IReadOnlyList<AuthenticationDiagnosticEntry> Entries => _entries.AsReadOnly();

    // No free-form messages or provider exception text can enter this process-only log.
    internal void Record(AuthenticationDiagnosticKind kind, Guid? userId = null, AuthenticationFailure? failure = null)
    {
        if (_entries.Count == 500) _entries.RemoveAt(0);
        _entries.Add(new(_timeProvider.GetUtcNow(), kind, userId, failure));
    }
}
