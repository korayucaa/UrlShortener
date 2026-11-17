namespace UrlShortener.Api.Models
{
    public class ShortUrl
    {
        public int Id { get; set; }

        public string OriginalUrl { get; set; } = string.Empty;

        public string ShortCode { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public DateTime? ExpiresAt { get; set; }

        public int ClickCount { get; set; }
        public int? UserId { get; set; }

        public User? User { get; set; }

    }
}
