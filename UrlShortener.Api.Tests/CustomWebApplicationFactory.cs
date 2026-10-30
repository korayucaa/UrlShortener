using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Data.Sqlite;
using Moq;
using StackExchange.Redis;
using UrlShortener.Api.Data;

namespace UrlShortener.Api.Tests;

public class CustomWebApplicationFactory
    : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection =
        new("Data Source=:memory:");

    protected override void ConfigureWebHost(
        IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            _connection.Open();

            services.RemoveAll<AppDbContext>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();

            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite(_connection));
            var serviceProvider = services.BuildServiceProvider();

            using (var scope = serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider
                    .GetRequiredService<AppDbContext>();

                db.Database.EnsureCreated();
            }
            var redisMock = new Mock<IConnectionMultiplexer>();

            redisMock
            .Setup(redis => redis.GetDatabase(
                It.IsAny<int>(),
                It.IsAny<object>()))
            .Returns(Mock.Of<IDatabase>());

            services.RemoveAll<IConnectionMultiplexer>();

            services.AddSingleton(
                redisMock.Object);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection.Dispose();
        }
    }
}