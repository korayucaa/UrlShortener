
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text.Json;

namespace UrlShortener.Api.HealthChecks
{
    public static class HealthCheckResponseWriter
    {
        public static async Task WriteResponse(
            HttpContext context,
            HealthReport report)
        {
            context.Response.ContentType = "application/json";

            var response = new
            {
                status = report.Status.ToString(),

                checks = report.Entries.Select(entry => new
                {
                    name = entry.Key,
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description,
                    duration = entry.Value.Duration.TotalMilliseconds
                })
            };

            await JsonSerializer.SerializeAsync(
                context.Response.Body,
                response,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });
        }
    }
}
