using CommunityToolkit.Mvvm.ComponentModel;
using KioskOsWizard.Models;

namespace KioskOsWizard.ViewModels;

/// <summary>
/// Base class for wizard steps. Each step has a title and can validate
/// whether the user can proceed to the next step.
/// </summary>
public abstract partial class StepViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _title = "";

    public KioskConfig Config { get; }

    protected StepViewModel(KioskConfig config)
    {
        Config = config;
    }

    public virtual bool CanProceed => true;

    public virtual void OnEntered() { }

    public virtual void OnLeaving() { }
}
