using Offload.Crm.Domain.Enums;

namespace Offload.Crm.Domain.Entities;

public class Customer : EntityBase
{
    public const string DefaultCountry = "Czech Republic";

    private string? _registrationNo;

    public string CompanyName { get; set; } = string.Empty;

    /// <summary>IČO. Setting it also maintains <see cref="RegistrationNoNormalized"/>.</summary>
    public string? RegistrationNo
    {
        get => _registrationNo;
        set
        {
            _registrationNo = value;
            RegistrationNoNormalized = NormalizeRegistrationNo(value);
        }
    }

    /// <summary>Upper-cased, whitespace-free copy of <see cref="RegistrationNo"/>; carries the unique index (D-12).</summary>
    public string? RegistrationNoNormalized { get; private set; }

    public string? VatId { get; set; }

    public CustomerStatus Status { get; set; } = CustomerStatus.Lead;

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Website { get; set; }

    public string? Street { get; set; }

    public string? City { get; set; }

    public string? PostalCode { get; set; }

    public string? Country { get; set; } = DefaultCountry;

    public string? Notes { get; set; }

    public ICollection<Contact> Contacts { get; set; } = new List<Contact>();

    public ICollection<Project> Projects { get; set; } = new List<Project>();

    public ICollection<Activity> Activities { get; set; } = new List<Activity>();

    public static string? NormalizeRegistrationNo(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return string.Concat(value.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
    }
}
