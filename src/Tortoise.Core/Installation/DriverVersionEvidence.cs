namespace Tortoise.Core.Installation;

public static class DriverVersionEvidence
{
    public static readonly Version UnknownVersion = new(0, 0);

    public static bool IsKnown(Version? version) =>
        version is not null && version != UnknownVersion;
}
