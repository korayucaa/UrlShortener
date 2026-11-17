namespace UrlShortener.Api.Models;

public class UrlClick
{
    public int Id { get; set; }

    public int ShortUrlId { get; set; }

    public DateTime ClickedAt { get; set; }

    // Hangi kısa URL'ye ait olduğunu belirtir.
    public ShortUrl ShortUrl { get; set; } = null!;
}