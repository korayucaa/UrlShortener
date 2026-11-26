using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;
using StackExchange.Redis;
using System.Text;
using System.Threading.RateLimiting;
using UrlShortener.Api.Authentication;
using UrlShortener.Api.Data;
using UrlShortener.Api.Filters;
using UrlShortener.Api.HealthChecks;
using UrlShortener.Api.Middleware;
using UrlShortener.Api.Models;
using UrlShortener.Api.Services;

var builder = WebApplication.CreateBuilder(args);
// Serilog yapýlandýrmasý
builder.Host.UseSerilog((context, configuration) =>
{
    configuration
        .MinimumLevel.Information()
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File(
            "logs/log-.txt",
            rollingInterval: RollingInterval.Day);
});
// Servisler
builder.Services.AddControllers();
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
})
.AddApiExplorer(options =>
 {
     options.GroupNameFormat = "'v'VVV";
     options.SubstituteApiVersionInUrl = true;
 });
// Health Checks
var healthChecks = builder.Services.AddHealthChecks();

if (!builder.Environment.IsEnvironment("Testing"))
{
    healthChecks
        .AddNpgSql(
            builder.Configuration.GetConnectionString("DefaultConnection")!,
            name: "postgresql",
            tags: new[] { "database" })
        .AddRedis(
            builder.Configuration["Redis:ConnectionString"]!,
            name: "redis",
            tags: new[] { "cache" });
}
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
    options.DocInclusionPredicate((docName, apiDesc) =>
    {
        return apiDesc.GroupName == docName;
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

var apiVersionDescriptionProvider =
    app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
app.UseRouting();

app.UseMiddleware<GlobalExceptionMiddleware>();

app.Use(async (context, next) =>
{
    if (context.Request.Path.Equals("/health", StringComparison.OrdinalIgnoreCase))
    {
        var healthCheckService = context.RequestServices
            .GetRequiredService<HealthCheckService>();

        var report = await healthCheckService.CheckHealthAsync();

        context.Response.StatusCode =
            report.Status == HealthStatus.Healthy ? 200 : 503;

        await HealthCheckResponseWriter.WriteResponse(
            context,
            report);

        return;
    }

    await next();
});

// HTTP isteklerini otomatik logla
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment() ||
    app.Environment.IsEnvironment("Testing"))
{
    app.UseSwagger();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwaggerUI(options =>
        {
            foreach (var description in apiVersionDescriptionProvider.ApiVersionDescriptions)
            {
                options.SwaggerEndpoint(
                    $"/swagger/{description.GroupName}/swagger.json",
                    description.GroupName.ToUpperInvariant());
            }
        });
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
// Genel saðlýk kontrolü

// Liveness: API çalýþýyor mu?
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthCheckResponseWriter.WriteResponse
});

// Readiness: PostgreSQL ve Redis hazýr mý?
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("database") ||
                         check.Tags.Contains("cache"),
    ResponseWriter = HealthCheckResponseWriter.WriteResponse
}); app.MapControllers();

app.Run();
public partial class Program { }
