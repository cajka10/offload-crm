using System.Globalization;

namespace Offload.Crm.Web.Ui;

/// <summary>
/// D-28: regional settings — Slovak formatting (sk-SK) and euro amounts. The UI texts stay English (D-11).
/// One place for the culture and the currency; everything that formats numbers, dates or money uses it.
/// </summary>
public static class AppCulture
{
    public const string Name = "sk-SK";

    /// <summary>ISO 4217 code (shown in Settings → General).</summary>
    public const string CurrencyCode = "EUR";

    public const string CurrencySymbol = "€";

    public static CultureInfo Culture { get; } = CultureInfo.GetCultureInfo(Name);
}
