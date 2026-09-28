using Offload.Crm.Application.Common;
using Offload.Crm.Application.Contacts;
using Offload.Crm.Application.Customers;
using Offload.Crm.Domain.Entities;
using Offload.Crm.Domain.Enums;

namespace Offload.Crm.Tests.Unit;

/// <summary>D-12 validation rules on the input DTOs (no database needed).</summary>
public class ValidationTests
{
    private static Dictionary<string, string[]> Validate(CustomerInput input)
    {
        input.Normalize();
        return ModelValidator.Validate(input);
    }

    [Fact]
    public void Valid_customer_has_no_errors()
    {
        var errors = Validate(new CustomerInput
        {
            CompanyName = "Stavby Řehoř s.r.o.",
            RegistrationNo = "25596641",
            Email = "info@stavby.cz",
            Phone = "+420 (541) 123-456",
            Website = "stavby.cz",
        });

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Company_name_is_required(string? name)
    {
        var errors = Validate(new CustomerInput { CompanyName = name });

        Assert.Contains(nameof(CustomerInput.CompanyName), errors.Keys);
    }

    [Fact]
    public void Company_name_max_200()
    {
        Assert.Empty(Validate(new CustomerInput { CompanyName = new string('a', 200) }));
        Assert.Contains(nameof(CustomerInput.CompanyName), Validate(new CustomerInput { CompanyName = new string('a', 201) }).Keys);
    }

    [Theory]
    [InlineData("bad")]
    [InlineData("bad@")]
    [InlineData("a@b")]
    [InlineData("a b@c.cz")]
    public void Invalid_email_is_rejected(string email)
    {
        var errors = Validate(new CustomerInput { CompanyName = "X", Email = email });

        Assert.Contains(nameof(CustomerInput.Email), errors.Keys);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("+420 123 x")]
    [InlineData("---")]
    public void Phone_allows_only_digits_spaces_plus_parentheses_dash(string phone)
    {
        var errors = Validate(new CustomerInput { CompanyName = "X", Phone = phone });

        Assert.Contains(nameof(CustomerInput.Phone), errors.Keys);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://example.cz")]
    [InlineData("https://nodot")]
    public void Invalid_website_is_rejected(string website)
    {
        var errors = Validate(new CustomerInput { CompanyName = "X", Website = website });

        Assert.Contains(nameof(CustomerInput.Website), errors.Keys);
    }

    [Fact]
    public void Normalize_trims_turns_empty_into_null_and_prefixes_https()
    {
        var input = new CustomerInput { CompanyName = "  Firma  ", VatId = "   ", Website = " example.cz ", City = "" };

        input.Normalize();

        Assert.Equal("Firma", input.CompanyName);
        Assert.Null(input.VatId);
        Assert.Null(input.City);
        Assert.Equal("https://example.cz", input.Website);
    }

    [Fact]
    public void Normalize_keeps_existing_scheme()
    {
        var input = new CustomerInput { CompanyName = "X", Website = "http://example.cz" };

        input.Normalize();

        Assert.Equal("http://example.cz", input.Website);
    }

    [Fact]
    public void Registration_no_max_20()
    {
        var errors = Validate(new CustomerInput { CompanyName = "X", RegistrationNo = new string('1', 21) });

        Assert.Contains(nameof(CustomerInput.RegistrationNo), errors.Keys);
    }

    [Theory]
    [InlineData("255 966 41", "25596641")]
    [InlineData(" cz123 ", "CZ123")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Registration_no_is_normalized_for_uniqueness(string? input, string? expected)
    {
        Assert.Equal(expected, Customer.NormalizeRegistrationNo(input));
        Assert.Equal(expected, new Customer { RegistrationNo = input }.RegistrationNoNormalized);
    }

    [Fact]
    public void Contact_requires_last_name_but_not_first_name()
    {
        var input = new ContactInput { FirstName = "Jan", LastName = "  " };
        input.Normalize();

        var errors = ModelValidator.Validate(input);

        Assert.Equal([nameof(ContactInput.LastName)], errors.Keys);
    }

    [Fact]
    public void New_customer_defaults_follow_decisions()
    {
        var input = new CustomerInput();

        Assert.Equal(CustomerStatus.Lead, input.Status); // D-04
        Assert.Equal("Czech Republic", input.Country); // D-12
    }
}
