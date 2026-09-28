using Microsoft.AspNetCore.Http;
using Offload.Crm.Application.Common;
using Offload.Crm.Application.Customers;
using Offload.Crm.Application.Projects;
using Offload.Crm.Domain.Enums;
using Offload.Crm.Web.Api;

namespace Offload.Crm.Tests.Unit;

/// <summary>T-38 shared rules: paging clamp (D-20.2), enum checks (D-20.4), e-mail format (D-20.8), API error text (D-20.7).</summary>
public class SharedRulesTests
{
    [Theory]
    [InlineData(-5, -1, 1, PagedQuery.DefaultPageSize)]
    [InlineData(0, 0, 1, PagedQuery.DefaultPageSize)]
    [InlineData(3, 100_000, 3, PagedQuery.MaxPageSize)]
    [InlineData(int.MaxValue, 50, int.MaxValue, 50)]
    public void Paging_parameters_are_clamped(int page, int size, int expectedPage, int expectedSize)
    {
        var query = new CustomerListQuery { Page = page, PageSize = size };

        Assert.Equal(expectedPage, query.NormalizedPage);
        Assert.Equal(expectedSize, query.NormalizedPageSize);
    }

    [Theory]
    [InlineData("sales@firma.cz?cc=attacker@evil.com")]
    [InlineData("x@y.cz?subject=<b>")]
    [InlineData("a,b@firma.cz")]
    [InlineData("a;b@firma.cz")]
    [InlineData("\"quoted\"@firma.cz")]
    [InlineData("a(b)@firma.cz")]
    [InlineData("a[b]@firma.cz")]
    [InlineData(@"a\b@firma.cz")]
    [InlineData("a@b@firma.cz")]
    [InlineData("a@firma")]
    [InlineData("a@firma.c")]
    [InlineData("a@-firma.cz")]
    [InlineData("a b@firma.cz")]
    [InlineData("mailto:a@firma.cz")]
    public void Email_rejects_header_injection_and_invalid_characters(string email)
    {
        var errors = ModelValidator.Validate(new CustomerInput { CompanyName = "X", Email = email });

        Assert.Contains(nameof(CustomerInput.Email), errors.Keys);
    }

    [Theory]
    [InlineData("info@firma.cz")]
    [InlineData("jan.novak+crm@sub.firma-test.co.uk")]
    [InlineData("o'brien@firma.com")]
    public void Email_accepts_normal_addresses(string email)
    {
        var errors = ModelValidator.Validate(new CustomerInput { CompanyName = "X", Email = email });

        Assert.DoesNotContain(nameof(CustomerInput.Email), errors.Keys);
    }

    [Fact]
    public void Undefined_enum_values_fail_validation()
    {
        var customer = ModelValidator.Validate(new CustomerInput { CompanyName = "X", Status = (CustomerStatus)99 });
        var project = ModelValidator.Validate(new ProjectInput { Name = "P", CustomerId = 1, Priority = (Priority)42 });

        Assert.Contains(nameof(CustomerInput.Status), customer.Keys);
        Assert.Contains(nameof(ProjectInput.Priority), project.Keys);
        Assert.Throws<AppValidationException>(() => Guard.EnumDefined<CustomerStatus>((CustomerStatus)99, "status"));
        Assert.Throws<AppValidationException>(() => Guard.EnumsDefined<TaskItemStatus>([TaskItemStatus.Done, (TaskItemStatus)7], "status"));
        Guard.EnumDefined<CustomerStatus>(null, "status");
        Guard.EnumDefined<CustomerStatus>(CustomerStatus.Inactive, "status");
    }

    [Fact]
    public void Bad_json_is_described_with_its_path()
    {
        var ex = new BadHttpRequestException(
            "Failed to read parameter \"CustomerInput input\" from the request body as JSON.",
            new System.Text.Json.JsonException("The JSON value could not be converted.", "$.companyName", 0, 17));

        var detail = ApiErrorMiddleware.Describe(ex);

        Assert.Contains("$.companyName", detail);
        Assert.DoesNotContain("System.", detail);
    }

    [Fact]
    public void Missing_body_is_described()
    {
        var ex = new BadHttpRequestException("Required parameter \"CustomerInput input\" was not provided from body.");

        Assert.Equal("A JSON request body is required.", ApiErrorMiddleware.Describe(ex));
    }
}
