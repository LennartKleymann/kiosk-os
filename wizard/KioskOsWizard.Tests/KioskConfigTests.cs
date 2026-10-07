using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using KioskOsWizard.Models;
using Xunit;

namespace KioskOsWizard.Tests;

/// <summary>
/// The wizard writes the config; the kiosk parses it with shell tooling in
/// modules/config-fetcher.nix. Nobody checks that the two agree, and a
/// mismatch shows up as a kiosk quietly ignoring its settings — so these
/// tests parse the generated file the same way the kiosk does.
/// </summary>
public class KioskConfigTests
{
    /// <summary>Mirrors config_value() from modules/config-fetcher.nix,
    /// which matches ^[[:space:]]*key[[:space:]]*= and takes the last hit.</summary>
    private static string? ReadValue(string config, string key)
    {
        var pattern = new Regex($@"^\s*{Regex.Escape(key)}\s*=(.*)$", RegexOptions.Multiline);
        var matches = pattern.Matches(config);
        return matches.Count == 0 ? null : matches[^1].Groups[1].Value.Trim();
    }

    [Fact]
    public void Wired_config_round_trips_through_the_kiosk_parser()
    {
        var config = new KioskConfig
        {
            Homepage = "https://intranet.example.com",
            Connection = ConnectionType.Wired,
            Timezone = "America/Chicago",
            BrowserMode = BrowserMode.Kiosk,
            BlockRemovableDevices = true,
        };

        var content = config.ToConfigFileContent();

        Assert.Equal("https://intranet.example.com", ReadValue(content, "homepage"));
        Assert.Equal("wired", ReadValue(content, "connection"));
        Assert.Equal("America/Chicago", ReadValue(content, "timezone"));
        Assert.Equal("kiosk", ReadValue(content, "browser_mode"));
        Assert.Equal("no", ReadValue(content, "removable_devices"));
    }

    [Fact]
    public void Wifi_credentials_are_written_only_when_wifi_is_selected()
    {
        var wifi = new KioskConfig
        {
            Connection = ConnectionType.Wifi,
            WifiSsid = "CampusWiFi",
            WifiPassword = "hunter2",
        }.ToConfigFileContent();

        Assert.Equal("CampusWiFi", ReadValue(wifi, "wifi_ssid"));
        Assert.Equal("hunter2", ReadValue(wifi, "wifi_password"));

        var wired = new KioskConfig
        {
            Connection = ConnectionType.Wired,
            WifiSsid = "CampusWiFi",
            WifiPassword = "hunter2",
        }.ToConfigFileContent();

        Assert.Null(ReadValue(wired, "wifi_ssid"));
        Assert.Null(ReadValue(wired, "wifi_password"));
    }

    [Fact]
    public void Whitelist_uses_the_pipe_separator_the_kiosk_splits_on()
    {
        var content = new KioskConfig
        {
            Whitelist = new List<string> { "example.com", "cdn.example.com" },
        }.ToConfigFileContent();

        Assert.Equal("example.com|cdn.example.com", ReadValue(content, "whitelist"));
    }

    [Fact]
    public void Auto_install_is_absent_unless_explicitly_requested()
    {
        Assert.Null(ReadValue(new KioskConfig().ToConfigFileContent(), "auto_install"));
        Assert.Equal("yes", ReadValue(
            new KioskConfig { AutoInstall = true }.ToConfigFileContent(), "auto_install"));
    }

    /// <summary>
    /// Comments are stripped by the kiosk's grep, but a value containing '='
    /// must survive intact — cut -d'=' -f2- keeps everything after the first.
    /// </summary>
    [Fact]
    public void Values_containing_equals_signs_survive()
    {
        var content = new KioskConfig
        {
            Homepage = "https://example.com/?a=1&b=2",
        }.ToConfigFileContent();

        Assert.Equal("https://example.com/?a=1&b=2", ReadValue(content, "homepage"));
    }

    [Fact]
    public void Every_emitted_line_is_a_comment_blank_or_key_value()
    {
        var content = new KioskConfig
        {
            Connection = ConnectionType.Wifi,
            WifiSsid = "Net",
            WifiPassword = "pw",
            Whitelist = new List<string> { "a.example" },
            Wallpaper = "https://example.com/bg.jpg",
            RemoteConfigUrl = "https://example.com/remote.conf",
            SessionIdleMinutes = 5,
            DpmsIdleMinutes = 30,
            AutoInstall = true,
        }.ToConfigFileContent();

        var offending = content
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Where(l => !l.TrimStart().StartsWith('#'))
            .Where(l => !Regex.IsMatch(l, @"^\s*[a-z_]+\s*=.*$"))
            .ToList();

        Assert.Empty(offending);
    }

    [Fact]
    public void Line_endings_are_lf_on_every_platform()
    {
        // The kiosk's shell parsers would keep a trailing \r on every value.
        var content = new KioskConfig { SessionIdleMinutes = 10 }.ToConfigFileContent();
        Assert.DoesNotContain("\r", content);
        Assert.EndsWith("\n", content);
    }

    [Fact]
    public void Line_breaks_inside_values_cannot_inject_keys()
    {
        var content = new KioskConfig
        {
            Homepage = "https://example.com\r\nauto_install=yes",
            Connection = ConnectionType.Wifi,
            WifiSsid = "Net\nkiosk_config=https://evil.example",
            WifiPassword = "password1",
        }.ToConfigFileContent();

        Assert.Null(ReadValue(content, "auto_install"));
        Assert.Null(ReadValue(content, "kiosk_config"));
    }

    [Theory]
    [InlineData("", true)]                     // open network
    [InlineData("password", true)]             // 8 characters
    [InlineData("short", false)]               // wpa_passphrase rejects < 8
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123", false)] // 64
    public void Wifi_password_length_is_validated(string password, bool valid)
    {
        var config = new KioskConfig
        {
            Connection = ConnectionType.Wifi,
            WifiSsid = "Net",
            WifiPassword = password,
        };
        Assert.Equal(valid, config.Validate().Count == 0);
    }

    [Fact]
    public void Install_option_in_the_wizard_reaches_the_config_file()
    {
        var config = new KioskConfig();
        var step = new KioskOsWizard.ViewModels.ConfigStepViewModel(config)
        {
            InstallToDisk = true,
            UseWifi = true,
            WifiSsid = "KioskNet",
            WifiPassword = "supersecret",
            Timezone = "America/Chicago",
        };
        Assert.True(step.CanProceed);
        step.OnLeaving();

        var content = config.ToConfigFileContent();
        Assert.Equal("yes", ReadValue(content, "auto_install"));
        Assert.Equal("wifi", ReadValue(content, "connection"));
        Assert.Equal("KioskNet", ReadValue(content, "wifi_ssid"));
        Assert.Equal("supersecret", ReadValue(content, "wifi_password"));
        Assert.Equal("America/Chicago", ReadValue(content, "timezone"));
    }

    [Fact]
    public void Wizard_blocks_an_unusable_wifi_password()
    {
        var step = new KioskOsWizard.ViewModels.ConfigStepViewModel(new KioskConfig())
        {
            UseWifi = true,
            WifiSsid = "KioskNet",
            WifiPassword = "short",
        };
        Assert.False(step.CanProceed);
        Assert.NotNull(step.ValidationMessage);
    }

    [Fact]
    public void Keyboard_layout_defaults_to_us()
    {
        Assert.Equal("us", ReadValue(new KioskConfig().ToConfigFileContent(), "primary_keyboard_layout"));
    }

    [Fact]
    public void Keyboard_layout_chosen_in_the_wizard_reaches_the_config_file()
    {
        var config = new KioskConfig();
        var step = new KioskOsWizard.ViewModels.ConfigStepViewModel(config)
        {
            KeyboardLayout = KeyboardLayouts.Find("de"),
        };
        step.OnLeaving();

        Assert.Equal("de", ReadValue(config.ToConfigFileContent(), "primary_keyboard_layout"));
    }

    [Fact]
    public void Offered_layouts_are_layout_codes_not_languages()
    {
        var codes = KeyboardLayouts.All.Select(l => l.Code).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.DoesNotContain("en", codes);   // a language, not an XKB layout — it disabled kiosk keyboards
        Assert.All(codes, c => Assert.Matches("^[a-z]{2,5}$", c));
        Assert.Equal("us", KeyboardLayouts.All[0].Code);
    }
}