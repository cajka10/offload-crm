namespace Offload.Crm.Application.Common;

/// <summary>Base type for expected business errors thrown by services. Mapped to ProblemDetails by the API.</summary>
public abstract class AppException : Exception
{
    protected AppException(string message)
        : base(message)
    {
    }
}

/// <summary>Input failed validation (HTTP 400). <see cref="Errors"/> is keyed by input property name.</summary>
public sealed class AppValidationException : AppException
{
    public AppValidationException(IDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = new Dictionary<string, string[]>(errors);
    }

    public AppValidationException(string field, string error)
        : this(new Dictionary<string, string[]> { [field] = [error] })
    {
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

/// <summary>The requested entity doesn't exist (HTTP 404).</summary>
public sealed class NotFoundException : AppException
{
    public NotFoundException(string entityName, object key)
        : base($"{entityName} {key} was not found.")
    {
    }
}

/// <summary>The operation conflicts with the current state, e.g. delete is blocked (HTTP 409).</summary>
public sealed class ConflictException : AppException
{
    public ConflictException(string message)
        : base(message)
    {
    }
}

/// <summary>The caller's session is no longer valid (signed out elsewhere, deactivated). HTTP 401.</summary>
public sealed class SessionExpiredException : AppException
{
    public SessionExpiredException()
        : base("Your session has ended. Please sign in again.")
    {
    }
}

/// <summary>Validation helpers for values that don't come through a DataAnnotations-validated DTO (e.g. query filters).</summary>
public static class Guard
{
    /// <summary>D-20.4: undefined enum values (e.g. <c>99</c>) are rejected with a field error.</summary>
    public static void EnumDefined<TEnum>(TEnum? value, string field)
        where TEnum : struct, Enum
    {
        if (value is { } v && !Enum.IsDefined(v))
        {
            throw new AppValidationException(field, $"'{v}' is not a valid value. Allowed: {string.Join(", ", Enum.GetNames<TEnum>())}.");
        }
    }

    public static void EnumsDefined<TEnum>(IEnumerable<TEnum>? values, string field)
        where TEnum : struct, Enum
    {
        foreach (var value in values ?? [])
        {
            EnumDefined<TEnum>(value, field);
        }
    }
}

/// <summary>The signed-in user may not perform the operation (HTTP 403).</summary>
public sealed class ForbiddenException : AppException
{
    public ForbiddenException(string message)
        : base(message)
    {
    }
}
