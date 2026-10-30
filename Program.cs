
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Data;
using UrlShortener.Api.Services;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Servisler
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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

app.UseAuthorization();

if (!app.Environment.IsEnvironment("Testing"))
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.Migrate();
    }
}

app.MapControllers();
app.Run();
public partial class Program { }
