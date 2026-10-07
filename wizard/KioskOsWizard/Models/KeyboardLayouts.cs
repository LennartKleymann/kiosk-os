using System.Collections.Generic;

namespace KioskOsWizard.Models;

/// <summary>A keyboard layout as offered in the wizard: what people read, and the XKB code the kiosk needs.</summary>
public record KeyboardLayout(string Code, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Layouts offered in the wizard. Each code is an XKB layout that ships with
/// the kiosk (xkeyboard-config). Typing a language code like "en" into
/// kiosk.conf by hand used to leave kiosks without a working keyboard, so the
/// wizard offers names instead of codes.
/// </summary>
public static class KeyboardLayouts
{
    public static IReadOnlyList<KeyboardLayout> All { get; } =
    [
        new("us", "English (US)"),
        new("gb", "English (UK)"),
        new("de", "Deutsch"),
        new("ch", "Deutsch (Schweiz)"),
        new("fr", "Français"),
        new("be", "Français / Nederlands (Belgique)"),
        new("ca", "Français (Canada)"),
        new("es", "Español"),
        new("latam", "Español (Latinoamérica)"),
        new("it", "Italiano"),
        new("nl", "Nederlands"),
        new("pt", "Português"),
        new("br", "Português (Brasil)"),
        new("se", "Svenska"),
        new("no", "Norsk"),
        new("dk", "Dansk"),
        new("fi", "Suomi"),
        new("pl", "Polski"),
        new("cz", "Čeština"),
        new("tr", "Türkçe"),
    ];

    public static KeyboardLayout Find(string? code) =>
        All.FirstOrDefaultByCode(code) ?? All[0];

    private static KeyboardLayout? FirstOrDefaultByCode(this IReadOnlyList<KeyboardLayout> list, string? code)
    {
        foreach (var l in list)
            if (string.Equals(l.Code, code, System.StringComparison.OrdinalIgnoreCase))
                return l;
        return null;
    }
}
