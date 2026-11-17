
using System.ComponentModel.DataAnnotations;

namespace UrlShortener.Api.Dtos
{
    public class RegisterRequest
    {
        [Required]
        [MinLength(3)]
        [MaxLength(30)]
        public string Username { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MinLength(8)]
        public string Password { get; set; } = string.Empty;
    }
}
