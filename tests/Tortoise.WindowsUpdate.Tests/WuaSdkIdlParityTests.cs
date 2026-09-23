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
            var sdkGuid = WuapiIdlParser.ExtractInterfaceGuid(idlText, interfaceName);
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
        var updateCollectionSection = WuapiIdlParser.ExtractInterfaceSection(idlText, "IUpdateCollection");
        var downloaderSection = WuapiIdlParser.ExtractInterfaceSection(idlText, "IUpdateDownloader");
        var downloadResultSection = WuapiIdlParser.ExtractInterfaceSection(idlText, "IDownloadResult");
        var installerSection = WuapiIdlParser.ExtractInterfaceSection(idlText, "IUpdateInstaller");
        var installationResultSection = WuapiIdlParser.ExtractInterfaceSection(idlText, "IInstallationResult");

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
}
