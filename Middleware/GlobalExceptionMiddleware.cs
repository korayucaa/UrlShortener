
using System.Net;
using System.Text.Json;
using UrlShortener.Api.DTOs;

namespace UrlShortener.Api.Middleware
{
    public class GlobalExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionMiddleware> _logger;

        public GlobalExceptionMiddleware(
            RequestDelegate next,
            ILogger<GlobalExceptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }


        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Beklenmeyen bir hata oluştu. Path: {Path}",
                    context.Request.Path);

                var statusCode = ex switch
                {
                    ArgumentException => StatusCodes.Status400BadRequest,
                    _ => StatusCodes.Status500InternalServerError
                };

                var message = statusCode == StatusCodes.Status400BadRequest
                    ? ex.Message
                    : "Sunucu tarafında beklenmeyen bir hata oluştu.";

                context.Response.StatusCode = statusCode;
                context.Response.ContentType = "application/json";

                var response = new ApiResponse<object>
                {
                    Success = false,
                    Message = message,
                    Data = null
                };

                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                await context.Response.WriteAsync(
                    JsonSerializer.Serialize(response, options));
            }
        }
    }
}
