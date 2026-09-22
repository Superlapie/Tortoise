using System.Collections.ObjectModel;
using Tortoise.Core.Pilot;
using Tortoise.Core.Planning;

namespace Tortoise.App.ViewModels;

public sealed partial class PilotViewModel : PageViewModelBase
{
    private readonly IUpdatePlanService _planService;
    private readonly IPhysicalPilotChecklistService _checklistService;
    private readonly IPhysicalPilotConfirmationService _confirmationService;
    private readonly IPhysicalPilotRecoveryGuide _recoveryGuide;

    private PilotPlanOptionViewModel? _selectedPlan;
    private string _confirmationPhrase = string.Empty;
    private bool _checklistReady;
    private string? _checklistSummary;
    private string? _lastConfirmationSummary;

    public PilotViewModel(
        IUpdatePlanService planService,
        IPhysicalPilotChecklistService checklistService,
        IPhysicalPilotConfirmationService confirmationService,
        IPhysicalPilotRecoveryGuide recoveryGuide)
    {
        _planService = planService;
        _checklistService = checklistService;
        _confirmationService = confirmationService;
        _recoveryGuide = recoveryGuide;

        Plans = [];
        ChecklistItems = [];
        Acknowledgements =
        [
            new PilotAcknowledgementViewModel(
                "recovery-docs-reviewed",
                "I reviewed the physical pilot recovery documentation."),
            new PilotAcknowledgementViewModel(
                "restart-impact-understood",
                "I understand restart may be required and may interrupt work."),
            new PilotAcknowledgementViewModel(
                "single-device-scope-understood",
                "I understand this pilot applies to one selected device only."),
        ];

        foreach (var acknowledgement in Acknowledgements)
        {
            acknowledgement.PropertyChanged += (_, _) => ConfirmCommand.NotifyCanExecuteChanged();
        }

        RecoveryGuidePreview = _recoveryGuide.GetMarkdown();
        RequiredConfirmationPhrase = PhysicalPilotConstants.ConfirmationPhrase;
    }

    public ObservableCollection<PilotPlanOptionViewModel> Plans { get; }

    public ObservableCollection<PilotChecklistItemViewModel> ChecklistItems { get; }

    public ObservableCollection<PilotAcknowledgementViewModel> Acknowledgements { get; }

    public string RequiredConfirmationPhrase { get; }

    public string RecoveryGuidePreview { get; }

    public PilotPlanOptionViewModel? SelectedPlan
    {
        get => _selectedPlan;
        set
        {
            if (SetProperty(ref _selectedPlan, value))
            {
                _ = EvaluateChecklistAsync();
            }
        }
    }

    public string ConfirmationPhrase
    {
        get => _confirmationPhrase;
        set
        {
            if (SetProperty(ref _confirmationPhrase, value))
            {
                ConfirmCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool ChecklistReady
    {
        get => _checklistReady;
        private set
        {
            if (SetProperty(ref _checklistReady, value))
            {
                ConfirmCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? ChecklistSummary
    {
        get => _checklistSummary;
        private set => SetProperty(ref _checklistSummary, value);
    }

    public string? LastConfirmationSummary
    {
        get => _lastConfirmationSummary;
        private set => SetProperty(ref _lastConfirmationSummary, value);
    }

    [RelayCommand]
    private async Task RefreshPlansAsync()
    {
        IsBusy = true;
        ClearMessages();

        try
        {
            var plans = await _planService.ListPlansAsync();
            Plans.Clear();

            foreach (var plan in plans.OrderBy(entry => entry.Plan.DeviceSnapshot.Identity.FriendlyName))
            {
                Plans.Add(PilotPlanOptionViewModel.FromPlan(plan));
            }

            SelectedPlan = Plans.FirstOrDefault();
            StatusMessage = plans.Count == 0
                ? "No update plans found. Create plans with tortoise plan first."
                : $"Loaded {plans.Count.ToString()} update plan(s).";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task EvaluateChecklistAsync()
    {
        ChecklistItems.Clear();
        ChecklistReady = false;
        ChecklistSummary = null;
        ConfirmCommand.NotifyCanExecuteChanged();

        if (SelectedPlan is null)
        {
            return;
        }

        IsBusy = true;
        ClearMessages();

        try
        {
            var result = await _checklistService.EvaluateAsync(SelectedPlan.PlanId);
            foreach (var item in result.Items)
            {
                ChecklistItems.Add(PilotChecklistItemViewModel.FromItem(item));
            }

            ChecklistReady = result.IsReady;
            ChecklistSummary = result.Summary;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            ConfirmCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        if (SelectedPlan is null)
        {
            return;
        }

        IsBusy = true;
        ClearMessages();

        try
        {
            var result = await _confirmationService.ConfirmAsync(
                new PhysicalPilotConfirmationRequest(
                    SelectedPlan.PlanId,
                    ConfirmationPhrase,
                    Acknowledgements.Where(item => item.IsChecked).Select(item => item.Id).ToList()));

            LastConfirmationSummary = result.Summary;
            if (result.Accepted)
            {
                StatusMessage = result.Summary;
            }
            else
            {
                ErrorMessage = result.Summary;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanConfirm() =>
        SelectedPlan is not null
        && ChecklistReady
        && string.Equals(
            ConfirmationPhrase.Trim(),
            PhysicalPilotConstants.ConfirmationPhrase,
            StringComparison.Ordinal)
        && PhysicalPilotConstants.RequiredAcknowledgementIds.All(id =>
            Acknowledgements.Any(item => item.Id == id && item.IsChecked));
}

public sealed partial class PilotPlanOptionViewModel : ObservableObject
{
    public required Guid PlanId { get; init; }

    public required string DisplayName { get; init; }

    public override string ToString() => DisplayName;

    public static PilotPlanOptionViewModel FromPlan(StoredUpdatePlan plan) =>
        new()
        {
            PlanId = plan.PlanId,
            DisplayName =
                $"{plan.Plan.DeviceSnapshot.Identity.FriendlyName} ({plan.Classification}, {plan.RiskLevel})",
        };
}

public sealed partial class PilotChecklistItemViewModel : ObservableObject
{
    public required string Title { get; init; }

    public required string Detail { get; init; }

    public required string Status { get; init; }

    public static PilotChecklistItemViewModel FromItem(PilotChecklistItem item) =>
        new()
        {
            Title = item.Title,
            Detail = item.Detail,
            Status = item.Status.ToString(),
        };
}

public sealed partial class PilotAcknowledgementViewModel : ObservableObject
{
    private bool _isChecked;

    public PilotAcknowledgementViewModel(string id, string title)
    {
        Id = id;
        Title = title;
    }

    public string Id { get; }

    public string Title { get; }

    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }
}
