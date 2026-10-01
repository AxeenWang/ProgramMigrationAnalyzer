using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.AccessControl;
using System.Security.Principal;
using ProgramMigrationAnalyzer.Infrastructure.Authentication;
using static ProgramMigrationAnalyzer.AuthenticationChecks.CheckSupport;

namespace ProgramMigrationAnalyzer.AuthenticationChecks;

// Real Windows security descriptors, evaluated in memory. No deployment ACL is changed.
internal static class WindowsAclChecks
{
    public static void VolumeRootDeleteCannotReplaceDeployment()
    {
        // Reproduces SWANG-PC's direct, non-inheriting Authenticated Users Modify ACE.
        var security = Security(FileSystemRights.Modify);
        ValidateAncestor(security, @"C:\");
    }

    public static void ReplacementAndRootControlRemainForbidden()
    {
        foreach (var path in new[] { @"C:\ProgramData", @"C:\deployment-parent", @"\\server\share\", @"C:\rootlike\" })
            Throws<LocalAccountConfigurationException>(() => ValidateAncestor(Security(FileSystemRights.Modify), path));

        foreach (var rights in new[] { FileSystemRights.DeleteSubdirectoriesAndFiles, FileSystemRights.ChangePermissions,
            FileSystemRights.TakeOwnership })
            Throws<LocalAccountConfigurationException>(() => ValidateAncestor(Security(rights), @"C:\"));

        foreach (var generic in new[] { "GA", "GW" })
        {
            var security = new DirectorySecurity();
            security.SetSecurityDescriptorSddlForm($"O:SYG:SYD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;{generic};;;AU)");
            Throws<LocalAccountConfigurationException>(() => ValidateAncestor(security, @"C:\"));
        }

        var untrustedOwner = Security(FileSystemRights.ReadAndExecute);
        untrustedOwner.SetOwner(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null));
        Throws<LocalAccountConfigurationException>(() => ValidateAncestor(untrustedOwner, @"C:\"));
        var empty = new DirectorySecurity();
        empty.SetOwner(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
        empty.SetAccessRuleProtection(true, false);
        Throws<LocalAccountConfigurationException>(() => ValidateAncestor(empty, @"C:\"));
    }

    private static DirectorySecurity Security(FileSystemRights untrustedRights)
    {
        var security = new DirectorySecurity();
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        security.SetOwner(system);
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new(system, FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            untrustedRights, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }

    private static void ValidateAncestor(FileSystemSecurity security, string path)
    {
        var type = typeof(WindowsLocalAccountAccessPolicy);
        var validator = type.GetMethod("ValidateSecurity", BindingFlags.NonPublic | BindingFlags.Static)!;
        var forbidden = (FileSystemRights)type.GetField("ReplacementRights", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        try
        {
            // Allows the RED run to exercise the original validator, before it has path context.
            validator.Invoke(null, validator.GetParameters().Length == 2
                ? [security, forbidden] : [security, forbidden, path]);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        { ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); }
    }
}
