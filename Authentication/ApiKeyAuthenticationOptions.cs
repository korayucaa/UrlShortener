using Microsoft.AspNetCore.Authentication;

namespace UrlShortener.Api.Authentication;

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public string ApiKey { get; set; } = string.Empty;
}