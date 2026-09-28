using MudBlazor;
using Offload.Crm.Domain.Enums;

namespace Offload.Crm.Web.Ui;

/// <summary>Brand theme (D-15): primary #0F5132, secondary #E8912D, light only.</summary>
public static class AppTheme
{
    public const string PrimaryColor = "#0F5132";
    public const string SecondaryColor = "#E8912D";

    public static readonly MudTheme Theme = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = PrimaryColor,
            Secondary = SecondaryColor,
            Tertiary = "#1B7A4F",
            AppbarBackground = PrimaryColor,
            AppbarText = "#FFFFFF",
            DrawerBackground = "#FFFFFF",
            DrawerIcon = PrimaryColor,
            Background = "#F6F8F7",
            ActionDefault = "#5F6B66",
        },
        LayoutProperties = new LayoutProperties
        {
            DrawerWidthLeft = "232px",
            DefaultBorderRadius = "6px",
        },
    };

    public static Color StatusColor(CustomerStatus status) => status switch
    {
        CustomerStatus.Active => Color.Success,
        CustomerStatus.Lead => Color.Info,
        _ => Color.Default,
    };
}
