using Offload.Crm.Web.Components.Account;

namespace Offload.Crm.Tests.Unit;

/// <summary>The login page only redirects to local app paths (no open redirects).</summary>
public class ReturnUrlTests
{
    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("/customers?search=x", "/customers?search=x")]
    [InlineData("https://evil.example", "/")]
    [InlineData("//evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("/account/logout", "/")]
    public void Only_local_paths_are_allowed(string? input, string expected)
    {
        Assert.Equal(expected, Login.SafeReturnUrl(input));
    }
}
