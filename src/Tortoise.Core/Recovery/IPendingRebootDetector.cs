namespace Tortoise.Core.Recovery;

public interface IPendingRebootDetector
{
    PendingRebootState DetectPendingReboot();
}
