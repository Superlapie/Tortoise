using System.Text.RegularExpressions;

namespace Tortoise.WindowsUpdate.Tests;

internal static class WuapiIdlParser
{
    internal static string ExtractInterfaceGuid(string idlText, string interfaceName)
    {
        var declarationIndex = FindInterfaceDeclarationIndex(idlText, interfaceName);
        if (declarationIndex < 0)
        {
            throw new InvalidOperationException(
                $"Could not locate interface declaration for '{interfaceName}' in wuapi.idl.");
        }

        var attributeBlock = ExtractImmediatelyPrecedingAttributeBlock(idlText, declarationIndex);
        var uuidMatches = Regex.Matches(
            attributeBlock,
            @"uuid\s*\(\s*(?<guid>[0-9a-fA-F-]+)\s*\)",
            RegexOptions.IgnoreCase);

        if (uuidMatches.Count == 0)
        {
            throw new InvalidOperationException(
                $"No uuid(...) attribute was found for interface '{interfaceName}' in wuapi.idl.");
        }

        if (uuidMatches.Count > 1)
        {
            throw new InvalidOperationException(
                $"Ambiguous uuid(...) attributes were found for interface '{interfaceName}' in wuapi.idl.");
        }

        return NormalizeGuid(uuidMatches[0].Groups["guid"].Value);
    }

    internal static string ExtractInterfaceSection(string idlText, string interfaceName)
    {
        var pattern = $@"interface\s+{Regex.Escape(interfaceName)}\s*:\s*[\s\S]*?\n\}}";
        var match = Regex.Match(idlText, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Value : string.Empty;
    }

    private static int FindInterfaceDeclarationIndex(string idlText, string interfaceName)
    {
        var pattern = $@"\binterface\s+{Regex.Escape(interfaceName)}\s*:";
        var match = Regex.Match(idlText, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Index : -1;
    }

    private static string ExtractImmediatelyPrecedingAttributeBlock(string idlText, int interfaceDeclarationIndex)
    {
        var originalBefore = idlText[..interfaceDeclarationIndex];
        var beforeDeclaration = originalBefore.TrimEnd();
        if (beforeDeclaration.Length == 0 || beforeDeclaration[^1] != ']')
        {
            throw new InvalidOperationException(
                "Interface declaration is not immediately preceded by an IDL attribute block.");
        }

        var closeBracketIndex = beforeDeclaration.Length - 1;
        var betweenCloseBracketAndDeclaration = originalBefore[(closeBracketIndex + 1)..];
        if (!string.IsNullOrWhiteSpace(betweenCloseBracketAndDeclaration))
        {
            throw new InvalidOperationException(
                "Interface declaration is not immediately preceded by its attribute block.");
        }

        var openBracketIndex = FindMatchingOpenBracket(beforeDeclaration, closeBracketIndex);
        if (openBracketIndex < 0)
        {
            throw new InvalidOperationException(
                "Could not locate the opening '[' for the interface attribute block.");
        }

        return beforeDeclaration[openBracketIndex..(closeBracketIndex + 1)];
    }

    private static int FindMatchingOpenBracket(string text, int closeBracketIndex)
    {
        var depth = 1;
        for (var index = closeBracketIndex - 1; index >= 0; index--)
        {
            switch (text[index])
            {
                case ']':
                    depth++;
                    break;
                case '[':
                    depth--;
                    if (depth == 0)
                    {
                        return index;
                    }

                    break;
            }
        }

        return -1;
    }

    private static string NormalizeGuid(string rawGuid) =>
        Guid.Parse(rawGuid).ToString("D").ToUpperInvariant();
}
