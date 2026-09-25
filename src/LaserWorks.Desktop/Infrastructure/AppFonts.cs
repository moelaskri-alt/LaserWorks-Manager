using Avalonia.Media;

namespace LaserWorks.Desktop;

/// <summary>Bundled Noto Sans (Latin) with Noto Sans Arabic fallback, so the UI looks identical on every Windows installation.</summary>
public static class AppFonts
{
    public const string LatinUri = "avares://LaserWorksManager/Assets/Fonts#Noto Sans";
    public const string ArabicUri = "avares://LaserWorksManager/Assets/Fonts#Noto Sans Arabic";

    public static FontManagerOptions Options => new()
    {
        DefaultFamilyName = LatinUri,
        FontFallbacks = new[] { new FontFallback { FontFamily = new FontFamily(ArabicUri) } }
    };
}
