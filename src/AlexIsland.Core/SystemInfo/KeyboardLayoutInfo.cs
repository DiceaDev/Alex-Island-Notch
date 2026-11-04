using System.Globalization;

namespace AlexIsland.Core.SystemInfo;

public static class KeyboardLayoutInfo
{
    /// <summary>
    /// Convierte el identificador de idioma (palabra baja del HKL) en un código
    /// corto ("ES") y un nombre legible ("español (Chile)").
    /// </summary>
    public static (string Code, string Name) Describe(int languageId)
    {
        if (languageId <= 0) return ("??", "Idioma desconocido");

        try
        {
            var culture = CultureInfo.GetCultureInfo(languageId);
            var code = culture.TwoLetterISOLanguageName.ToUpperInvariant();
            var name = culture.NativeName;
            return (code.Length is > 0 and <= 3 ? code : "??", string.IsNullOrWhiteSpace(name) ? culture.Name : name);
        }
        catch (CultureNotFoundException)
        {
            return ("??", $"Idioma 0x{languageId:X4}");
        }
    }

    /// <summary>Palabra baja del HKL, que identifica el idioma de entrada.</summary>
    public static int LanguageIdFromLayout(long hkl) => (int)(hkl & 0xFFFF);
}
