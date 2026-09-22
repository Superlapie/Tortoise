using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Tortoise.Contracts.Mutation;

namespace Tortoise.Broker.Validation;

public static class LabAuthoritativeStoreGuard
{
    public static void EnsureProtectedStoreReady(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        if (!BrokerDatabasePathValidator.TryValidate(databasePath, out var normalizedPath, out var validationError)
            || string.IsNullOrWhiteSpace(normalizedPath))
        {
            throw new InvalidOperationException(validationError ?? "Lab database path is invalid.");
        }

        var directory = Path.GetDirectoryName(normalizedPath)
            ?? throw new InvalidOperationException("Lab database path does not contain a directory.");

        Directory.CreateDirectory(directory);

        if (OperatingSystem.IsWindows())
        {
            ApplyWindowsDirectoryProtection(directory);
            if (File.Exists(normalizedPath))
            {
                ApplyWindowsFileProtection(normalizedPath);
            }
        }
    }

    public static bool ValidateAuthoritativeStore(string databasePath, out string? error)
    {
        error = null;

        if (!BrokerDatabasePathValidator.TryValidate(databasePath, out var normalizedPath, out var validationError)
            || string.IsNullOrWhiteSpace(normalizedPath))
        {
            error = validationError ?? "Lab database path is invalid.";
            return false;
        }

        if (!File.Exists(normalizedPath))
        {
            error = "Authoritative Lab plan database was not found at the expected path.";
            return false;
        }

        if (OperatingSystem.IsWindows() && !VerifyWindowsFileProtection(normalizedPath, out error))
        {
            return false;
        }

        return true;
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyWindowsDirectoryProtection(string directoryPath)
    {
        var currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current Windows user identity is unavailable.");

        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        DirectoryInfo directoryInfo = new(directoryPath);
        directoryInfo.SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyWindowsFileProtection(string filePath)
    {
        var currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current Windows user identity is unavailable.");

        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            AccessControlType.Allow));

        FileInfo fileInfo = new(filePath);
        fileInfo.SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static bool VerifyWindowsFileProtection(string filePath, out string? error)
    {
        error = null;
        var currentUser = WindowsIdentity.GetCurrent().User;
        if (currentUser is null)
        {
            error = "Current Windows user identity is unavailable.";
            return false;
        }

        FileInfo fileInfo = new(filePath);
        var security = fileInfo.GetAccessControl();
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier));
        var hasCurrentUserRule = false;

        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.IdentityReference == currentUser
                && rule.AccessControlType == AccessControlType.Allow
                && (rule.FileSystemRights & FileSystemRights.Read) != 0)
            {
                hasCurrentUserRule = true;
                break;
            }
        }

        if (!hasCurrentUserRule)
        {
            error = "Authoritative Lab plan database ACL does not grant the current user read access.";
            return false;
        }

        var labRoot = Path.GetFullPath(TortoiseLabPaths.LabDataRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedFile = Path.GetFullPath(filePath);
        if (!normalizedFile.StartsWith(labRoot, StringComparison.OrdinalIgnoreCase))
        {
            error = "Authoritative Lab plan database resolved outside the Lab data root.";
            return false;
        }

        return true;
    }
}
