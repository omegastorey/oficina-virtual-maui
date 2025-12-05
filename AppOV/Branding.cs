using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Core.Platform;

public static class Branding
{
    /// <summary>Color por defecto si la web/empresa no provee otro.</summary>
    public static string DefaultHex { get; } = "#153C46";
    //public static string DefaultHex { get; } = "#FF0012";

    private static string _currentHex = DefaultHex;
    public static string CurrentHex => _currentHex;

    public static event EventHandler? Changed;

    /// <summary>Inicializa recursos XAML con el color de marca.</summary>
    public static void Initialize(string? initialHex = null)
    {
        _currentHex = string.IsNullOrWhiteSpace(initialHex) ? DefaultHex : initialHex!;
        ApplyToResources();
    }

    /// <summary>Permite actualizar el color en runtime (p.ej. traído desde la web).</summary>
    public static void Update(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || string.Equals(hex, _currentHex, StringComparison.OrdinalIgnoreCase))
            return;

        _currentHex = hex;
        ApplyToResources();
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Aplica el color a los recursos compartidos.</summary>
    private static void ApplyToResources()
    {
        var color = Color.FromArgb(_currentHex);

        Application.Current!.Resources["BrandPrimaryColor"] = color;
        Application.Current!.Resources["BrandPrimaryBrush"] = new SolidColorBrush(color);
    }

    /// <summary>Pinta status bar y navigation bar con el color actual.</summary>
    public static void ApplySystemBars()
    {
        var color = Color.FromArgb(_currentHex);

#if ANDROID || IOS
        // Status bar
        StatusBar.SetColor(color);
        StatusBar.SetStyle(UseLightContent(color) ? StatusBarStyle.LightContent
                                                  : StatusBarStyle.DarkContent);
#endif

#if ANDROID
        // Navigation bar (Android)
        var a = (byte)(color.Alpha * 255);
        var r = (byte)(color.Red * 255);
        var g = (byte)(color.Green * 255);
        var b = (byte)(color.Blue * 255);
        var activity = Platform.CurrentActivity;
        activity?.Window?.SetNavigationBarColor(Android.Graphics.Color.Argb(a, r, g, b));
#endif
    }

    private static bool UseLightContent(Color c)
    {
        // luminancia aproximada para decidir íconos claros/oscuros
        var l = 0.2126 * c.Red + 0.7152 * c.Green + 0.0722 * c.Blue;
        return l < 0.65; // para colores oscuros devolvemos LightContent
    }
}
