using System.Security;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace ProgramMigrationAnalyzer.AuthenticationChecks;

internal static class CheckSupport
{
    public static void RunStaChild(string argument, string? scenario = null, string? expectedOutput = null)
        => RunStaChild(argument.Length == 0 ? [] : [argument], scenario, expectedOutput);
    public static void RunStaChild(string[] arguments, string? scenario = null, string? expectedOutput = null)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (scenario is not null) start.Environment["PMA_AUTH_CHECK_SCENARIO"] = scenario;
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Could not launch STA check.");
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        if (!child.WaitForExit(45_000)) { child.Kill(); throw new InvalidOperationException("STA check timed out."); }
        Check(child.ExitCode == 0, $"STA check failed: {output.GetAwaiter().GetResult()} {error.GetAwaiter().GetResult()}");
        if (expectedOutput is not null)
            Check(output.GetAwaiter().GetResult().Contains(expectedOutput, StringComparison.Ordinal),
                "STA child did not run the requested real-provider scenario.");
    }
    public static SecureString Password(string value)
    {
        var password = new SecureString();
        foreach (var character in value)
        {
            password.AppendChar(character);
        }

        password.MakeReadOnly();
        return password;
    }

    public static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    internal static async Task<TException> Expect<TException>(Func<Task> action) where TException : Exception
    {
        try { await action(); }
        catch (TException exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    public static void Run(params (string Name, Action Check)[] checks)
    {
        var failures = 0;
        foreach (var (name, check) in checks)
        {
            try
            {
                check();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
            }
        }

        if (failures > 0)
        {
            throw new InvalidOperationException($"{failures}/{checks.Length} authentication checks failed.");
        }
    }
}

internal sealed class ManualTimeProvider : TimeProvider
{
    private long _ticks;
    private readonly List<ManualTimer> _timers = [];
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _ticks;
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(_ticks);
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        _timers.Add(timer);
        timer.Change(dueTime, period);
        return timer;
    }
    public void Advance(TimeSpan duration)
    {
        _ticks += duration.Ticks;
        foreach (var timer in _timers.ToArray()) timer.Fire();
    }
    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private long _due = long.MaxValue;
        private TimeSpan _period;
        private bool _disposed;
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (_disposed) return false;
            _due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : owner._ticks + dueTime.Ticks;
            _period = period;
            return true;
        }
        public void Fire()
        {
            if (_disposed || owner._ticks < _due) return;
            _due = _period <= TimeSpan.Zero ? long.MaxValue : owner._ticks + _period.Ticks;
            callback(state);
        }
        public void Dispose() => _disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
