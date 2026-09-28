using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Offload.Crm.Web.Localization;

/// <summary>
/// D-29: MudBlazor's own texts (mostly screen-reader labels such as the dialog close button or the date picker
/// toggle) in Slovak. Keys are MudBlazor's resource names; anything not listed stays MudBlazor's English default.
/// </summary>
internal sealed class SlovakMudLocalizer : MudLocalizer
{
    private static readonly Dictionary<string, string> Texts = new(StringComparer.Ordinal)
    {
        ["MudAlert_Close"] = "Zavrieť",
        ["MudChip_Close"] = "Zavrieť",
        ["MudDialog_Close"] = "Zavrieť",
        ["MudSnackbar_Close"] = "Zavrieť",
        ["MudColorPicker_Close"] = "Zavrieť",
        ["MudBaseDatePicker_Open"] = "Otvoriť",
        ["MudTimePicker_Open"] = "Otvoriť",
        ["MudColorPicker_Open"] = "Otvoriť",
        ["MudBaseDatePicker_NextMonth"] = "Nasledujúci mesiac {0}",
        ["MudBaseDatePicker_PrevMonth"] = "Predchádzajúci mesiac {0}",
        ["MudBaseDatePicker_NextYear"] = "Nasledujúci rok {0}",
        ["MudBaseDatePicker_PrevYear"] = "Predchádzajúci rok {0}",
        ["MudInput_Clear"] = "Vymazať",
        ["MudInput_Increment"] = "Zvýšiť",
        ["MudInput_Decrement"] = "Znížiť",
        ["MudNavGroup_ToggleExpand"] = "Prepnúť {0}",
        ["MudTablePager_FirstPage"] = "Prvá stránka",
        ["MudTablePager_PreviousPage"] = "Predchádzajúca stránka",
        ["MudTablePager_NextPage"] = "Ďalšia stránka",
        ["MudTablePager_LastPage"] = "Posledná stránka",
        ["Converter_InvalidNumber"] = "Neplatné číslo",
        ["Converter_InvalidDateTime"] = "Neplatný dátum a čas",
        ["Converter_InvalidTimeSpan"] = "Neplatný čas",
        ["Converter_InvalidBoolean"] = "Neplatná hodnota áno/nie",
    };

    public override LocalizedString this[string key] =>
        Text.Language == "sk" && Texts.TryGetValue(key, out var value)
            ? new LocalizedString(key, value)
            : base[key];

    public override LocalizedString this[string key, params object[] arguments] =>
        Text.Language == "sk" && Texts.TryGetValue(key, out var value)
            ? new LocalizedString(key, string.Format(System.Globalization.CultureInfo.CurrentCulture, value, arguments))
            : base[key, arguments];
}
