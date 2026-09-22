namespace Tortoise.Core.Pilot;

public sealed class PhysicalPilotConfirmationService : IPhysicalPilotConfirmationService
{
    private readonly IPhysicalPilotChecklistService _checklistService;
    private readonly IPilotConfirmationStore _confirmationStore;

    public PhysicalPilotConfirmationService(
        IPhysicalPilotChecklistService checklistService,
        IPilotConfirmationStore confirmationStore)
    {
        _checklistService = checklistService;
        _confirmationStore = confirmationStore;
    }

    public async Task<PhysicalPilotConfirmationResult> ConfirmAsync(
        PhysicalPilotConfirmationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var checklist = await _checklistService.EvaluateAsync(request.PlanId, cancellationToken);
        if (!checklist.IsReady)
        {
            return Rejected(
                request,
                checklist.IsReady,
                "Physical pilot confirmation was rejected because the checklist is not ready.");
        }

        if (!string.Equals(
                request.ConfirmationPhrase.Trim(),
                PhysicalPilotConstants.ConfirmationPhrase,
                StringComparison.Ordinal))
        {
            return Rejected(
                request,
                checklist.IsReady,
                $"Confirmation phrase must exactly match '{PhysicalPilotConstants.ConfirmationPhrase}'.");
        }

        var missingAcknowledgements = PhysicalPilotConstants.RequiredAcknowledgementIds
            .Except(request.AcknowledgedItemIds, StringComparer.Ordinal)
            .ToList();

        if (missingAcknowledgements.Count > 0)
        {
            return Rejected(
                request,
                checklist.IsReady,
                "All required acknowledgement items must be confirmed.");
        }

        var record = new PhysicalPilotConfirmationRecord(
            Guid.NewGuid(),
            request.PlanId,
            DateTimeOffset.UtcNow,
            PhysicalPilotConstants.ConfirmationPhrase,
            request.AcknowledgedItemIds.ToList(),
            checklist.IsReady,
            "Physical pilot readiness confirmed. Installation remains policy-gated and is not performed automatically.");

        await _confirmationStore.SaveAsync(record, cancellationToken);

        return new PhysicalPilotConfirmationResult(
            record,
            Accepted: true,
            Summary: record.Summary);
    }

    private static PhysicalPilotConfirmationResult Rejected(
        PhysicalPilotConfirmationRequest request,
        bool checklistReady,
        string summary) =>
        new(
            new PhysicalPilotConfirmationRecord(
                Guid.NewGuid(),
                request.PlanId,
                DateTimeOffset.UtcNow,
                request.ConfirmationPhrase,
                request.AcknowledgedItemIds.ToList(),
                checklistReady,
                summary),
            Accepted: false,
            Summary: summary);
}
