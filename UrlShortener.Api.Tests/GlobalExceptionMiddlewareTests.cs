using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using UrlShortener.Api.Middleware;
using Xunit;

namespace UrlShortener.Api.Tests
{
    public class GlobalExceptionMiddlewareTests
    {
        [Fact]
        public async Task InvokeAsync_ShouldReturn500_WhenUnexpectedExceptionOccurs()
        {
            // Arrange
            var context = new DefaultHttpContext();
            context.Response.Body = new MemoryStream();

            RequestDelegate next = _ =>
                Task.FromException(new InvalidOperationException("Test hatası"));

            var middleware = new GlobalExceptionMiddleware(
                next,
                NullLogger<GlobalExceptionMiddleware>.Instance);

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            Assert.Equal(
                StatusCodes.Status500InternalServerError,
                context.Response.StatusCode);

            context.Response.Body.Position = 0;

            using var document = await JsonDocument.ParseAsync(
                context.Response.Body);

            var root = document.RootElement;

            Assert.False(root.GetProperty("success").GetBoolean());

            Assert.Equal(
                "Sunucu tarafında beklenmeyen bir hata oluştu.",
                root.GetProperty("message").GetString());

            Assert.Equal(
                JsonValueKind.Null,
                root.GetProperty("data").ValueKind);
        }

        [Fact]
        public async Task InvokeAsync_ShouldReturn400_WhenArgumentExceptionOccurs()
        {
            // Arrange
            var context = new DefaultHttpContext();
            context.Response.Body = new MemoryStream();

            RequestDelegate next = _ =>
                Task.FromException(
                    new ArgumentException("Geçersiz parametre"));

            var middleware = new GlobalExceptionMiddleware(
                next,
                NullLogger<GlobalExceptionMiddleware>.Instance);

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            Assert.Equal(
                StatusCodes.Status400BadRequest,
                context.Response.StatusCode);

            context.Response.Body.Position = 0;

            using var document = await JsonDocument.ParseAsync(
                context.Response.Body);

            var root = document.RootElement;

            Assert.False(root.GetProperty("success").GetBoolean());

            Assert.Equal(
                "Geçersiz parametre",
                root.GetProperty("message").GetString());

            Assert.Equal(
                JsonValueKind.Null,
                root.GetProperty("data").ValueKind);
        }
    }
}