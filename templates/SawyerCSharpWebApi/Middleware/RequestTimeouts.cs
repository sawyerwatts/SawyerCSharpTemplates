using System.ComponentModel.DataAnnotations;
using System.Net;

using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.Extensions.Options;

namespace SawyerCSharpWebApi.Middleware;

/// <remarks>
/// Note that the .NET middleware will throw an
/// <see cref="OperationCanceledException"/> when the timeout is reached.
/// </remarks>
public class RequestTimeouts
{
    /// <remarks>
    /// Don't forget to use this middleware via <see cref="RequestTimeoutsIApplicationBuilderExtensions.UseRequestTimeouts"/>.
    /// </remarks>
    public static void Add(
        WebApplicationBuilder builder)
    {
        Settings settings = new();
        builder.Configuration
            .GetRequiredSection("Middleware:RequestTimeouts")
            .Bind(settings);
        ValidateOptionsResult results =
            new ValidateRequestTimeoutsSettings()
                .Validate(nameof(RequestTimeouts), settings);
        if (results.Failed)
            throw new InvalidOperationException(results.FailureMessage);

        builder.Services.AddRequestTimeouts(options =>
        {
            options.DefaultPolicy = new RequestTimeoutPolicy
            {
                Timeout = TimeSpan.FromMilliseconds(settings.TimeoutMs),
                TimeoutStatusCode = (int)HttpStatusCode.ServiceUnavailable,
                WriteTimeoutResponse = async context =>
                {
                    ILogger<RequestTimeouts> logger = context.RequestServices
                        .GetRequiredService<ILogger<RequestTimeouts>>();
                    logger.LogError(
                        "Request is cancelled as it took longer than {RequestTimeoutMs} milliseconds",
                        settings.TimeoutMs);
                    context.Response.ContentType = "text/plain";
                    await context.Response.WriteAsync(
                        $"Timeout, request took longer than {settings.TimeoutMs} milliseconds",
                        context.RequestAborted);
                },
            };
        });
    }

    public class Settings
    {
        [Range(1, int.MaxValue)]
        public int TimeoutMs { get; set; }
    }
}

[OptionsValidator]
public partial class ValidateRequestTimeoutsSettings
    : IValidateOptions<RequestTimeouts.Settings>;
