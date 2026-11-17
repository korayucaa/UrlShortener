namespace UrlShortener.Api.DTOs
{
    public class UrlAnalyticsResponse
    {
        public string ShortCode { get; set; } = string.Empty;

        public int TotalClicks { get; set; }

        public List<DateTime> RecentClicks { get; set; } = new();
        public List<DailyClickStat> DailyClicks { get; set; } = new();
    }
}