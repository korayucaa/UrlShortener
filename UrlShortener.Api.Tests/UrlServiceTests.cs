
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using UrlShortener.Api.Data;
using UrlShortener.Api.Models;
using UrlShortener.Api.Services;
using Xunit;

namespace UrlShortener.Api.Tests;

public class UrlServiceTests
{
    [Fact]
    public async Task CreateUrlAsync_ShouldSaveUrlWithSixCharacterCode()
    {
        await using var test = await TestEnvironment.CreateAsync();

        var result = await test.Service.CreateUrlAsync(
            "https://example.com");

        Assert.Equal("https://example.com", result.OriginalUrl);
        Assert.Equal(6, result.ShortCode.Length);
        Assert.Equal(0, result.ClickCount);

        var savedUrl = await test.Context.ShortUrls
            .AsNoTracking()
            .SingleAsync(u => u.Id == result.Id);

        Assert.Equal(result.ShortCode, savedUrl.ShortCode);
    }

    [Fact]
    public async Task GetUrlForRedirectAsync_ShouldIncreaseClickCount()
    {
        await using var test = await TestEnvironment.CreateAsync();

        test.Context.ShortUrls.Add(new ShortUrl
        {
            OriginalUrl = "https://example.com",
            ShortCode = "abc123",
            ClickCount = 0,
            CreatedAt = DateTime.UtcNow
        });

        await test.Context.SaveChangesAsync();

        var result = await test.Service.GetUrlForRedirectAsync(
            "abc123",
            "test-visitor");

        Assert.NotNull(result);
        Assert.Equal("https://example.com", result.OriginalUrl);

        var updatedUrl = await test.Context.ShortUrls
            .AsNoTracking()
            .SingleAsync(u => u.ShortCode == "abc123");

        Assert.Equal(1, updatedUrl.ClickCount);
    }

    [Fact]
    public async Task GetUrlForRedirectAsync_ShouldReturnNullWhenCodeDoesNotExist()
    {
        await using var test = await TestEnvironment.CreateAsync();

        var result = await test.Service.GetUrlForRedirectAsync(
            "yok123",
            "test-visitor");

        Assert.Null(result);
        Assert.Empty(await test.Context.ShortUrls.ToListAsync());
    }

    [Fact]
    public async Task DeleteUrlAsync_ShouldRemoveUrlFromDatabase()
    {
        // Arrange: Test ortamýný ve silinecek URL'yi hazýrla.
        await using var test = await TestEnvironment.CreateAsync();

        var url = new ShortUrl
        {
            OriginalUrl = "https://example.com",
            ShortCode = "del123",
            ClickCount = 0,
            CreatedAt = DateTime.UtcNow
        };

        test.Context.ShortUrls.Add(url);
        await test.Context.SaveChangesAsync();

        // Act: URL'yi sil.
        var result = await test.Service.DeleteUrlAsync(url.Id);

        // Assert: Silme baþarýlý olmalý ve kayýt artýk bulunmamalý.
        Assert.True(result);

        var deletedUrl = await test.Context.ShortUrls
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == url.Id);

        Assert.Null(deletedUrl);
    }


    [Fact]
    public async Task DeleteUrlAsync_ShouldReturnFalseWhenUrlDoesNotExist()
    {
        // Arrange
        await using var test = await TestEnvironment.CreateAsync();

        // Act: Veritabanýnda bulunmayan bir ID'yi silmeyi dene.
        var result = await test.Service.DeleteUrlAsync(999);

        // Assert
        Assert.False(result);
        Assert.Empty(await test.Context.ShortUrls.ToListAsync());
    }
    [Fact]
    public async Task DeleteUrlAsync_ShouldDeleteBothRedisCacheKeys()
    {
        // Arrange: Test ortamýný ve silinecek URL'yi hazýrla.
        await using var test = await TestEnvironment.CreateAsync();

        var url = new ShortUrl
        {
            OriginalUrl = "https://example.com",
            ShortCode = "del123",
            ClickCount = 0,
            CreatedAt = DateTime.UtcNow
        };

        test.Context.ShortUrls.Add(url);
        await test.Context.SaveChangesAsync();

        // Act: URL'yi sil.
        var result = await test.Service.DeleteUrlAsync(url.Id);

        // Assert: Silme baþarýlý olmalý.
        Assert.True(result);

        // Redis'e iki önbellek anahtarýný silme isteði gönderilmiþ mi?
        test.RedisDatabase.Verify(
            r => r.KeyDeleteAsync(
                It.Is<RedisKey[]>(keys =>
                    keys.Length == 2 &&
                    keys.Any(k => k.ToString() == "shorturl:del123") &&
                    keys.Any(k => k.ToString() == "redirect:del123")),
                It.IsAny<CommandFlags>()),
            Times.Once);
    }
    [Fact]
    public async Task CreateUrlAsync_ShouldThrowExceptionWhenUrlIsEmpty()
    {
        await using var test = await TestEnvironment.CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => test.Service.CreateUrlAsync(""));
    }

    [Fact]
    public async Task CreateUrlAsync_ShouldThrowExceptionWhenUrlIsInvalid()
    {
        await using var test = await TestEnvironment.CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => test.Service.CreateUrlAsync("merhaba"));
    }

    [Fact]
    public async Task CreateUrlAsync_ShouldThrowExceptionWhenProtocolIsNotHttpOrHttps()
    {
        await using var test = await TestEnvironment.CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => test.Service.CreateUrlAsync("ftp://example.com"));
    }
    [Fact]
    public async Task CreateUrlAsync_ShouldNotSaveInvalidUrlToDatabase()
    {
        await using var test = await TestEnvironment.CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => test.Service.CreateUrlAsync("ftp://example.com"));

        var count = await test.Context.ShortUrls.CountAsync();

        Assert.Equal(0, count);
    }

    // Her test için gerekli ortamý hazýrlayan yardýmcý sýnýf.
    private sealed class TestEnvironment : IAsyncDisposable
    {
        public SqliteConnection Connection { get; }
        public AppDbContext Context { get; }
        public MemoryCache MemoryCache { get; }
        public ILoggerFactory LoggerFactory { get; }
        public UrlService Service { get; }
        public Mock<IDatabase> RedisDatabase { get; }

        private TestEnvironment(
            SqliteConnection connection,
            AppDbContext context,
            MemoryCache memoryCache,
            ILoggerFactory loggerFactory,
            UrlService service,
            Mock<IDatabase> redisDatabase)
        {
            Connection = connection;
            Context = context;
            MemoryCache = memoryCache;
            LoggerFactory = loggerFactory;
            Service = service;
            RedisDatabase = redisDatabase;
        }

        public static async Task<TestEnvironment> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;

            var context = new AppDbContext(dbOptions);
            await context.Database.EnsureCreatedAsync();

            var redis = new Mock<IConnectionMultiplexer>();
            var redisDatabase = new Mock<IDatabase>();

            redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(redisDatabase.Object);

            redisDatabase
                .Setup(r => r.StringGetAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null);

            redisDatabase
                .Setup(r => r.StringSetAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<When>(),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);
            redisDatabase
                .Setup(r => r.KeyDeleteAsync(
                    It.IsAny<RedisKey[]>(),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync(2L);

            var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory
                .Create(builder => { });

            var logger = loggerFactory.CreateLogger<UrlService>();

            var service = new UrlService(
                context,
                redis.Object,
                memoryCache,
                logger);

            return new TestEnvironment(
                connection,
                context,
                memoryCache,
                loggerFactory,
                service,
                redisDatabase);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Connection.DisposeAsync();
            MemoryCache.Dispose();
            LoggerFactory.Dispose();
        }
    }
}
