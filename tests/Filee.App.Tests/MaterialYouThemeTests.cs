// Tests for Material You theme: dynamic tonal palette generation, theme dictionary application,
// softness adherence, theme.json overrides, monochrome contrast, and preset selection.

using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Filee.App.Services;
using Filee.App.ViewModels.Pages;
using Filee.Core.Localization;
using Filee.Core.Platform;
using Filee.Core.Settings;

namespace Filee.App.Tests;

public class MaterialYouThemeTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void ThemeSettings_persists_ThemeStyle()
    {
        var settings = new AppSettings
        {
            Theme = new ThemeSettings
            {
                Style = ThemeStyle.MaterialYou,
                Accent = "#7F77DD",
            },
        };

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        Assert.Contains("\"style\"", json);
        Assert.Contains("MaterialYou", json);

        var restored = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
        Assert.NotNull(restored);
        Assert.Equal(ThemeStyle.MaterialYou, restored.Theme.Style);
        Assert.Equal("#7F77DD", restored.Theme.Accent);
    }

    [Fact]
    public void Palette_generates_distinct_tonal_roles_for_light_and_dark()
    {
        var seed = Color.Parse("#7F77DD");
        var light = MaterialYouPalette.Generate(seed, isDark: false);
        var dark = MaterialYouPalette.Generate(seed, isDark: true);

        // Light mode: surfaces are bright, text is dark
        Assert.True(light.Page.R > 230 && light.Page.G > 230 && light.Page.B > 230);
        Assert.True(light.Surface.R >= 250 && light.Surface.G >= 250 && light.Surface.B >= 250);
        Assert.True(light.TextPrimary.R < 60 && light.TextPrimary.G < 60 && light.TextPrimary.B < 60);

        // Light mode accent roles: faint/soft are light backgrounds, strong is dark foreground
        Assert.True(MaterialYouPalette.Luminance(light.AccentFaint) > 0.85);
        Assert.True(MaterialYouPalette.Luminance(light.AccentStrong) < 0.25);

        // Dark mode: surfaces are deep dark, text is bright
        Assert.True(dark.Page.R < 40 && dark.Page.G < 40 && dark.Page.B < 40);
        Assert.True(dark.Surface.R < 50 && dark.Surface.G < 50 && dark.Surface.B < 50);
        Assert.True(dark.TextPrimary.R > 200 && dark.TextPrimary.G > 200 && dark.TextPrimary.B > 200);

        // Dark mode accent roles: faint is dark background, strong is bright foreground (for drop zone contrast!)
        Assert.True(MaterialYouPalette.Luminance(dark.AccentFaint) < 0.20);
        Assert.True(MaterialYouPalette.Luminance(dark.AccentStrong) > 0.65);
    }

    [Fact]
    public void Palette_generates_proper_monochrome_contrast()
    {
        var charcoal = Color.Parse("#1F1E1D");
        Assert.True(MaterialYouPalette.IsMonochrome(charcoal));

        var light = MaterialYouPalette.Generate(charcoal, isDark: false);
        var dark = MaterialYouPalette.Generate(charcoal, isDark: true);

        // Light mode monochrome: accent is dark charcoal, text on accent is white
        Assert.True(MaterialYouPalette.Luminance(light.Accent) < 0.15);
        Assert.Equal(Colors.White, light.OnAccent);

        // Dark mode monochrome: accent is silvery white, text on accent is dark charcoal
        Assert.True(MaterialYouPalette.Luminance(dark.Accent) > 0.80);
        Assert.Equal(Color.Parse("#1F1E1D"), dark.OnAccent);
    }

    [AvaloniaFact]
    public void ThemeService_applies_MaterialYou_to_theme_dictionaries()
    {
        TestServices.EnsureInitialized("en");
        var themeService = AppHost.Get<ThemeService>();
        var theme = new ThemeSettings
        {
            Style = ThemeStyle.MaterialYou,
            Accent = "#7F77DD",
            CornerRadius = 20,
        };

        try
        {
            themeService.Apply(theme, "en");

            Assert.Equal(ThemeStyle.MaterialYou, themeService.CurrentStyle);
            Assert.True(themeService.IsMaterialYou);

            var app = Application.Current!;
            Assert.True(app.Resources.ThemeDictionaries.ContainsKey(ThemeVariant.Light));
            Assert.True(app.Resources.ThemeDictionaries.ContainsKey(ThemeVariant.Dark));

            var lightDict = (ResourceDictionary)app.Resources.ThemeDictionaries[ThemeVariant.Light];
            var darkDict = (ResourceDictionary)app.Resources.ThemeDictionaries[ThemeVariant.Dark];

            // All standard theme brushes exist in both variants
            string[] requiredKeys =
            [
                "PageBrush", "SurfaceBrush", "SurfaceAltBrush", "SubtleBorderBrush",
                "TextPrimaryBrush", "TextSecondaryBrush", "DangerBrush", "SuccessBrush", "WarningBrush",
                "CardShadow", "AccentBrush", "AccentSoftBrush", "AccentFaintBrush", "AccentStrongBrush", "OnAccentBrush"
            ];

            foreach (var key in requiredKeys)
            {
                Assert.True(lightDict.ContainsKey(key), $"Missing key {key} in Light dictionary");
                Assert.True(darkDict.ContainsKey(key), $"Missing key {key} in Dark dictionary");
            }

            // Drop zone in dark mode uses AccentFaintBrush (dark background) and AccentStrongBrush (bright foreground)
            var darkFaint = Assert.IsType<SolidColorBrush>(darkDict["AccentFaintBrush"]);
            var darkStrong = Assert.IsType<SolidColorBrush>(darkDict["AccentStrongBrush"]);
            Assert.True(MaterialYouPalette.Luminance(darkFaint.Color) < 0.25);
            Assert.True(MaterialYouPalette.Luminance(darkStrong.Color) > 0.60);

            // Softness corner radii are applied and follow theme.CornerRadius
            var softRadius = Assert.IsType<CornerRadius>(app.Resources["SoftRadius"]);
            Assert.Equal(20, softRadius.TopLeft);
        }
        finally
        {
            themeService.Apply(new ThemeSettings(), "en");
        }
    }

    [AvaloniaFact]
    public void ThemeService_respects_softness_corner_radius()
    {
        TestServices.EnsureInitialized("en");
        var themeService = AppHost.Get<ThemeService>();
        var app = Application.Current!;

        // Softness = 8
        themeService.Apply(new ThemeSettings { Style = ThemeStyle.MaterialYou, CornerRadius = 8 }, "en");
        Assert.Equal(8, ((CornerRadius)app.Resources["SoftRadius"]!).TopLeft);
        Assert.Equal(4, ((CornerRadius)app.Resources["SoftRadiusSmall"]!).TopLeft);

        // Softness = 28
        themeService.Apply(new ThemeSettings { Style = ThemeStyle.MaterialYou, CornerRadius = 28 }, "en");
        Assert.Equal(28, ((CornerRadius)app.Resources["SoftRadius"]!).TopLeft);
        Assert.Equal(15, ((CornerRadius)app.Resources["SoftRadiusSmall"]!).TopLeft);

        // Reset
        themeService.Apply(new ThemeSettings(), "en");
    }

    [AvaloniaFact]
    public void ThemeService_respects_custom_theme_json_overrides()
    {
        TestServices.EnsureInitialized("en");
        var themeService = AppHost.Get<ThemeService>();
        var app = Application.Current!;

        var custom = new CustomTheme
        {
            Light = new Dictionary<string, string>
            {
                ["surface"] = "#123456",
            },
            Dark = new Dictionary<string, string>
            {
                ["surface"] = "#654321",
            },
        };

        try
        {
            themeService.Apply(new ThemeSettings { Style = ThemeStyle.MaterialYou }, "en", custom);

            var lightDict = (ResourceDictionary)app.Resources.ThemeDictionaries[ThemeVariant.Light];
            var darkDict = (ResourceDictionary)app.Resources.ThemeDictionaries[ThemeVariant.Dark];

            var lightSurface = Assert.IsType<SolidColorBrush>(lightDict["SurfaceBrush"]);
            Assert.Equal(Color.Parse("#123456"), lightSurface.Color);

            var darkSurface = Assert.IsType<SolidColorBrush>(darkDict["SurfaceBrush"]);
            Assert.Equal(Color.Parse("#654321"), darkSurface.Color);
        }
        finally
        {
            themeService.Apply(new ThemeSettings(), "en");
        }
    }

    [AvaloniaFact]
    public void ThemePageViewModel_switches_styles_and_presets()
    {
        TestServices.EnsureInitialized("en");
        var directory = Directory.CreateTempSubdirectory("filee-vm-test-").FullName;
        var store = new UserDataStore(directory);
        store.Load();

        try
        {
            var vm = new ThemePageViewModel(store, AppHost.Get<ILocalizer>(), AppHost.Get<IPlatformServices>());

            // Default style is Soft
            Assert.False(vm.IsMaterialYou);

            // Switch to Material You
            vm.SetStyleCommand.Execute(ThemeStyle.MaterialYou);
            Assert.True(vm.IsMaterialYou);
            Assert.Equal(ThemeStyle.MaterialYou, store.Settings.Theme.Style);

            // Switch preset
            var preset = vm.PastelPresets.First(p => p.Key == "lavender");
            vm.PickPresetCommand.Execute(preset);
            Assert.Equal(preset.Color, vm.Accent);
            Assert.Equal(store.Settings.Theme.Accent, vm.AccentHex);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
