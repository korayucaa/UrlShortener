using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace UrlShortener.Api.Authentication;

public class ApiKeyAuthenticationHandler
    : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private const string ApiKeyHeaderName = "X-API-Key";

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // İstekte API Key başlığı var mı?
        if (!Request.Headers.TryGetValue(
                ApiKeyHeaderName, out var apiKeyHeaderValues))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var providedApiKey = apiKeyHeaderValues.ToString();

        if (string.IsNullOrWhiteSpace(providedApiKey))
        {
            return Task.FromResult(
                AuthenticateResult.Fail("API Key boş olamaz."));
        }

        // Gelen anahtarı kayıtlı anahtarla güvenli şekilde karşılaştır.
        var expectedBytes = Encoding.UTF8.GetBytes(Options.ApiKey);
        var providedBytes = Encoding.UTF8.GetBytes(providedApiKey);

        if (expectedBytes.Length != providedBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(
                expectedBytes, providedBytes))
        {
            return Task.FromResult(
                AuthenticateResult.Fail("Geçersiz API Key."));
        }

        // Anahtar doğruysa kullanıcı kimliği oluştur.
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "ApiKeyUser")
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}