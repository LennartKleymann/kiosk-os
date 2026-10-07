// Usage (as root / administrator):
//   WizardE2E --iso kiosk-os.iso --device /dev/loop0 [--homepage URL] [--wifi SSID PASSWORD]
//             [--whitelist a.com|b.com] [--timezone Europe/Berlin] [--install-to-disk] [--fullscreen]
//
// Fills the configuration step exactly like a user would, then runs the flash
// step's sequence: write + verify the image, then put kiosk.conf on KIOSK_CFG.

using KioskOsWizard.Models;
using KioskOsWizard.Services;
using KioskOsWizard.ViewModels;

string? iso = null, device = null;
var config = new KioskConfig();
var step = new ConfigStepViewModel(config);

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--iso": iso = args[++i]; break;
        case "--device": device = args[++i]; break;
        case "--homepage": step.Homepage = args[++i]; break;
        case "--wifi": step.UseWifi = true; step.WifiSsid = args[++i]; step.WifiPassword = args[++i]; break;
        case "--whitelist": step.Whitelist = args[++i]; break;
        case "--timezone": step.Timezone = args[++i]; break;
        case "--install-to-disk": step.InstallToDisk = true; break;
        case "--fullscreen": step.BrowserModeKiosk = false; break;
        default: Console.Error.WriteLine($"unknown argument {args[i]}"); return 2;
    }
}

if (iso is null || device is null)
{
    Console.Error.WriteLine("--iso and --device are required");
    return 2;
}

if (!step.CanProceed)
{
    Console.Error.WriteLine($"Config step refuses to continue: {step.ValidationMessage}");
    return 3;
}
step.OnLeaving();

var flash = new FlashService();
if (!flash.HasRequiredPrivileges())
{
    Console.Error.WriteLine("Needs root / administrator");
    return 4;
}

var lastStage = (FlashStage?)null;
await flash.FlashAsync(iso, device, p =>
{
    if (p.Stage != lastStage) { Console.WriteLine($"[wizard] {p.Stage}"); lastStage = p.Stage; }
});

var content = config.ToConfigFileContent();
await new ConfigWriterService().WriteAsync(content, device);
Console.WriteLine("[wizard] kiosk.conf written:");
Console.Write(content);
Console.WriteLine("[wizard] done");
return 0;
