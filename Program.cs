
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
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exceptionFeature = context.Features
            .Get<IExceptionHandlerFeature>();

        if (exceptionFeature != null)
        {
            var logger = context.RequestServices
                .GetRequiredService<ILogger<Program>>();

            logger.LogError(
                exceptionFeature.Error,
                "Beklenmeyen bir hata oluþtu.");
        }

        context.Response.StatusCode =
            StatusCodes.Status500InternalServerError;

        context.Response.ContentType = "application/json";

        await context.Response.WriteAsJsonAsync(new
        {
            message = "Beklenmeyen bir hata oluþtu."
        });
    });
});

// HTTP istek hattý
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();
