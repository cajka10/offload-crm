using System.Globalization;

namespace Offload.Crm.Web.Ui;

/// <summary>Display formatting (D-11/D-28): sk-SK, dates dd.MM.yyyy, local time Europe/Bratislava, amounts in EUR.</summary>
public static class Fmt
{
    public const string Empty = "—";

    private static CultureInfo Culture => AppCulture.Culture;
    public static System.DateTime ToLocal(System.DateTime utc) => Offload.Crm.Application.Common.AppTime.ToLocal(utc);

    public static string Date(DateTime? utc) =>
        utc is null ? Empty : ToLocal(utc.Value).ToString("dd.MM.yyyy", Culture);

    public static string DateTimeText(DateTime? utc) =>
        utc is null ? Empty : ToLocal(utc.Value).ToString("dd.MM.yyyy HH:mm", Culture);

    public static string Date(DateOnly? date) =>
        date is null ? Empty : date.Value.ToString("dd.MM.yyyy", Culture);

    /// <summary>e.g. <c>1 250 000 €</c>, or <c>1 250,50 €</c> when there are cents (D-28).</summary>
    public static string Money(decimal? value) =>
        value is null
            ? Empty
            : value.Value.ToString(value.Value % 1 == 0 ? "#,##0" : "#,##0.00", Culture) + " " + AppCulture.CurrencySymbol;

    /// <summary>"just now", "5 min ago", "2 h ago", "3 days ago", or the date for older values.</summary>
    public static string Relative(System.DateTime utc, TimeProvider clock)
    {
        var age = clock.GetUtcNow().UtcDateTime - System.DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return age.TotalMinutes switch
        {
            < 1 => Localization.Text.T("just now"),
            < 60 => Localization.Text.T("{0} min ago", (int)age.TotalMinutes),
            < 60 * 24 => Localization.Text.T("{0} h ago", (int)age.TotalHours),
            < 60 * 24 * 30 => (int)age.TotalDays == 1 ? Localization.Text.T("1 day ago") : Localization.Text.T("{0} days ago", (int)age.TotalDays),
            _ => Date(utc),
        };
    }

    public static string OrDash(string? value) => string.IsNullOrWhiteSpace(value) ? Empty : value;
}
