using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace ProgramMigrationAnalyzer.Infrastructure.Authentication;

public interface ILocalAccountAccessPolicy
{
    bool IsElevatedAdministrator { get; }
    void ValidateReadAccess(string filePath);
    void PrepareWriteAccess(string directoryPath);
    void SecureFile(string filePath);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsLocalAccountAccessPolicy : ILocalAccountAccessPolicy
{
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier System = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);
    private static readonly SecurityIdentifier TrustedInstaller =
        new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464");
    private const FileSystemRights MutatingRights = FileSystemRights.Write | FileSystemRights.Delete
        | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
    private const FileSystemRights ReplacementRights = FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles
        | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    public bool IsElevatedAdministrator
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)
                && GetTokenInformation(identity.AccessToken, 20, out var elevated, sizeof(int), out _)
                && elevated != 0;
        }
    }

    public void ValidateReadAccess(string filePath)
    {
        RejectReparsePoints(filePath);
        var file = new FileInfo(filePath);
        if (!file.Exists) throw new LocalAccountConfigurationException(LocalAccountConfigurationFailure.Missing);
        ValidateSecurity(file.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner), MutatingRights);
        var auth = file.Directory ?? throw UnsafeAccess();
        ValidateDirectory(auth, MutatingRights, requireProtected: true);
        var application = auth.Parent ?? throw UnsafeAccess();
        ValidateDirectory(application, MutatingRights, requireProtected: true);
        ValidateAncestors(application.Parent);
    }

    public void PrepareWriteAccess(string directoryPath)
    {
        RequireAdministrator();
        var auth = new DirectoryInfo(Path.GetFullPath(directoryPath));
        var application = auth.Parent ?? throw UnsafeAccess();
        RejectReparsePoints(auth.FullName);
        // Do not change ProgramData or any deployment ancestor.
        ValidateAncestors(application.Parent);
        SecureDirectory(application);
        SecureDirectory(auth);
        RejectReparsePoints(auth.FullName);
    }

    public void SecureFile(string filePath)
    {
        RequireAdministrator();
        RejectReparsePoints(filePath);
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(Administrators);
        security.AddAccessRule(new(Administrators, FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new(System, FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new(Users, FileSystemRights.Read, AccessControlType.Allow));
        new FileInfo(filePath).SetAccessControl(security);
    }

    private void SecureDirectory(DirectoryInfo directory)
    {
        RejectReparsePoints(directory.FullName);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(Administrators);
        var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new(Administrators, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new(System, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new(Users, FileSystemRights.ReadAndExecute, inheritance, PropagationFlags.None, AccessControlType.Allow));
        directory.Create(security);
        directory.SetAccessControl(security);
        ValidateDirectory(new DirectoryInfo(directory.FullName), MutatingRights, requireProtected: true);
    }

    private static void ValidateAncestors(DirectoryInfo? directory)
    {
        while (directory is not null)
        {
            ValidateDirectory(directory, ReplacementRights, requireProtected: false);
            directory = directory.Parent;
        }
    }

    private static void ValidateDirectory(DirectoryInfo directory, FileSystemRights forbidden, bool requireProtected)
    {
        var security = directory.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        if (requireProtected && !security.AreAccessRulesProtected) throw UnsafeAccess();
        ValidateSecurity(security, forbidden, directory.FullName);
    }

    private static void ValidateSecurity(FileSystemSecurity security, FileSystemRights forbidden, string? directoryPath = null)
    {
        if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !Trusted(owner))
            throw UnsafeAccess();
        // A local volume root cannot itself be removed or renamed. DELETE on that root
        // does not authorize replacing its children. Still reject DELETE_CHILD and ACL control.
        if (forbidden == ReplacementRights && directoryPath is { Length: 3 }
            && char.IsAsciiLetter(directoryPath[0]) && directoryPath[1] == ':'
            && directoryPath[2] is '\\' or '/')
            forbidden &= ~FileSystemRights.Delete;
        // Empty/null DACL and grants to unknown principals are never trusted.
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));
        if (rules.Count == 0) throw UnsafeAccess();
        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.AccessControlType != AccessControlType.Allow
                || (rule.PropagationFlags & PropagationFlags.InheritOnly) != 0) continue;
            if (!Trusted((SecurityIdentifier)rule.IdentityReference)
                && (((rule.FileSystemRights & forbidden) != 0)
                    || ((uint)rule.FileSystemRights & 0x50000000u) != 0))
                throw UnsafeAccess();
        }
    }

    private static bool Trusted(SecurityIdentifier principal) =>
        principal == Administrators || principal == System || principal == TrustedInstaller;

    private static void RejectReparsePoints(string path)
    {
        var fullPath = Path.GetFullPath(path);
        for (string? current = fullPath; current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw UnsafeAccess();
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private void RequireAdministrator()
    {
        if (!IsElevatedAdministrator)
            throw new UnauthorizedAccessException("Local account management requires an elevated Windows administrator.");
    }

    private static LocalAccountConfigurationException UnsafeAccess() => new(LocalAccountConfigurationFailure.UnsafeAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass,
        out int information, int length, out int returnLength);
}
