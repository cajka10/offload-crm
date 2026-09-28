using System.ComponentModel.DataAnnotations;
using Offload.Crm.Application.Common;
using Offload.Crm.Domain.Entities;
using Offload.Crm.Domain.Enums;

namespace Offload.Crm.Application.Customers;

/// <summary>Sort keys accepted by <see cref="ICustomerService.ListAsync"/> (case-insensitive).</summary>
public static class CustomerSort
{
    public const string CompanyName = "companyName";
    public const string RegistrationNo = "registrationNo";
    public const string City = "city";
    public const string Status = "status";
    public const string Email = "email";
    public const string Phone = "phone";
    public const string ProjectCount = "projectCount";
    public const string LastActivity = "lastActivity";
    public const string Created = "created";
}

public sealed class CustomerListQuery : PagedQuery
{
    public CustomerStatus? Status { get; set; }
}

public sealed record CustomerListItem(
    int Id,
    string CompanyName,
    string? RegistrationNo,
    string? City,
    CustomerStatus Status,
    string? Email,
    string? Phone,
    int ProjectCount,
    DateTime? LastActivityAt,
    DateTime CreatedAt);

public sealed record CustomerDetail(
    int Id,
    string CompanyName,
    string? RegistrationNo,
    string? VatId,
    CustomerStatus Status,
    string? Email,
    string? Phone,
    string? Website,
    string? Street,
    string? City,
    string? PostalCode,
    string? Country,
    string? Notes,
    int ProjectCount,
    int ContactCount,
    DateTime? LastActivityAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int ActiveProjectCount = 0,
    int OpenTaskCount = 0,
    decimal PipelineValue = 0);

/// <summary>Create/update payload; used as the form model in the UI and the request body in the API (D-12).</summary>
public sealed class CustomerInput : INormalizable
{
    [Required(ErrorMessage = "Company name is required.")]
    [StringLength(200, ErrorMessage = "Company name can be at most 200 characters.")]
    public string? CompanyName { get; set; }

    [StringLength(20, ErrorMessage = "Registration no. (IČO) can be at most 20 characters.")]
    public string? RegistrationNo { get; set; }

    [StringLength(20, ErrorMessage = "VAT ID can be at most 20 characters.")]
    public string? VatId { get; set; }

    [EnumDefined(ErrorMessage = "Select a valid status.")]
    public CustomerStatus Status { get; set; } = CustomerStatus.Lead;

    [StringLength(254, ErrorMessage = "E-mail can be at most 254 characters.")]
    [EmailFormat]
    public string? Email { get; set; }

    [StringLength(30, ErrorMessage = "Phone can be at most 30 characters.")]
    [PhoneChars]
    public string? Phone { get; set; }

    [StringLength(500, ErrorMessage = "Website can be at most 500 characters.")]
    [HttpUrl]
    public string? Website { get; set; }

    [StringLength(200, ErrorMessage = "Street can be at most 200 characters.")]
    public string? Street { get; set; }

    [StringLength(200, ErrorMessage = "City can be at most 200 characters.")]
    public string? City { get; set; }

    [StringLength(200, ErrorMessage = "Postal code can be at most 200 characters.")]
    public string? PostalCode { get; set; }

    [StringLength(200, ErrorMessage = "Country can be at most 200 characters.")]
    public string? Country { get; set; } = Customer.DefaultCountry;

    [StringLength(4000, ErrorMessage = "Notes can be at most 4000 characters.")]
    public string? Notes { get; set; }

    /// <summary>Trims strings, turns empty strings into null and prefixes the website scheme (D-12).</summary>
    public void Normalize()
    {
        CompanyName = InputText.Clean(CompanyName);
        RegistrationNo = InputText.Clean(RegistrationNo);
        VatId = InputText.Clean(VatId);
        Email = InputText.Clean(Email);
        Phone = InputText.Clean(Phone);
        Website = InputText.CleanUrl(Website);
        Street = InputText.Clean(Street);
        City = InputText.Clean(City);
        PostalCode = InputText.Clean(PostalCode);
        Country = InputText.Clean(Country);
        Notes = InputText.Clean(Notes);
    }

    public static CustomerInput From(CustomerDetail c) => new()
    {
        CompanyName = c.CompanyName,
        RegistrationNo = c.RegistrationNo,
        VatId = c.VatId,
        Status = c.Status,
        Email = c.Email,
        Phone = c.Phone,
        Website = c.Website,
        Street = c.Street,
        City = c.City,
        PostalCode = c.PostalCode,
        Country = c.Country,
        Notes = c.Notes,
    };
}
