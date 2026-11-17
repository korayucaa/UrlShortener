using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using UrlShortener.Api.Data;
using UrlShortener.Api.DTOs;
using UrlShortener.Api.Models;
using Xunit;
namespace UrlShortener.Api.Tests;

public class UrlControllerIntegrationTests
    : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public UrlControllerIntegrationTests(
        CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
    }

    [Fact]
    public async Task CreateUrl_ShouldReturn400_WhenProtocolIsFtp()
    {
        var request = new
        {
            OriginalUrl = "ftp://example.com"
        };

        var response = await _client.PostAsJsonAsync(
            "/api/Urls",
            request);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }
    [Fact]
    public async Task CreateUrl_ShouldReturn201_WhenUrlIsValid()
    {
        var request = new
        {
            OriginalUrl = "https://www.google.com"
        };

        var response = await _client.PostAsJsonAsync(
            "/api/Urls",
            request);

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<
     UrlShortener.Api.DTOs.ShortUrlResponse>();

        Assert.NotNull(result);
        Assert.Equal("https://www.google.com", result.OriginalUrl);
        Assert.False(string.IsNullOrEmpty(result.ShortCode));
    }
    [Fact]
    public async Task Redirect_ShouldReturn302_WhenShortCodeExists()
    {
        // Arrange
        var request = new
        {
            OriginalUrl = "https://www.google.com"
        };

        var createResponse = await _client.PostAsJsonAsync(
            "/api/Urls",
            request);

        var createdUrl = await createResponse.Content.ReadFromJsonAsync<
            UrlShortener.Api.DTOs.ShortUrlResponse>();

        Assert.NotNull(createdUrl);
        _client.DefaultRequestHeaders.Remove("X-API-Key");
        // Act
        var redirectResponse = await _client.GetAsync(
            $"/{createdUrl.ShortCode}");

        // Assert
        Assert.Equal(
            HttpStatusCode.Redirect,
            redirectResponse.StatusCode);

        Assert.Equal(
     new Uri("https://www.google.com/"),
     redirectResponse.Headers.Location);
    }
    [Fact]
    public async Task Redirect_ShouldReturn404_WhenShortCodeDoesNotExist()
    {
        var response = await _client.GetAsync("/abcdef");

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }
    [Fact]
    public async Task GetUrls_ShouldReturnCreatedUrl()
    {
        // Arrange: Bir URL oluştur.
        var request = new
        {
            OriginalUrl = "https://www.google.com"
        };

        var createResponse = await _client.PostAsJsonAsync(
            "/api/Urls",
            request);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        // Act: URL listesini getir.
        var response = await _client.GetAsync("/api/Urls");

        var urls = await response.Content.ReadFromJsonAsync<
            List<UrlShortener.Api.DTOs.ShortUrlResponse>>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.NotNull(urls);

        Assert.Contains(urls, url =>
            url.OriginalUrl == "https://www.google.com");
    }
    [Fact]
    public async Task DeleteUrl_ShouldReturn204_AndRemoveUrl()
    {
        // Arrange: URL oluştur.
        var request = new
        {
            OriginalUrl = "https://www.google.com"
        };

        var createResponse = await _client.PostAsJsonAsync(
            "/api/Urls",
            request);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var createdUrl = await createResponse.Content.ReadFromJsonAsync<
            UrlShortener.Api.DTOs.ShortUrlResponse>();

        Assert.NotNull(createdUrl);

        // Act: URL'yi sil.
        var deleteResponse = await _client.DeleteAsync(
            $"/api/Urls/{createdUrl.Id}");

        // Assert: Silme işleminin başarılı olduğunu kontrol et.
        Assert.Equal(
            HttpStatusCode.NoContent,
            deleteResponse.StatusCode);

        // Silinen URL artık listede bulunmamalı.
        var listResponse = await _client.GetAsync("/api/Urls");

        var urls = await listResponse.Content.ReadFromJsonAsync<
            List<UrlShortener.Api.DTOs.ShortUrlResponse>>();

        Assert.NotNull(urls);

        Assert.DoesNotContain(urls, url =>
            url.Id == createdUrl.Id);
    }
    [Fact]
    public async Task DeleteUrl_ShouldReturn404_WhenUrlDoesNotExist()
    {
        // Veritabanında bulunmayan bir ID gönderiyoruz.
        var response = await _client.DeleteAsync("/api/Urls/999999");

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }
    [Fact]
    public async Task Redirect_ShouldIncreaseClickCount()
    {
        // Arrange
        var request = new
        {
            OriginalUrl = "https://www.google.com"
        };

        var createResponse = await _client.PostAsJsonAsync(
            "/api/Urls",
            request);

        var createdUrl = await createResponse.Content.ReadFromJsonAsync<
            UrlShortener.Api.DTOs.ShortUrlResponse>();

        Assert.NotNull(createdUrl);
        Assert.Equal(0, createdUrl.ClickCount);

        // Act
        var redirectResponse = await _client.GetAsync(
            $"/{createdUrl.ShortCode}");

        // Assert
        Assert.Equal(
            HttpStatusCode.Redirect,
            redirectResponse.StatusCode);

        var listResponse = await _client.GetAsync("/api/Urls");

        var urls = await listResponse.Content.ReadFromJsonAsync<
            List<UrlShortener.Api.DTOs.ShortUrlResponse>>();

        Assert.NotNull(urls);

        var updatedUrl = Assert.Single(
            urls.Where(url => url.Id == createdUrl.Id));

        Assert.Equal(1, updatedUrl.ClickCount);
    }
    [Fact]
    public async Task Redirect_ShouldReturn204_WhenRequestIsPrefetch()
    {
        // Arrange
        var request = new
        {
            OriginalUrl = "https://www.google.com"
        };

        var createResponse = await _client.PostAsJsonAsync(
            "/api/Urls",
            request);

        var createdUrl = await createResponse.Content.ReadFromJsonAsync<
            UrlShortener.Api.DTOs.ShortUrlResponse>();

        Assert.NotNull(createdUrl);

        // Act
        var httpRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/{createdUrl.ShortCode}");

        httpRequest.Headers.Add("Purpose", "prefetch");

        var response = await _client.SendAsync(httpRequest);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);

        // Prefetch nedeniyle tıklanma sayısı artmamalı.
        var listResponse = await _client.GetAsync("/api/Urls");

        var urls = await listResponse.Content.ReadFromJsonAsync<
            List<UrlShortener.Api.DTOs.ShortUrlResponse>>();

        Assert.NotNull(urls);

        var savedUrl = Assert.Single(
            urls.Where(url => url.Id == createdUrl.Id));

        Assert.Equal(0, savedUrl.ClickCount);
    }
    [Fact]
    public async Task Redirect_ShouldReturn204_WhenRequestIsHead()
    {
        // Arrange
        var request = new
        {
            OriginalUrl = "https://www.google.com"
        };

        var createResponse = await _client.PostAsJsonAsync(
            "/api/Urls",
            request);

        var createdUrl = await createResponse.Content.ReadFromJsonAsync<
            UrlShortener.Api.DTOs.ShortUrlResponse>();

        Assert.NotNull(createdUrl);

        // Act
        var httpRequest = new HttpRequestMessage(
            HttpMethod.Head,
            $"/{createdUrl.ShortCode}");

        var response = await _client.SendAsync(httpRequest);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);

        var listResponse = await _client.GetAsync("/api/Urls");

        var urls = await listResponse.Content.ReadFromJsonAsync<
            List<UrlShortener.Api.DTOs.ShortUrlResponse>>();

        Assert.NotNull(urls);

        var savedUrl = Assert.Single(
            urls.Where(url => url.Id == createdUrl.Id));

        Assert.Equal(0, savedUrl.ClickCount);
    }
    [Fact]
    public async Task CreateUrl_ShouldReturn429_WhenRateLimitIsExceeded()
    {
        // Arrange
        using var factory = new CustomWebApplicationFactory();
        factory.RateLimit = 10;
        using var client = factory.CreateClient();

        var request = new
        {
            OriginalUrl = "https://www.google.com"
        };

        // İlk 10 istek başarılı olmalı.
        for (int i = 0; i < 10; i++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/Urls",
                request);

            Assert.Equal(
                HttpStatusCode.Created,
                response.StatusCode);
        }

        // 11. istek reddedilmeli.
        var rejectedResponse = await client.PostAsJsonAsync(
            "/api/Urls",
            request);

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            rejectedResponse.StatusCode);
    }
    [Fact]
    public async Task GetUrls_ShouldReturn401_WhenApiKeyIsMissing()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Remove("X-API-Key");

        var response = await client.GetAsync("/api/Urls");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
    [Fact]
    public async Task GetUrls_ShouldReturn401_WhenApiKeyIsInvalid()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Remove("X-API-Key");
        client.DefaultRequestHeaders.Add("X-API-Key", "wrong-api-key");

        var response = await client.GetAsync("/api/Urls");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
    [Fact]
    public async Task CreateUrl_ShouldReturn201_WhenCustomShortCodeIsValid()
    {
        // Arrange
        var request = new
        {
            OriginalUrl = "https://www.google.com",
            CustomShortCode = "my-google-test"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Urls",
            request);

        var result = await response.Content.ReadFromJsonAsync<
            UrlShortener.Api.DTOs.ShortUrlResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal("my-google-test", result.ShortCode);
    }
    [Fact]
    public async Task CreateUrl_ShouldReturn400_WhenCustomShortCodeAlreadyExists()
    {
        // Arrange
        var request = new
        {
            OriginalUrl = "https://www.google.com",
            CustomShortCode = "duplicate-test"
        };

        // İlk URL'yi oluştur.
        var firstResponse = await _client.PostAsJsonAsync(
            "/api/Urls",
            request);

        Assert.Equal(
            HttpStatusCode.Created,
            firstResponse.StatusCode);

        // Act: Aynı kısa kodla ikinci kez oluşturmayı dene.
        var secondResponse = await _client.PostAsJsonAsync(
            "/api/Urls",
            request);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            secondResponse.StatusCode);
    }
    [Fact]
    public async Task CreateUrl_ShouldReturn400_WhenCustomShortCodeContainsInvalidCharacters()
    {
        // Arrange
        var request = new
        {
            OriginalUrl = "https://www.google.com",
            CustomShortCode = "google_test"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Urls",
            request);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }
    [Fact]
    public async Task CreateUrl_ShouldReturn400_WhenExpirationDateIsInThePast()
    {
        // Arrange
        var request = new
        {
            OriginalUrl = "https://www.google.com",
            ExpiresAt = DateTime.UtcNow.AddDays(-1)
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Urls",
            request);
        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }
    [Fact]
    public async Task CreateUrl_ShouldReturn201_WhenExpirationDateIsInTheFuture()
    {
        // Arrange
        var expirationDate = DateTime.UtcNow.AddDays(7);

        var request = new
        {
            OriginalUrl = "https://www.google.com",
            ExpiresAt = expirationDate
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Urls",
            request);

        var result = await response.Content.ReadFromJsonAsync<
            UrlShortener.Api.DTOs.ShortUrlResponse>();

        // Assert
        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);

        Assert.NotNull(result);
        Assert.NotNull(result.ExpiresAt);

        Assert.Equal(
            expirationDate,
            result.ExpiresAt.Value,
            TimeSpan.FromSeconds(1));
    }
    [Fact]
    public async Task Redirect_ShouldReturn404_WhenUrlIsExpired()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        // Önce gelecekte sona erecek bir URL oluşturalım.
        var createResponse = await client.PostAsJsonAsync(
            "/api/Urls",
            new
            {
                originalUrl = "https://www.google.com",
                customShortCode = "expired-redirect-test",
                expiresAt = DateTime.UtcNow.AddDays(1)
            });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        // Veritabanından URL'nin son kullanma tarihini geçmişe alalım.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var url = await db.ShortUrls
                .FirstAsync(u => u.ShortCode == "expired-redirect-test");

            url.ExpiresAt = DateTime.UtcNow.AddDays(-1);

            await db.SaveChangesAsync();
        }

        // Süresi dolmuş URL'ye erişmeyi deneyelim.
        var response = await client.GetAsync("/expired-redirect-test");


        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // Tıklanma sayısının artmadığını kontrol edelim.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var url = await db.ShortUrls
                .FirstAsync(u => u.ShortCode == "expired-redirect-test");

            Assert.Equal(0, url.ClickCount);
        }
    }
    [Fact]
    public async Task GetUrlByCode_ShouldReturn404_WhenUrlIsExpired()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        // Önce geçerli bir URL oluşturalım.
        var createResponse = await client.PostAsJsonAsync(
            "/api/Urls",
            new
            {
                originalUrl = "https://www.google.com",
                customShortCode = "expired-get-test",
                expiresAt = DateTime.UtcNow.AddDays(1)
            });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        // Veritabanında son kullanma tarihini geçmişe alalım.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var url = await db.ShortUrls
                .FirstAsync(u => u.ShortCode == "expired-get-test");

            url.ExpiresAt = DateTime.UtcNow.AddDays(-1);

            await db.SaveChangesAsync();
        }

        // Süresi dolmuş URL'nin bilgilerini getirmeyi deneyelim.
        var response = await client.GetAsync(
            "/api/Urls/code/expired-get-test");


        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
    [Fact]
    public async Task CreateUrl_ShouldAllowCustomCodeWithoutExpiration()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/Urls",
            new
            {
                originalUrl = "https://www.google.com",
                customShortCode = "no-expiration-test"
            });


        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<ShortUrlResponse>();

        Assert.NotNull(content);
        Assert.Equal("no-expiration-test", content.ShortCode);
        Assert.Null(content.ExpiresAt);
    }

    [Fact]
    public async Task Redirect_ShouldCreateUrlClickRecord()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient(
    new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

        // Test için kısa URL oluştur.
        var createResponse = await client.PostAsJsonAsync(
            "/api/Urls",
            new
            {
                originalUrl = "https://www.google.com",
                customShortCode = "analytics-test"
            });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        int shortUrlId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var url = await db.ShortUrls
                .FirstAsync(u => u.ShortCode == "analytics-test");

            shortUrlId = url.Id;
        }

        // Kısa URL'ye bir kez tıkla.
        var redirectResponse = await client.GetAsync("/analytics-test");


        Assert.Equal(HttpStatusCode.Redirect, redirectResponse.StatusCode);

        // Veritabanındaki sonuçları kontrol et.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var url = await db.ShortUrls
                .FirstAsync(u => u.Id == shortUrlId);

            var click = await db.UrlClicks
                .FirstOrDefaultAsync(c => c.ShortUrlId == shortUrlId);

            Assert.Equal(1, url.ClickCount);
            Assert.NotNull(click);
            Assert.True(click.ClickedAt <= DateTime.UtcNow);
        }
    }
    [Fact]
    public async Task GetUrlAnalytics_ShouldReturnDailyClickStatistics()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        // Test URL'si oluştur.
        var createResponse = await client.PostAsJsonAsync(
            "/api/Urls",
            new
            {
                originalUrl = "https://www.google.com",
                customShortCode = "daily-test"
            });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        // Veritabanına test tıklamaları ekle.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var url = await db.ShortUrls
                .FirstAsync(u => u.ShortCode == "daily-test");

            var today = DateTime.UtcNow.Date;
            var yesterday = today.AddDays(-1);

            url.ClickCount = 3;

            db.UrlClicks.AddRange(
                new UrlClick
                {
                    ShortUrlId = url.Id,
                    ClickedAt = today.AddHours(10)
                },
                new UrlClick
                {
                    ShortUrlId = url.Id,
                    ClickedAt = today.AddHours(12)
                },
                new UrlClick
                {
                    ShortUrlId = url.Id,
                    ClickedAt = yesterday.AddHours(15)
                });

            await db.SaveChangesAsync();
        }

        // Analytics endpoint'ini çağır.
        var response = await client.GetAsync(
            "/api/Urls/analytics/daily-test");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // JSON yanıtını kontrol et.
        var json = await response.Content.ReadAsStringAsync();

        using var document = System.Text.Json.JsonDocument.Parse(json);

        var root = document.RootElement;

        Assert.Equal(3, root.GetProperty("totalClicks").GetInt32());

        var dailyClicks = root.GetProperty("dailyClicks");

        Assert.Equal(2, dailyClicks.GetArrayLength());

        Assert.Equal(2, dailyClicks[0].GetProperty("clicks").GetInt32());
        Assert.Equal(1, dailyClicks[1].GetProperty("clicks").GetInt32());
    }
}