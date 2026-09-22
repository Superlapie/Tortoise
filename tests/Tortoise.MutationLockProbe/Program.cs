using Tortoise.Core.Mutation;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: tortoise-mutation-lock-probe <workflow|servicing|hold workflow|hold servicing>");
    return 1;
}

switch (args[0].ToLowerInvariant())
{
    case "workflow":
        return WriteLockResult(MutationWorkflowLock.TryAcquire().IsAcquired);
    case "servicing":
        return WriteLockResult(MutationServicingLock.TryAcquire().IsAcquired);
    case "hold" when args.Length >= 2:
        return HoldLock(args[1]);
    default:
        Console.Error.WriteLine("Usage: tortoise-mutation-lock-probe <workflow|servicing|hold workflow|hold servicing>");
        return 1;
}

static int WriteLockResult(bool acquired)
{
    Console.WriteLine(acquired ? "ACQUIRED" : "DENIED");
    return acquired ? 0 : 2;
}

static int HoldLock(string lockName)
{
    switch (lockName.ToLowerInvariant())
    {
        case "workflow":
            using (var workflowLock = MutationWorkflowLock.TryAcquire())
            {
                if (!workflowLock.IsAcquired)
                {
                    Console.WriteLine("DENIED");
                    return 2;
                }

                Console.WriteLine("HOLDING");
                Console.Out.Flush();
                Thread.Sleep(Timeout.Infinite);
            }

            break;
        case "servicing":
            using (var servicingLock = MutationServicingLock.TryAcquire())
            {
                if (!servicingLock.IsAcquired)
                {
                    Console.WriteLine("DENIED");
                    return 2;
                }

                Console.WriteLine("HOLDING");
                Console.Out.Flush();
                Thread.Sleep(Timeout.Infinite);
            }

            break;
        default:
            Console.Error.WriteLine("Unknown lock name.");
            return 1;
    }

    return 0;
}
