using System.Security;

namespace ProgramMigrationAnalyzer.AuthenticationChecks;

internal static class CheckSupport
{
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
