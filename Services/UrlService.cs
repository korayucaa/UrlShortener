
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using UrlShortener.Api.Data;
using UrlShortener.Api.Models;
using StackExchange.Redis;
using System.Text.Json;
using Microsoft.Extensions.Logging;

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

        public async Task<ShortUrl> CreateUrlAsync(string originalUrl)
        {
            var shortUrl = new ShortUrl
            {
                OriginalUrl = originalUrl,
                ShortCode = await GenerateUniqueShortCodeAsync(),
                CreatedAt = DateTime.UtcNow,
                ClickCount = 0
            };

            _context.ShortUrls.Add(shortUrl);
            await _context.SaveChangesAsync();

            return shortUrl;
        }

        public async Task<List<ShortUrl>> GetUrlsAsync()
        {
            return await _context.ShortUrls.ToListAsync();
        }



        public async Task<ShortUrl?> GetUrlByCodeAsync(string shortCode)
        {
            string cacheKey = $"shorturl:{shortCode}";

            // 1. Önce Redis önbelleğini kontrol et
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
                        // Tıklama sayısı PostgreSQL'de güncel tutuluyor.
                        var clickCount = await _context.ShortUrls
                            .AsNoTracking()
                            .Where(u => u.ShortCode == shortCode)
                            .Select(u => (int?)u.ClickCount)
                            .FirstOrDefaultAsync();

                        if (clickCount == null)
                        {
                            // Kayıt silinmişse eski önbelleği temizle.
                            await _cache.KeyDeleteAsync(cacheKey);
                            return null;
                        }

                        cachedShortUrl.ClickCount = clickCount.Value;

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
            

            // 2. Önbellekte yoksa PostgreSQL'den oku
            var url = await _context.ShortUrls
                .FirstOrDefaultAsync(u => u.ShortCode == shortCode);
            _logger.LogInformation(
                "POSTGRESQL: {ShortCode}, kayıt bulundu mu: {Found}",
                shortCode,
                url != null);
            // 3. Kaydın tamamını JSON olarak Redis'e kaydet
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
            // 3. Tıklama sayısını PostgreSQL'de atomik olarak artır.
            var updatedRows = await _context.ShortUrls
                .Where(u => u.ShortCode == shortCode)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(
                        u => u.ClickCount,
                        u => u.ClickCount + 1));

            if (updatedRows == 0)
            {
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
