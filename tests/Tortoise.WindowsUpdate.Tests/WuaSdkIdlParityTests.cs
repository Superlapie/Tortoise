using System.Text.RegularExpressions;

namespace Tortoise.WindowsUpdate.Tests;

public sealed class WuaSdkIdlParityTests
{
    [Fact]
    public void Windows_sdk_wuapi_idl_matches_managed_interface_guids()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var idlPath = LocateWuapiIdl();
        if (idlPath is null)
        {
            throw new InvalidOperationException("Windows SDK wuapi.idl was not found on this runner.");
        }

        var idlText = File.ReadAllText(idlPath);
        foreach (var expectation in WuaComMetadataExpectations.All)
        {
            var interfaceName = expectation.InterfaceType.Name;
            var sdkGuid = ExtractInterfaceGuid(idlText, interfaceName);
            Assert.False(string.IsNullOrWhiteSpace(sdkGuid));
            Assert.Equal(expectation.InterfaceId, sdkGuid, ignoreCase: true);
        }
    }

    [Fact]
    public void Windows_sdk_wuapi_idl_documents_mutation_path_method_shapes()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var idlPath = LocateWuapiIdl();
        if (idlPath is null)
        {
            throw new InvalidOperationException("Windows SDK wuapi.idl was not found on this runner.");
        }

        var idlText = File.ReadAllText(idlPath);
        var updateCollectionSection = ExtractInterfaceSection(idlText, "IUpdateCollection");
        var downloaderSection = ExtractInterfaceSection(idlText, "IUpdateDownloader");
        var downloadResultSection = ExtractInterfaceSection(idlText, "IDownloadResult");
        var installerSection = ExtractInterfaceSection(idlText, "IUpdateInstaller");
        var installationResultSection = ExtractInterfaceSection(idlText, "IInstallationResult");

        Assert.Contains("HRESULT Add(", updateCollectionSection, StringComparison.Ordinal);
        Assert.Contains("[out, retval] LONG* retval", updateCollectionSection, StringComparison.Ordinal);
        Assert.Contains("HRESULT Download(", downloaderSection, StringComparison.Ordinal);
        Assert.Contains("[out, retval] IDownloadResult** retval", downloaderSection, StringComparison.Ordinal);
        Assert.Contains("HRESULT GetUpdateResult(", downloadResultSection, StringComparison.Ordinal);
        Assert.Contains("[out, retval] IUpdateDownloadResult** retval", downloadResultSection, StringComparison.Ordinal);
        Assert.Contains("HRESULT Install(", installerSection, StringComparison.Ordinal);
        Assert.Contains("[out, retval] IInstallationResult** retval", installerSection, StringComparison.Ordinal);
        Assert.Contains("HRESULT GetUpdateResult(", installationResultSection, StringComparison.Ordinal);
        Assert.Contains("[out, retval] IUpdateInstallationResult** retval", installationResultSection, StringComparison.Ordinal);
    }

    private static string? LocateWuapiIdl()
    {
        var roots = new List<string>();
        var programFilesX86 = global::System.Environment.GetEnvironmentVariable("ProgramFiles(x86)");
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            roots.Add(Path.Combine(programFilesX86, "Windows Kits", "10", "Include"));
        }

        var windowsSdkDir = global::System.Environment.GetEnvironmentVariable("WindowsSdkDir");
        if (!string.IsNullOrWhiteSpace(windowsSdkDir))
        {
            roots.Add(Path.Combine(windowsSdkDir, "Include"));
        }

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            var matches = Directory.EnumerateFiles(root, "wuapi.idl", SearchOption.AllDirectories)
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (matches.Length > 0)
            {
                return matches[0];
            }
        }

        return null;
    }

    private static string ExtractInterfaceGuid(string idlText, string interfaceName)
    {
        var pattern = $@"uuid\((?<guid>[0-9a-fA-F-]+)\)[\s\S]*?interface\s+{Regex.Escape(interfaceName)}\s*:";
        var match = Regex.Match(idlText, pattern, RegexOptions.IgnoreCase);
        return match.Success ? NormalizeGuid(match.Groups["guid"].Value) : string.Empty;
    }

    private static string ExtractInterfaceSection(string idlText, string interfaceName)
    {
        var pattern = $@"interface\s+{Regex.Escape(interfaceName)}\s*:\s*[\s\S]*?\n\}}";
        var match = Regex.Match(idlText, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Value : string.Empty;
    }

    private static string NormalizeGuid(string rawGuid) =>
        Guid.Parse(rawGuid).ToString("D").ToUpperInvariant();
}
