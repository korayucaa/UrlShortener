using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
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
}