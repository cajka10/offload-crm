using Offload.Crm.Web.Ui;

namespace Offload.Crm.Tests.Unit;

/// <summary>D-28: Slovak formatting and euro amounts.</summary>
public sealed class FormattingTests
{
    private const char Nbsp = ' ';

    [Theory]
    [InlineData(1250000, "1 250 000 €")]
    [InlineData(1250.5, "1 250,50 €")]
    [InlineData(0, "0 €")]
    [InlineData(999.99, "999,99 €")]
    public void Money_uses_slovak_separators_and_the_euro_sign(double value, string expected)
    {
        // sk-SK groups thousands with a non-breaking space; normalize so the expectation stays readable.
        var text = Fmt.Money((decimal)value).Replace(' ', ' ').Replace(Nbsp, ' ');

        Assert.Equal(expected, text);
    }

    [Fact]
    public void Money_keeps_the_symbol_on_the_same_line_and_shows_a_dash_for_no_value()
    {
        Assert.EndsWith(Nbsp + "€", Fmt.Money(10));
        Assert.Equal(Fmt.Empty, Fmt.Money(null));
    }

    [Fact]
    public void Dates_use_the_day_month_year_format()
    {
        Assert.Equal("05.03.2026", Fmt.Date(new DateOnly(2026, 3, 5)));
        Assert.Equal("sk-SK", AppCulture.Culture.Name);
        Assert.Equal("EUR", AppCulture.CurrencyCode);
    }
}
