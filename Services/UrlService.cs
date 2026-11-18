
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;
using UrlShortener.Api.Data;
using UrlShortener.Api.DTOs;
using UrlShortener.Api.Models;

namespace UrlShortener.Api.Services
{
    public class UrlService
    {
        private static readonly TimeSpan ClickDedupWindow = TimeSpan.FromSeconds(10);

        private readonly AppDbContext _context;
        private readonly IDatabase _cache;
        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<UrlService> _logger;
        public UrlService(
            AppDbContext context,
            IConnectionMultiplexer redis,
            IMemoryCache memoryCache,
            ILogger<UrlService> logger)
        {
            _context = context;
            _cache = redis.GetDatabase();
            _memoryCache = memoryCache;
            _logger = logger;
        }

        public async Task<ShortUrl> CreateUrlAsync(
            string originalUrl,
            string? customShortCode = null,
            DateTime? expiresAt = null,
            int? userId = null)
            {
                if (string.IsNullOrWhiteSpace(originalUrl))
                {
                    throw new ArgumentException("URL boş olamaz.");
                }

                originalUrl = originalUrl.Trim();

                if (!Uri.TryCreate(originalUrl, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp &&
                     uri.Scheme != Uri.UriSchemeHttps))
                {
                    throw new ArgumentException(
                        "Geçerli bir HTTP veya HTTPS adresi girilmelidir.");
                }

                // Son kullanma tarihini UTC'ye dönüştür.
                DateTime? expirationUtc = null;

                if (expiresAt.HasValue)
                {
                    expirationUtc = expiresAt.Value.Kind switch
                    {
                        DateTimeKind.Utc => expiresAt.Value,
                        DateTimeKind.Local => expiresAt.Value.ToUniversalTime(),
                        _ => DateTime.SpecifyKind(
                            expiresAt.Value, DateTimeKind.Utc)
                    };

                    if (expirationUtc <= DateTime.UtcNow)
                    {
                        throw new ArgumentException(
                            "Son kullanma tarihi gelecekte olmalıdır.");
                    }
                }

                string shortCode;

                // Kullanıcı özel kod belirlemiş mi?
                if (!string.IsNullOrWhiteSpace(customShortCode))
                {
                    shortCode = customShortCode.Trim().ToLowerInvariant();

                    if (shortCode.Length > 30 ||
                        !System.Text.RegularExpressions.Regex.IsMatch(
                            shortCode, @"^[a-zA-Z0-9-]+$"))
                    {
                        throw new ArgumentException(
                            "Kısa kod 30 karakteri geçemez ve yalnızca harf, rakam ve tire içerebilir.");
                    }

                    // Kod daha önce kullanılmış mı?
                    bool codeExists = await _context.ShortUrls
                        .AnyAsync(u => u.ShortCode == shortCode);

                    if (codeExists)
                    {
                        throw new ArgumentException(
                            "Bu kısa kod zaten kullanılıyor.");
                    }
                }
                else
                {
                    // Özel kod yoksa otomatik kod üret.
                    shortCode = await GenerateUniqueShortCodeAsync();
                }

                var shortUrl = new ShortUrl
                {
                    OriginalUrl = originalUrl,
                    ShortCode = shortCode,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = expirationUtc,
                    ClickCount = 0,
                    UserId = userId
                };

                _context.ShortUrls.Add(shortUrl);
                await _context.SaveChangesAsync();

                return shortUrl;
        }

        public async Task<List<ShortUrl>> GetUrlsAsync(int? userId = null)
        {
            var query = _context.ShortUrls.AsQueryable();

            if (userId.HasValue)
            {
                query = query.Where(url => url.UserId == userId.Value);
            }

            return await query.ToListAsync();
        }



        public async Task<ShortUrl?> GetUrlByCodeAsync(string shortCode)
        {
            string cacheKey = $"shorturl:{shortCode}";

            // 1. Önce Redis önbelleğini kontrol et.
            try
            {
                RedisValue cachedUrl = await _cache.StringGetAsync(cacheKey);

                _logger.LogInformation(
                    "REDIS GET: {CacheKey}, bulundu mu: {Found}",
                    cacheKey,
                    cachedUrl.HasValue);

                if (cachedUrl.HasValue)
                {
                    var cachedShortUrl =
                        JsonSerializer.Deserialize<ShortUrl>(cachedUrl.ToString());

                    if (cachedShortUrl != null)
                    {
                        // Süre ve tıklama sayısını PostgreSQL'den kontrol et.
                        var dbInfo = await _context.ShortUrls
                            .AsNoTracking()
                            .Where(u => u.ShortCode == shortCode)
                            .Select(u => new
                            {
                                u.ClickCount,
                                u.ExpiresAt
                            })
                            .FirstOrDefaultAsync();

                        // Kayıt silinmiş veya süresi dolmuş olabilir.
                        if (dbInfo == null ||
                            (dbInfo.ExpiresAt.HasValue &&
                             dbInfo.ExpiresAt.Value <= DateTime.UtcNow))
                        {
                            await _cache.KeyDeleteAsync(cacheKey);
                            return null;
                        }

                        cachedShortUrl.ClickCount = dbInfo.ClickCount;
                        cachedShortUrl.ExpiresAt = dbInfo.ExpiresAt;

                        return cachedShortUrl;
                    }
                }
            }
            catch (RedisException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Redis'ten okuma başarısız. PostgreSQL kullanılacak.");
            }

            // 2. Önbellekte yoksa PostgreSQL'den oku.
            var url = await _context.ShortUrls
                .FirstOrDefaultAsync(u =>
                    u.ShortCode == shortCode &&
                    (u.ExpiresAt == null || u.ExpiresAt > DateTime.UtcNow));

            _logger.LogInformation(
                "POSTGRESQL: {ShortCode}, kayıt bulundu mu: {Found}",
                shortCode,
                url != null);

            // 3. Geçerli kaydı Redis'e kaydet.
            if (url != null)
            {
                try
                {
                    string json = JsonSerializer.Serialize(url);

                    await _cache.StringSetAsync(
                        cacheKey,
                        json,
                        TimeSpan.FromMinutes(10));
                }
                catch (RedisException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Redis'e yazılamadı. Sonuç PostgreSQL'den döndürülecek.");
                }
            }

            return url;
        }

        public async Task<UrlAnalyticsResponse?> GetUrlAnalyticsAsync(string shortCode)
        {
            var url = await _context.ShortUrls
                .AsNoTracking()
                .FirstOrDefaultAsync(u =>
                    u.ShortCode == shortCode &&
                    (u.ExpiresAt == null || u.ExpiresAt > DateTime.UtcNow));

            if (url == null)
                return null;

            var recentClicks = await _context.UrlClicks
                .AsNoTracking()
                .Where(c => c.ShortUrlId == url.Id)
                .OrderByDescending(c => c.ClickedAt)
                .Take(10)
                .Select(c => c.ClickedAt)
                .ToListAsync();

            var dailyClicks = await _context.UrlClicks
                .AsNoTracking()
                .Where(c => c.ShortUrlId == url.Id)
                .GroupBy(c => c.ClickedAt.Date)
                .Select(g => new DailyClickStat
                {
                    Date = g.Key,
                    Clicks = g.Count()
                })
                .OrderByDescending(g => g.Date)
                .ToListAsync();

            return new UrlAnalyticsResponse
            {
                ShortCode = url.ShortCode,
                TotalClicks = url.ClickCount,
                RecentClicks = recentClicks,
                DailyClicks = dailyClicks
            };
        }
        public async Task<bool> DeleteUrlAsync(int id)
        {
            var url = await _context.ShortUrls
                .FirstOrDefaultAsync(u => u.Id == id);

            if (url == null)
            {
                return false;
            }

            string shortUrlCacheKey = $"shorturl:{url.ShortCode}";
            string redirectCacheKey = $"redirect:{url.ShortCode}";

            // 1. Önce PostgreSQL kaydını sil.
            _context.ShortUrls.Remove(url);
            await _context.SaveChangesAsync();

            // 2. Redis'teki iki önbellek kaydını da temizle.
            try
            {
                await _cache.KeyDeleteAsync(
                    new RedisKey[] { shortUrlCacheKey, redirectCacheKey });

                _logger.LogInformation(
                    "Redis önbellekleri temizlendi: {ShortUrlKey}, {RedirectKey}",
                    shortUrlCacheKey,
                    redirectCacheKey);
            }
            catch (RedisException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Redis önbellekleri temizlenemedi: {ShortUrlKey}, {RedirectKey}",
                    shortUrlCacheKey,
                    redirectCacheKey);
            }

            return true;
        }

        public async Task<ShortUrl?> GetUrlForRedirectAsync(string shortCode, string visitorKey)
        {
            string cacheKey = $"redirect:{shortCode}";
            ShortUrl? url = null;
            bool cacheHit = false;

            // 1. Önce Redis'ten oku.
            try
            {
                RedisValue cachedUrl = await _cache.StringGetAsync(cacheKey);

                if (cachedUrl.HasValue)
                {
                    cacheHit = true;

                    url = new ShortUrl
                    {
                        ShortCode = shortCode,
                        OriginalUrl = cachedUrl.ToString()
                    };

                    _logger.LogInformation(
                        "REDIRECT REDIS GET: {CacheKey}, bulundu mu: True",
                        cacheKey);
                }
            }
            catch (RedisException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Redis'ten yönlendirme URL'si okunamadı.");
            }

            // 2. Redis'te yoksa PostgreSQL'den oku.
            if (url == null)
            {
                url = await _context.ShortUrls
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.ShortCode == shortCode);

                if (url == null)
                    return null;
            }

            // 3. Tıklama sayısı ve analitik kaydını aynı transaction içinde işle.
            var now = DateTime.UtcNow;

            await using var transaction =
                await _context.Database.BeginTransactionAsync();

            var updatedRows = await _context.ShortUrls
                .Where(u =>
                    u.ShortCode == shortCode &&
                    (u.ExpiresAt == null || u.ExpiresAt > now))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(
                        u => u.ClickCount,
                        u => u.ClickCount + 1));

            if (updatedRows == 0)
            {
                await transaction.RollbackAsync();

                try
                {
                    await _cache.KeyDeleteAsync(cacheKey);
                }
                catch (RedisException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Silinmiş URL'nin Redis kaydı temizlenemedi.");
                }

                return null;
            }

            // 4. Kısa URL'nin veritabanı ID değerini al.
            var shortUrlId = await _context.ShortUrls
                .Where(u => u.ShortCode == shortCode)
                .Select(u => u.Id)
                .FirstAsync();

            // 5. Tıklama geçmişine yeni kayıt ekle.
            var click = new UrlClick
            {
                ShortUrlId = shortUrlId,
                ClickedAt = now
            };

            _context.UrlClicks.Add(click);

            await _context.SaveChangesAsync();

            // İki işlem de başarılıysa transaction'ı tamamla.
            await transaction.CommitAsync();



            // 4. Yalnızca cache miss durumunda Redis'e yaz.
            if (!cacheHit)
            {
                try
                {
                    await _cache.StringSetAsync(
                        cacheKey,
                        url.OriginalUrl,
                        TimeSpan.FromMinutes(10));

                    _logger.LogInformation(
                        "REDIRECT REDIS SET: {CacheKey}",
                        cacheKey);
                }
                catch (RedisException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Redis'e yazılamadı. PostgreSQL sayacı korunuyor.");
                }
            }

            return url;
        }

        private async Task<string> GenerateUniqueShortCodeAsync()
        {
            string code;

            do
            {
                code = Guid.NewGuid().ToString("N")[..6];
            }
            while (await _context.ShortUrls
                .AnyAsync(u => u.ShortCode == code));

            return code;
        }
    }
}
