using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Threading.RateLimiting;

using Microsoft.Extensions.Options;

namespace SawyerCSharpWebApi.Middleware;

/// <summary>
/// There is a lot of control available when rate limiting, including chaining
/// and limiting in conjunction with authorization (which should return
/// 429: Too Many Requests instead of 503: Service Unavailable.
/// <br />
/// When deploying to prod, be sure to stress test the app to determine the
/// most appropriate rate limiting. For more, see
/// <seealso href="https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-8.0#testing-endpoints-with-rate-limiting">here</seealso>.
/// </summary>
/// <remarks>
/// This is another middleware that would be best handled by an API Gateway.
/// <br />
/// <seealso href="https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit">here</seealso>
/// has solid examples of how to work with this framework.
/// </remarks>
public static class RateLimiting
{
    /// <remarks>
    /// Don't forget to use this middleware via <see cref="RateLimiterApplicationBuilderExtensions.UseRateLimiter(Microsoft.AspNetCore.Builder.IApplicationBuilder)"/>.
    /// </remarks>
    public static void Add(
        WebApplicationBuilder builder)
    {
        Settings settings = new();
        builder.Configuration
            .GetRequiredSection("Middleware:RateLimiting")
            .Bind(settings);
        ValidateOptionsResult results =
            new ValidateRateLimitingSettings()
                .Validate(nameof(RateLimiting), settings);
        if (results.Failed)
            throw new InvalidOperationException(results.FailureMessage);

        builder.Services.AddRateLimiter(limiterOptions =>
        {
            limiterOptions.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                    RateLimitPartition.GetConcurrencyLimiter(
                        partitionKey: "globalConcurrencyLimiter",
                        _ =>
                            new ConcurrencyLimiterOptions
                            {
                                PermitLimit = settings.ConcurrencyPermitLimit,
                                QueueLimit = settings.ConcurrencyQueueLimit,
                                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            })),

                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: PartitionKey(context),
                        _ =>
                            new FixedWindowRateLimiterOptions
                            {
                                AutoReplenishment = true,
                                PermitLimit = settings.IdentityOrHostPermitLimit,
                                Window = TimeSpan.FromSeconds(settings.IdentityOrHostWindowSec)
                            })));

            // The built in rate limiter is known to have poor functionality around
            // rejection metadata. As such, this just blindly assumes that a rejected
            // request was because of a user making too many requests at once.
            limiterOptions.RejectionStatusCode = (int)HttpStatusCode.TooManyRequests;
            limiterOptions.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<RateLimiter>>()
                    .LogError(
                        "Request for '{Partition}' was rejected, too many requests",
                        PartitionKey(context.HttpContext));

                context.HttpContext.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                if (context.Lease.TryGetMetadata(
                        MetadataName.RetryAfter,
                        out TimeSpan retryAfter))
                {
                    await context.HttpContext.Response.WriteAsync(
                        $"Too many requests. Please try again after {retryAfter.TotalSeconds} second(s).",
                        cancellationToken: cancellationToken);
                }
                else
                {
                    await context.HttpContext.Response.WriteAsync(
                        "Too many requests. Please try again later.",
                        cancellationToken: cancellationToken);
                }
            };
        });
    }

    private static string PartitionKey(
        HttpContext context)
    {
        return context.User.Identity?.Name
            ?? context.Request.Headers.Host.ToString();
    }

    public class Settings
    {
        [Range(1, int.MaxValue)]
        public int ConcurrencyPermitLimit { get; set; }

        [Range(1, int.MaxValue)]
        public int ConcurrencyQueueLimit { get; set; }

        [Range(1, int.MaxValue)]
        public int IdentityOrHostPermitLimit { get; set; }

        [Range(1, int.MaxValue)]
        public int IdentityOrHostWindowSec { get; set; }
    }
}

[OptionsValidator]
public partial class ValidateRateLimitingSettings
    : IValidateOptions<RateLimiting.Settings>;
