using System.Collections.Generic;
using System.Threading.Tasks;
using KioskOsWizard.Models;

namespace KioskOsWizard.Services;

public interface IUsbService
{
    Task<IReadOnlyList<UsbDevice>> ListDevicesAsync();
}
