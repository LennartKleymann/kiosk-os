using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using KioskOsWizard.ViewModels;

namespace KioskOsWizard.Views;

public partial class UsbStepView : UserControl
{
    public UsbStepView()
    {
        InitializeComponent();
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select kiosk-os ISO",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("ISO image") { Patterns = new[] { "*.iso" } },
            }
        });

        var file = files.FirstOrDefault();
        if (file != null && DataContext is UsbStepViewModel vm)
        {
            vm.CustomIsoPath = file.Path.LocalPath;
        }
    }
}
