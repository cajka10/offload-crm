using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Offload.Crm.Application.Common;

/// <summary>
/// Validation is done with DataAnnotations on the input DTOs (shared by the Blazor forms and the API),
/// plus business rules in the services (D-12).
/// </summary>
public static class ModelValidator
{
    /// <summary>Validates all DataAnnotations of <paramref name="model"/> and returns errors keyed by property name.</summary>
    public static Dictionary<string, string[]> Validate(object model)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(model);
        var valid = Validator.TryValidateObject(model, context, results, validateAllProperties: true);

        // The framework skips IValidatableObject rules when an attribute failed; run them anyway so the user sees
        // every problem at once (e.g. "name required" and "target before start").
        if (!valid && model is IValidatableObject validatable)
        {
            results.AddRange(validatable.Validate(context).Where(r => !results.Any(e => e.ErrorMessage == r.ErrorMessage)));
        }

        return results
            .SelectMany(r => (r.MemberNames.Any() ? r.MemberNames : [string.Empty]).Select(m => (Member: m, r.ErrorMessage)))
            .GroupBy(x => x.Member)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage ?? "Invalid value.").Distinct().ToArray());
    }

    /// <summary>Throws <see cref="AppValidationException"/> when the model is invalid.</summary>
    public static void ThrowIfInvalid(object model)
    {
        var errors = Validate(model);
        if (errors.Count > 0)
        {
            throw new AppValidationException(errors);
        }
    }
}

/// <summary>Input DTOs that trim/clean themselves before validation (D-12, D-19: normalise first, then validate).</summary>
public interface INormalizable
{
    void Normalize();
}

/// <summary>D-20.4: the value must be a defined member of its enum (rejects e.g. <c>99</c>).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class EnumDefinedAttribute : ValidationAttribute
{
    public EnumDefinedAttribute()
        : base("Select a valid value.")
    {
    }

    public override bool IsValid(object? value) =>
        value is null || (value.GetType().IsEnum && Enum.IsDefined(value.GetType(), value));
}

public static class InputText
{
    /// <summary>Trims the value; empty/whitespace becomes null (D-12).</summary>
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Trims and prepends <c>https://</c> when the scheme is missing (D-12).</summary>
    public static string? CleanUrl(string? value)
    {
        var cleaned = Clean(value);
        if (cleaned is null)
        {
            return null;
        }

        return cleaned.Contains("://", StringComparison.Ordinal) ? cleaned : "https://" + cleaned;
    }
}

/// <summary>
/// E-mail format (D-20.8): exactly one @, no whitespace and none of <c>? < > , ; " ( ) [ ] </c> or <c>:</c>,
/// a dot in the domain and a 2+ letter TLD. Prevents mailto: header injection (BUG-012).
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed partial class EmailFormatAttribute : ValidationAttribute
{
    public EmailFormatAttribute()
        : base("Enter a valid e-mail address.")
    {
    }

    public override bool IsValid(object? value) =>
        value is not string s || string.IsNullOrWhiteSpace(s) || EmailRegex().IsMatch(s.Trim());

    // Local part: no whitespace, @ or ? < > , ; " ( ) [ ] \ :  (D-20.8, BUG-012). Domain: labels + 2+ letter TLD.
    [GeneratedRegex(@"^[^@\s?<>,;""()\[\]\\:]+@[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)*\.[A-Za-z]{2,}$")]
    private static partial Regex EmailRegex();
}

/// <summary>Phone: only <c>+0-9 ()-</c> characters (D-12).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed partial class PhoneCharsAttribute : ValidationAttribute
{
    public PhoneCharsAttribute()
        : base("Phone may contain only digits, spaces and + ( ) -.")
    {
    }

    public override bool IsValid(object? value) =>
        value is not string s || string.IsNullOrWhiteSpace(s) || (PhoneRegex().IsMatch(s) && s.Any(char.IsDigit));

    [GeneratedRegex(@"^[+0-9 ()\-]+$")]
    private static partial Regex PhoneRegex();
}

/// <summary>A valid http(s) URL; a missing scheme is accepted because the service prepends <c>https://</c> (D-12).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class HttpUrlAttribute : ValidationAttribute
{
    public HttpUrlAttribute()
        : base("Enter a valid web address, e.g. https://example.com.")
    {
    }

    public override bool IsValid(object? value)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s))
        {
            return true;
        }

        var url = InputText.CleanUrl(s)!;
        return !url.Any(char.IsWhiteSpace)
            && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && (uri.Host.Contains('.') || uri.IsLoopback);
    }
}

/// <summary>
/// BUG-026: dates must be plausible (2000-01-01 … 2100-12-31) so typos like 0226 instead of 2026 are caught.
/// Works for <see cref="DateOnly"/> and <see cref="DateTime"/> values.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class PlausibleDateAttribute : ValidationAttribute
{
    public static readonly DateOnly Min = new(2000, 1, 1);
    public static readonly DateOnly Max = new(2100, 12, 31);

    public PlausibleDateAttribute()
        : base("Enter a date between 01.01.2000 and 31.12.2100.")
    {
    }

    public override bool IsValid(object? value) => value switch
    {
        DateOnly d => d >= Min && d <= Max,
        DateTime t => DateOnly.FromDateTime(t) >= Min && DateOnly.FromDateTime(t) <= Max,
        _ => true,
    };
}
