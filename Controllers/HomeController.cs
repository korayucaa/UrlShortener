using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using UrlShortener.Api.Data;
using UrlShortener.Api.DTOs;
using UrlShortener.Api.Models;
using UrlShortener.Api.Services;

namespace UrlShortener.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class UrlsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly UrlService _urlService;
        private readonly ILogger<UrlsController> _logger;
        public UrlsController(AppDbContext context,UrlService urlService, ILogger<UrlsController> logger)
        {
            _context = context;
            _urlService = urlService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetUrls()
        {
            int? userId = null;

            var authHeader = Request.Headers.Authorization.ToString();

            if (authHeader.StartsWith(
                "Bearer ",
                StringComparison.OrdinalIgnoreCase))
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);

                if (userIdClaim == null ||
                    !int.TryParse(userIdClaim.Value, out var parsedUserId))
                {
                    return Unauthorized();
                }

                userId = parsedUserId;
            }

            var urls = await _urlService.GetUrlsAsync(userId);

            return Ok(urls.Select(ToResponse));
        }

        [HttpGet("code/{shortCode}")]
        public async Task<IActionResult> GetUrlByCode(string shortCode)
        {
            var url = await _urlService.GetUrlByCodeAsync(shortCode);

            if (url == null)
                return NotFound("Kısa URL bulunamadı.");

            return Ok(ToResponse(url));
        }
        [HttpGet("analytics/{shortCode}")]
        public async Task<IActionResult> GetUrlAnalytics(string shortCode)
        {
            var analytics = await _urlService.GetUrlAnalyticsAsync(shortCode);

            if (analytics == null)
                return NotFound("URL bulunamadı veya süresi dolmuş.");

            return Ok(analytics);
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteUrl(int id)
        {
            var deleted = await _urlService.DeleteUrlAsync(id);

            if (!deleted)
                return NotFound("URL kaydı bulunamadı.");

            return NoContent();
        }

        [EnableRateLimiting("CreateUrlPolicy")]
        [HttpPost]
        public async Task<IActionResult> CreateUrl(
            [FromBody] CreateUrlRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);

            int? userId = null;

            if (userIdClaim != null &&
                int.TryParse(userIdClaim.Value, out var parsedUserId))
            {
                userId = parsedUserId;
            }

            var shortUrl = await _urlService.CreateUrlAsync(
                request.OriginalUrl,
                request.CustomShortCode,
                request.ExpiresAt,
                userId);

            return Created(
                $"/{shortUrl.ShortCode}",
                ToResponse(shortUrl));
        }

        [AllowAnonymous]
        [AcceptVerbs("GET", "HEAD", Route = "/{shortCode}")]
        public async Task<IActionResult> RedirectToOriginalUrl(string shortCode)
        {
            if (IsSpeculativeRequest(Request))
            {
                _logger.LogInformation(
                    "REDIRECT SKIP (speculative): {ShortCode} | Purpose={Purpose} | SecPurpose={SecPurpose} | FetchMode={FetchMode} | FetchDest={FetchDest} | FetchUser={FetchUser}",
                    shortCode,
                    Request.Headers["Purpose"].ToString(),
                    Request.Headers["Sec-Purpose"].ToString(),
                    Request.Headers["Sec-Fetch-Mode"].ToString(),
                    Request.Headers["Sec-Fetch-Dest"].ToString(),
                    Request.Headers["Sec-Fetch-User"].ToString());

                return NoContent();
            }

            _logger.LogInformation(
                "REDIRECT: {ShortCode} | Method={Method} | Path={Path} | Purpose={Purpose} | SecPurpose={SecPurpose} | FetchMode={FetchMode} | FetchDest={FetchDest} | FetchUser={FetchUser}",
                shortCode,
                Request.Method,
                Request.Path,
                Request.Headers["Purpose"].ToString(),
                Request.Headers["Sec-Purpose"].ToString(),
                Request.Headers["Sec-Fetch-Mode"].ToString(),
                Request.Headers["Sec-Fetch-Dest"].ToString(),
                Request.Headers["Sec-Fetch-User"].ToString());

            var visitorKey = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var url = await _urlService.GetUrlForRedirectAsync(shortCode, visitorKey);

            if (url == null)
                return NotFound("Kısa URL bulunamadı.");

            return Redirect(url.OriginalUrl);
        }

        private static bool IsSpeculativeRequest(HttpRequest request)
        {
            if (HttpMethods.IsHead(request.Method))
                return true;

            var secPurpose = request.Headers["Sec-Purpose"].ToString();
            var purpose = request.Headers["Purpose"].ToString();
            var moz = request.Headers["X-Moz"].ToString();

            if (ContainsSpeculativeToken(secPurpose) ||
                ContainsSpeculativeToken(purpose) ||
                moz.Contains("prefetch", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var fetchMode = request.Headers["Sec-Fetch-Mode"].ToString();
            var fetchDest = request.Headers["Sec-Fetch-Dest"].ToString();

            if (fetchDest.Equals("empty", StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrEmpty(fetchMode) &&
                !fetchMode.Equals("navigate", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(fetchDest) &&
                !fetchDest.Equals("document", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static bool ContainsSpeculativeToken(string value) =>
            value.Contains("prefetch", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("prerender", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("preview", StringComparison.OrdinalIgnoreCase);


        private async Task<string> GenerateUniqueShortCode()
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

        private static ShortUrlResponse ToResponse(ShortUrl url)
        {
            return new ShortUrlResponse
            {
                Id = url.Id,
                OriginalUrl = url.OriginalUrl,
                ShortCode = url.ShortCode,
                CreatedAt = url.CreatedAt,
                ExpiresAt = url.ExpiresAt,
                ClickCount = url.ClickCount
            };
        }

    }
}
