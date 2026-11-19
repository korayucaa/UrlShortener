using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using StackExchange.Redis;
using System.Text;
using System.Threading.RateLimiting;
using UrlShortener.Api.Authentication;
using UrlShortener.Api.Data;
using UrlShortener.Api.Models;
using UrlShortener.Api.Services;
using UrlShortener.Api.Filters;
var builder = WebApplication.CreateBuilder(args);

// Servisler
builder.Services.AddControllers();
builder.Services.AddScoped<JwtService>();
// API Key Authentication
// JWT ve API Key Authentication
builder.Services.AddScoped<
    Microsoft.AspNetCore.Identity.IPasswordHasher<User>,
    Microsoft.AspNetCore.Identity.PasswordHasher<User>>();
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = "Smart";
        options.DefaultChallengeScheme = "Smart";
    })
    .AddPolicyScheme("Smart", "API Key or JWT", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            var authHeader = context.Request.Headers.Authorization.ToString();

            return authHeader.StartsWith(
                "Bearer ",
                StringComparison.OrdinalIgnoreCase)
                ? "Bearer"
                : "ApiKey";
        };
    })
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
        "ApiKey",
        options =>
        {
            options.ApiKey = builder.Configuration["ApiKey:Key"]
                ?? throw new InvalidOperationException(
                    "ApiKey:Key yapýlandýrmasý bulunamadý.");
        })
   .AddJwtBearer("Bearer", options =>
   {
       var jwtKey = builder.Configuration["Jwt:Key"];

       if (string.IsNullOrWhiteSpace(jwtKey))
       {
           throw new InvalidOperationException(
               "Jwt:Key yapýlandýrmasý bulunamadý.");
       }
       options.TokenValidationParameters = new TokenValidationParameters
       {
           ValidateIssuer = true,
           ValidIssuer = builder.Configuration["Jwt:Issuer"],

           ValidateAudience = true,
           ValidAudience = builder.Configuration["Jwt:Audience"],

           ValidateIssuerSigningKey = true,
           IssuerSigningKey = new SymmetricSecurityKey(
               Encoding.UTF8.GetBytes(jwtKey)),

           ValidateLifetime = true,
           ClockSkew = TimeSpan.Zero
       };
   });

builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    int defaultLimit = builder.Environment.IsEnvironment("Testing")
     ? 1000
     : 10;

    int permitLimit = builder.Configuration.GetValue<int?>(
        "RateLimiting:CreateUrlPermitLimit") ?? defaultLimit;
    options.AddPolicy("CreateUrlPolicy", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: partition => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
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

    // JWT Bearer tanýmlamasý
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT token deðerini girin. Bearer yazmadan yalnýzca token'ý yapýþtýrýn.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    options.OperationFilter<AuthorizeCheckOperationFilter>();
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "URL Shortener API",
        Version = "v1",
        Description = "URL kýsaltma, yönlendirme, kullanýcý yönetimi ve URL analiz iþlemlerini saðlayan RESTful Web API.",
        Contact = new OpenApiContact
        {
            Name = "Koray",
            Url = new Uri("https://github.com/korayucaa")
        }
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
//if (app.Environment.IsDevelopment())
//{
//    app.UseSwagger();
//    app.UseSwaggerUI();
//}
//Belki geri açmam gerekir 
if (app.Environment.IsDevelopment() ||
    app.Environment.IsEnvironment("Testing"))
{
    app.UseSwagger();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwaggerUI();
    }
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
