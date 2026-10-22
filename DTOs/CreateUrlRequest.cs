using System.ComponentModel.DataAnnotations;

namespace UrlShortener.Api.DTOs
{
    public class CreateUrlRequest
    {
        [Required(ErrorMessage = "URL alanı boş olamaz.")]
        [Url(ErrorMessage = "Geçerli bir URL girin.")]
        [HttpOrHttpsUrl]
        public string OriginalUrl { get; set; } = string.Empty;
    }

}
