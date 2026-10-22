
using System.ComponentModel.DataAnnotations;

namespace UrlShortener.Api.DTOs
{
    public class HttpOrHttpsUrlAttribute : ValidationAttribute
    {
        public HttpOrHttpsUrlAttribute()
        {
            ErrorMessage = "Geçerli bir HTTP veya HTTPS adresi girin.";
        }

        public override bool IsValid(object? value)
        {
            if (value is not string url)
                return false;

            return Uri.TryCreate(
                       url,
                       UriKind.Absolute,
                       out var uri)
                   && (uri.Scheme == Uri.UriSchemeHttp
                       || uri.Scheme == Uri.UriSchemeHttps);
        }
    }
}
