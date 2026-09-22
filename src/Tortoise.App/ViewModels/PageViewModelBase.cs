namespace Tortoise.App.ViewModels;

public abstract partial class PageViewModelBase : ObservableObject
{
    private bool _isBusy;
    private string? _statusMessage;
    private string? _errorMessage;

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsNotBusy));
            }
        }
    }

    public bool IsNotBusy => !IsBusy;

    public string? StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public void ClearMessages()
    {
        StatusMessage = null;
        ErrorMessage = null;
    }
}
