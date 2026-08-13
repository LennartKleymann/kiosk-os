using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace KioskOsWizard.Services;

/// <summary>
/// Appends to a log file next to the image cache. Flashing fails on other
/// people's hardware in ways that cannot be reproduced here, so the file is
/// usually the only evidence available afterwards.
/// </summary>
public static class WizardLog
{
    private static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "kiosk-os", "wizard.log");

    public static void Info(string message) => Write("INFO ", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    public static void SessionStart()
    {
        Write("INFO ", new string('-', 60));
        Write("INFO ", $"kiosk-os wizard starting on {RuntimeInformation.OSDescription} " +
                       $"({RuntimeInformation.OSArchitecture}), log at {FilePath}");
    }

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}";

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, line, Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // Logging must never be the reason a flash fails.
        }

        Console.Write(line);
    }
}
