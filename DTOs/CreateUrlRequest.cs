using System.ComponentModel.DataAnnotations;

namespace UrlShortener.Api.DTOs
{
    public class CreateUrlRequest
    {
        [Required(ErrorMessage = "URL alanı boş olamaz.")]
        [Url(ErrorMessage = "Geçerli bir URL girin.")]
        [HttpOrHttpsUrl]
        public string OriginalUrl { get; set; } = string.Empty;

        [MaxLength(30, ErrorMessage = "Kısa kod en fazla 30 karakter olabilir.")]
        [RegularExpression(
           @"^[a-zA-Z0-9-]+$",
           ErrorMessage = "Kısa kod yalnızca harf, rakam ve tire içerebilir.")]
        public string? CustomShortCode { get; set; }

        public DateTime? ExpiresAt { get; set; }
    }

}
