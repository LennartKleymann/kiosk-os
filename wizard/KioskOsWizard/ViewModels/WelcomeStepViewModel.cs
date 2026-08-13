using KioskOsWizard.Models;

namespace KioskOsWizard.ViewModels;

public class WelcomeStepViewModel : StepViewModel
{
    public WelcomeStepViewModel(KioskConfig config) : base(config)
    {
        Title = "Welcome";
    }
}
