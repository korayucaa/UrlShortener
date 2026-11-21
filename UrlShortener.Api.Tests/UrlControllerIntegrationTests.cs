using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using UrlShortener.Api.Data;
using UrlShortener.Api.DTOs;
using UrlShortener.Api.Models;
using Xunit;
using System.Net.Http.Headers;
using System.Text.Json;

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
    [Fact]
    public async Task GetUrls_ShouldReturnOnlyCurrentUsersUrls_WhenAuthenticatedWithJwt()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        // Birinci kullanıcıyı oluştur.
        var registerResponse1 = await client.PostAsJsonAsync(
            "/api/Auth/register",
            new
            {
                username = "user1",
                email = "user1@test.com",
                password = "Test123!"
            });

        Assert.Equal(HttpStatusCode.OK, registerResponse1.StatusCode);

        // İkinci kullanıcıyı oluştur.
        var registerResponse2 = await client.PostAsJsonAsync(
            "/api/Auth/register",
            new
            {
                username = "user2",
                email = "user2@test.com",
                password = "Test123!"
            });

        Assert.Equal(HttpStatusCode.OK, registerResponse2.StatusCode);

        // Birinci kullanıcıyla giriş yap.
        var loginResponse1 = await client.PostAsJsonAsync(
            "/api/Auth/login",
            new
            {
                email = "user1@test.com",
                password = "Test123!"
            });

        var loginResult1 = await loginResponse1.Content.ReadFromJsonAsync<
            System.Text.Json.JsonElement>();

        var token1 = loginResult1.GetProperty("token").GetString();

        Assert.NotNull(token1);

        // Birinci kullanıcı adına URL oluştur.
        client.DefaultRequestHeaders.Remove("X-API-Key");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token1);

        var createResponse1 = await client.PostAsJsonAsync(
            "/api/Urls",
            new
            {
                originalUrl = "https://user1.com"
            });

        Assert.Equal(HttpStatusCode.Created, createResponse1.StatusCode);

        // İkinci kullanıcıyla giriş yap.
        client.DefaultRequestHeaders.Authorization = null;

        var loginResponse2 = await client.PostAsJsonAsync(
            "/api/Auth/login",
            new
            {
                email = "user2@test.com",
                password = "Test123!"
            });

        var loginResult2 = await loginResponse2.Content.ReadFromJsonAsync<
            System.Text.Json.JsonElement>();

        var token2 = loginResult2.GetProperty("token").GetString();

        Assert.NotNull(token2);

        // İkinci kullanıcı adına URL oluştur.
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token2);

        var createResponse2 = await client.PostAsJsonAsync(
            "/api/Urls",
            new
            {
                originalUrl = "https://user2.com"
            });

        Assert.Equal(HttpStatusCode.Created, createResponse2.StatusCode);

        // Tekrar birinci kullanıcıya geç.
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token1);

        // URL listesini getir.
        var listResponse = await client.GetAsync("/api/Urls");

        var urls = await listResponse.Content.ReadFromJsonAsync<
            List<ShortUrlResponse>>();

        // Kontroller.
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.NotNull(urls);

        Assert.Contains(urls, url =>
            url.OriginalUrl == "https://user1.com");

        Assert.DoesNotContain(urls, url =>
            url.OriginalUrl == "https://user2.com");
    }
    [Fact]
    public async Task GetUrls_ShouldReturnAllUsersUrls_WhenAuthenticatedWithApiKey()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        // İki kullanıcı oluştur.
        await client.PostAsJsonAsync(
            "/api/Auth/register",
            new
            {
                username = "apiuser1",
                email = "apiuser1@test.com",
                password = "Test123!"
            });

        await client.PostAsJsonAsync(
            "/api/Auth/register",
            new
            {
                username = "apiuser2",
                email = "apiuser2@test.com",
                password = "Test123!"
            });

        // Birinci kullanıcıyla giriş yap.
        var login1 = await client.PostAsJsonAsync(
            "/api/Auth/login",
            new
            {
                email = "apiuser1@test.com",
                password = "Test123!"
            });

        var result1 = await login1.Content.ReadFromJsonAsync<
            System.Text.Json.JsonElement>();

        var token1 = result1.GetProperty("token").GetString();

        // Birinci kullanıcının URL'sini oluştur.
        client.DefaultRequestHeaders.Remove("X-API-Key");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", token1);

        var create1 = await client.PostAsJsonAsync(
            "/api/Urls",
            new { originalUrl = "https://apiuser1.com" });

        Assert.Equal(HttpStatusCode.Created, create1.StatusCode);

        // İkinci kullanıcıyla giriş yap.
        client.DefaultRequestHeaders.Authorization = null;

        var login2 = await client.PostAsJsonAsync(
            "/api/Auth/login",
            new
            {
                email = "apiuser2@test.com",
                password = "Test123!"
            });

        var result2 = await login2.Content.ReadFromJsonAsync<
            System.Text.Json.JsonElement>();

        var token2 = result2.GetProperty("token").GetString();

        // İkinci kullanıcının URL'sini oluştur.
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", token2);

        var create2 = await client.PostAsJsonAsync(
            "/api/Urls",
            new { originalUrl = "https://apiuser2.com" });

        Assert.Equal(HttpStatusCode.Created, create2.StatusCode);

        // JWT'yi kaldır, API Key ile listele.
        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("X-API-Key", "test-api-key");

        var response = await client.GetAsync("/api/Urls");

        var urls = await response.Content.ReadFromJsonAsync<
            List<ShortUrlResponse>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(urls);

        Assert.Contains(urls, url =>
            url.OriginalUrl == "https://apiuser1.com");

        Assert.Contains(urls, url =>
            url.OriginalUrl == "https://apiuser2.com");
    }
    [Fact]
    public async Task AdminDashboard_ShouldReturn200_WhenUserIsAdmin()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        // Kullanıcı oluştur.
        await client.PostAsJsonAsync(
            "/api/Auth/register",
            new
            {
                username = "adminuser",
                email = "admin@test.com",
                password = "Test123!"
            });

        // Kullanıcıyı Admin yap.
        using var scope = factory.Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await db.Users.FirstAsync(u => u.Email == "admin@test.com");
        user.Role = "Admin";

        await db.SaveChangesAsync();

        // Admin olarak giriş yap.
        var loginResponse = await client.PostAsJsonAsync(
            "/api/Auth/login",
            new
            {
                email = "admin@test.com",
                password = "Test123!"
            });

        var loginResult = await loginResponse.Content.ReadFromJsonAsync<
            System.Text.Json.JsonElement>();

        var token = loginResult.GetProperty("token").GetString();

        Assert.NotNull(token);

        // API Key yerine JWT kullan.
        client.DefaultRequestHeaders.Remove("X-API-Key");

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        // Admin dashboard'a eriş.
        var response = await client.GetAsync("/api/Admin/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
    [Fact]
    public async Task AdminDashboard_ShouldReturn403_WhenUserIsNotAdmin()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        // Normal kullanıcı oluştur.
        await client.PostAsJsonAsync(
            "/api/Auth/register",
            new
            {
                username = "normaluser",
                email = "normal@test.com",
                password = "Test123!"
            });

        // Normal kullanıcı olarak giriş yap.
        var loginResponse = await client.PostAsJsonAsync(
            "/api/Auth/login",
            new
            {
                email = "normal@test.com",
                password = "Test123!"
            });

        var loginResult = await loginResponse.Content.ReadFromJsonAsync<
            System.Text.Json.JsonElement>();

        var token = loginResult.GetProperty("token").GetString();

        Assert.NotNull(token);

        // API Key yerine JWT kullan.
        client.DefaultRequestHeaders.Remove("X-API-Key");

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        // Normal kullanıcı Admin dashboard'a erişmeye çalışıyor.
        var response = await client.GetAsync("/api/Admin/dashboard");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Swagger_ShouldDocumentSecurityAndResponsesCorrectly()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        // Swagger JSON belgesini al.
        var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        var paths = json.RootElement.GetProperty("paths");

        // Register ve Login herkese açık olmalı.
        var register = paths
            .GetProperty("/api/Auth/register")
            .GetProperty("post");

        var login = paths
            .GetProperty("/api/Auth/login")
            .GetProperty("post");

        Assert.False(register.TryGetProperty("security", out _));
        Assert.False(login.TryGetProperty("security", out _));

        // URL endpoint'i API Key ve JWT seçeneklerini içermeli.
        var getUrls = paths
            .GetProperty("/api/Urls")
            .GetProperty("get");

        var security = getUrls.GetProperty("security");

        var schemes = security
            .EnumerateArray()
            .SelectMany(item => item.EnumerateObject())
            .Select(item => item.Name)
            .ToHashSet();

        Assert.Contains("ApiKey", schemes);
        Assert.Contains("Bearer", schemes);

        // Admin endpoint'i korumalı olmalı.
        var admin = paths
            .GetProperty("/api/Admin/dashboard")
            .GetProperty("get");

        Assert.True(admin.TryGetProperty("security", out _));

        // Redirect GET ve HEAD anonim olmalı.
        var redirect = paths.GetProperty("/{shortCode}");

        Assert.False(
            redirect.GetProperty("get")
                .TryGetProperty("security", out _));

        Assert.False(
            redirect.GetProperty("head")
                .TryGetProperty("security", out _));

        // HTTP yanıt kodlarını kontrol et.
        Assert.True(
            register.GetProperty("responses").TryGetProperty("200", out _));

        Assert.True(
            register.GetProperty("responses").TryGetProperty("409", out _));

        Assert.True(
            login.GetProperty("responses").TryGetProperty("401", out _));

        Assert.True(
            getUrls.GetProperty("responses").TryGetProperty("200", out _));

        Assert.True(
            admin.GetProperty("responses").TryGetProperty("403", out _));

        var createUrl = paths
            .GetProperty("/api/Urls")
            .GetProperty("post");

        Assert.True(
            createUrl.GetProperty("responses").TryGetProperty("201", out _));

        Assert.True(
            createUrl.GetProperty("responses").TryGetProperty("429", out _));

        var deleteUrl = paths
            .GetProperty("/api/Urls/{id}")
            .GetProperty("delete");

        Assert.True(
            deleteUrl.GetProperty("responses").TryGetProperty("204", out _));

        var redirectGet = redirect.GetProperty("get");

        Assert.True(
            redirectGet.GetProperty("responses").TryGetProperty("302", out _));

        Assert.True(
            redirectGet.GetProperty("responses").TryGetProperty("404", out _));
    }

    [Fact]
    public async Task HealthCheck_ShouldReturnHealthyStatus()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        // Health endpoint'ine istek gönder.
        var response = await client.GetAsync("/health");

        // HTTP 200 dönmeli.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // JSON yanıtını oku.
        using var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        var root = json.RootElement;

        // Genel durum Healthy olmalı.
        Assert.Equal(
            "Healthy",
            root.GetProperty("status").GetString());

        // Test ortamında dış servis kontrolleri kayıtlı değil.
        Assert.Empty(
            root.GetProperty("checks").EnumerateArray());
    }
    [Fact]
    public async Task HealthCheckLive_ShouldReturnHealthyStatus()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        Assert.Equal(
            "Healthy",
            json.RootElement.GetProperty("status").GetString());

        Assert.Empty(
            json.RootElement.GetProperty("checks").EnumerateArray());
    }
    [Fact]
    public async Task HealthCheckReady_ShouldReturnHealthyStatus()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        Assert.Equal(
            "Healthy",
            json.RootElement.GetProperty("status").GetString());

        Assert.Empty(
            json.RootElement.GetProperty("checks").EnumerateArray());
    }
}