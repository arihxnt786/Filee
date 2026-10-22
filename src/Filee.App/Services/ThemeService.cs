// Applies ThemeSettings (light/dark, accent colour, corner radius, font order and reading direction, reduced motion) and the user's
// theme.json colours at runtime by writing resources into Application.Resources. All styles reference these with
// DynamicResource.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Filee.Core.Platform;
using Filee.Core.Settings;

namespace Filee.App.Services;

/// <summary>Runtime theming.</summary>
public sealed class ThemeService
{
    private readonly IPlatformServices _platform;

    public ThemeService(IPlatformServices platform)
    {
        _platform = platform;
        Motion.WindowTracked = window => window.Classes.Set("material-you", IsMaterialYou);
    }

    /// <summary>Accent swatches offered in the Theme page.</summary>
    public static readonly IReadOnlyList<string> Swatches =
        ["#7F77DD", "#1D9E75", "#D85A30", "#378ADD", "#D4537E", "#BA7517", "#639922", "#5F5E5A"];

    /// <summary>Curated theme preset with coordinated light and dark accent colours.</summary>
    public sealed record ThemePreset(string Key, string LightHex, string DarkHex)
    {
        public Color LightColor => Color.Parse(LightHex);
        public Color DarkColor => Color.Parse(DarkHex);
        public Color ActiveColor(bool isDark) => isDark ? DarkColor : LightColor;
    }

    /// <summary>Curated pastel and monochrome presets offered in the Theme page.</summary>
    public static readonly IReadOnlyList<ThemePreset> Presets =
    [
        new("monochrome", "#1F1E1D", "#F1EFE8"),
        new("lavender", "#7F77DD", "#BCAEE5"),
        new("peach", "#E07A70", "#F29B91"),
        new("mint", "#529B7D", "#88D5B1"),
        new("rose", "#E86483", "#F79BB0"),
        new("sky", "#5B8BE3", "#98B8F6"),
        new("amber", "#CB852A", "#F0BA6A"),
        new("sage", "#5A8E51", "#8DC983"),
    ];

    /// <summary>Currently applied theme style.</summary>
    public ThemeStyle CurrentStyle { get; private set; } = ThemeStyle.Soft;

    /// <summary>True when Material You is currently applied.</summary>
    public bool IsMaterialYou => CurrentStyle == ThemeStyle.MaterialYou;

    /// <summary>
    /// theme.json colour names → the brushes in Styles/Soft.axaml they replace. Must cover exactly
    /// <see cref="CustomTheme.Keys"/> (a test checks).
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> CustomBrushKeys = new Dictionary<string, string>
    {
        ["page"] = "PageBrush",
        ["surface"] = "SurfaceBrush",
        ["surfaceAlt"] = "SurfaceAltBrush",
        ["border"] = "SubtleBorderBrush",
        ["text"] = "TextPrimaryBrush",
        ["textSecondary"] = "TextSecondaryBrush",
        ["danger"] = "DangerBrush",
        ["success"] = "SuccessBrush",
        ["warning"] = "WarningBrush",
    };

    public void Apply(ThemeSettings theme, string language, CustomTheme? custom = null)
    {
        var app = Application.Current ?? throw new InvalidOperationException("No application.");
        var res = app.Resources;

        CurrentStyle = theme.Style;

        app.RequestedThemeVariant = theme.Mode switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };

        var accent = Color.TryParse(theme.Accent, out var parsed) ? parsed : Color.Parse(SoftPalette.DefaultAccent);

        // Accent colour + derived shades, corner radii and the font order (shared with the installer).
        SoftPalette.ApplyAccent(res, accent);
        SoftPalette.ApplyCornerRadius(res, theme.CornerRadius);
        res["AppFontFamily"] = SoftPalette.FontFamilyFor(language);
        SoftPalette.ApplyFlowDirection(res, language);

        if (theme.Style == ThemeStyle.MaterialYou)
        {
            var lightPalette = MaterialYouPalette.Generate(accent, isDark: false);
            var darkPalette = MaterialYouPalette.Generate(accent, isDark: true);

            var lightDict = lightPalette.ToResourceDictionary();
            var darkDict = darkPalette.ToResourceDictionary();

            ApplyCustomOverrides(lightDict, custom?.Light);
            ApplyCustomOverrides(darkDict, custom?.Dark);

            res.ThemeDictionaries[ThemeVariant.Light] = lightDict;
            res.ThemeDictionaries[ThemeVariant.Dark] = darkDict;
        }
        else
        {
            if (MaterialYouPalette.IsMonochrome(accent))
            {
                var lightDict = new ResourceDictionary();
                var darkDict = new ResourceDictionary();
                SoftPalette.ApplyMonochromeAccent(lightDict, isDark: false);
                SoftPalette.ApplyMonochromeAccent(darkDict, isDark: true);
                ApplyCustomOverrides(lightDict, custom?.Light);
                ApplyCustomOverrides(darkDict, custom?.Dark);
                res.ThemeDictionaries[ThemeVariant.Light] = lightDict;
                res.ThemeDictionaries[ThemeVariant.Dark] = darkDict;
            }
            else
            {
                ApplyCustom(res, ThemeVariant.Light, custom?.Light);
                ApplyCustom(res, ThemeVariant.Dark, custom?.Dark);
            }
        }

        var isMaterialYou = theme.Style == ThemeStyle.MaterialYou;
        Motion.ForEachTracked(w => w.Classes.Set("material-you", isMaterialYou));
        if (app.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var window in desktop.Windows)
                window.Classes.Set("material-you", isMaterialYou);
        }

        Motion.Configure(theme.ReduceMotion, () => _platform.PrefersReducedMotion);
    }

    /// <summary>The UI font chain for <paramref name="language"/> (see <see cref="SoftPalette.FontFamilies"/>).</summary>
    internal static string FontFamilies(string language) => SoftPalette.FontFamilies(language);

    private static void ApplyCustomOverrides(IResourceDictionary dictionary, IReadOnlyDictionary<string, string>? colors)
    {
        foreach (var (name, value) in colors ?? new Dictionary<string, string>())
            if (CustomBrushKeys.TryGetValue(name, out var key) && Color.TryParse(value, out var color))
                dictionary[key] = new SolidColorBrush(color);
    }

    /// <summary>Replaces (or, with no colours, removes) the user's colours for one variant.</summary>
    private static void ApplyCustom(IResourceDictionary res, ThemeVariant variant, IReadOnlyDictionary<string, string>? colors)
    {
        var dictionary = new ResourceDictionary();
        ApplyCustomOverrides(dictionary, colors);

        if (dictionary.Count == 0)
            res.ThemeDictionaries.Remove(variant);
        else
            res.ThemeDictionaries[variant] = dictionary;
    }

    /// <summary>Lightens (amount &gt; 0) or darkens (amount &lt; 0) a colour.</summary>
    public static Color Shade(Color c, double amount) => SoftPalette.Shade(c, amount);

    /// <summary>Relative luminance 0..1 (sRGB approximation).</summary>
    public static double Luminance(Color c) => SoftPalette.Luminance(c);
}
