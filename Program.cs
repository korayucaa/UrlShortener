using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using System.Threading.RateLimiting;
using UrlShortener.Api.Data;
using UrlShortener.Api.Services;
using UrlShortener.Api.Authentication;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Servisler
builder.Services.AddControllers();
// API Key Authentication
builder.Services
    .AddAuthentication("ApiKey")
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
        "ApiKey",
        options =>
        {
            options.ApiKey = builder.Configuration["ApiKey:Key"]
                ?? throw new InvalidOperationException(
                    "ApiKey:Key yapýlandýrmasý bulunamadý.");
        });

builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("CreateUrlPolicy", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: partition => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // API Key tanýmlamasý
    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Description = "API Key deðerini girin.",
        Name = "X-API-Key",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey
    });

    // Swagger isteklerine API Key eklenmesini saðla
    options.AddSecurityRequirement(document =>
        new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("ApiKey", document)] = []
        });
});
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddMemoryCache();
builder.Services.AddScoped<UrlService>();
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var options = ConfigurationOptions.Parse(
        builder.Configuration["Redis:ConnectionString"]!);

    options.AbortOnConnectFail = false;
    options.BacklogPolicy = BacklogPolicy.FailFast;
    options.ConnectRetry = 1;
    options.ConnectTimeout = 3000;
    options.AsyncTimeout = 3000;

    return ConnectionMultiplexer.Connect(options);
});

var app = builder.Build();
// Global hata yönetimi
// Global hata yönetimi
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exceptionFeature = context.Features
            .Get<IExceptionHandlerFeature>();

        var exception = exceptionFeature?.Error;

        var logger = context.RequestServices
            .GetRequiredService<ILogger<Program>>();

        if (exception != null)
        {
            logger.LogError(
                exception,
                "Bir hata oluþtu.");
        }

        var statusCode = exception switch
        {
            ArgumentException => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var message = statusCode == StatusCodes.Status400BadRequest
            ? exception!.Message
            : "Beklenmeyen bir hata oluþtu.";

        await context.Response.WriteAsJsonAsync(new
        {
            status = statusCode,
            message
        });
    });
});

// HTTP istek hattý
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}
app.UseAuthentication();
app.UseAuthorization();

if (!app.Environment.IsEnvironment("Testing"))
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.Migrate();
    }
}
app.UseRateLimiter();
app.MapControllers();
app.Run();
public partial class Program { }
