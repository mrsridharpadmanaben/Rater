using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rater.Core.Algorithms;
using Rater.Core.Configuration;
using Rater.Core.KeyExtraction;
using Rater.Core.Rules;
using Rater.Core.Services;
using Rater.Core.Storage;

namespace Rater.Middleware;

/// <summary>
/// Extension methods for one-line setup in any ASP.NET Core app.
///
/// Usage in any app's Program.cs:
///   builder.Services.AddRateLimiter(builder.Configuration);
///   app.UseRateLimiter();
/// </summary>
public static class RateLimiterExtensions
{

    /// <summary>
    /// Registers all rate limiter services into the DI container.
    /// Call this in Program.cs before app.Build().
    /// </summary>
    public static IServiceCollection AddRateLimiter(this IServiceCollection services, IConfiguration configuration)
    {
        // IOptionsMonitor picks up appsettings.json changes without restart
        var section = configuration.GetSection(RateLimiterConfiguration.SectionName);

        services.Configure<RateLimiterConfiguration>(section);

        // Reads Storage enum from config to decide which implementation to use
        var storageType = section.GetValue<RaterStorage>("Storage");

        switch (storageType)
        {
            case RaterStorage.Redis:
                // will be wired here when RedisStorageProvider is built
                throw new NotImplementedException(
                    "Redis storage is not yet implemented. Use Storage: InMemory for now.");

            case RaterStorage.InMemory:
            default:
                // Singleton — one shared store for all requests
                // If scoped, each request gets a fresh empty store = useless
                services.AddSingleton<IStorageProvider, InMemoryStorageProvider>();
                break;
        }

        // Singleton: stateless, just hold values
        // dictionaries of registered strategies. No per-request state.
        services.AddSingleton<AlgorithmFactory>();
        services.AddSingleton<KeyExtractorFactory>();

        // Scope: lightweight and read from
        // IOptionsMonitor which is already singleton
        services.AddScoped<RuleResolver>();
        services.AddScoped<RateLimiterService>();

        return services;
    }


    /// <summary>
    /// Adds the rate limiter to the middleware pipeline.
    /// Call this in Program.cs after app.Build(), before app.MapControllers().
    ///
    /// ORDER MATTERS in middleware pipeline:
    ///   app.UseRouting()
    ///   app.UseAuthentication()
    ///   app.UseRateLimiter()    ← after auth so X-Client-Id / JWT claims are available
    ///   app.UseAuthorization()
    ///   app.MapControllers()
    /// </summary>
    public static IApplicationBuilder UseRateLimiter(this IApplicationBuilder app)
    {
        app.UseMiddleware<RateLimiterMiddleware>();
        return app;
    }

}
