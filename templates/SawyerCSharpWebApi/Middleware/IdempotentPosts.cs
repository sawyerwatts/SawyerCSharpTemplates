using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Security.Principal;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.OpenApi.Models;

using Swashbuckle.AspNetCore.SwaggerGen;

namespace SawyerCSharpWebApi.Middleware;

/// <summary>
/// POST requests are not idempotent, but a variety of events can occur when
/// making a POST request that can cause ambiguity on the success of the
/// request (such as a network error on the response trip). As such, this
/// middleware assists with this problem by requiring a <see cref="ClientIdempotencyKeyHeader"/>
/// when making POST requests so then clients can resend POST requests with
/// confidence that exactly-once semantics are maintained; upon subsequent
/// POSTs, <see cref="HttpStatusCode.Conflict"/> is returned.
/// </summary>
/// <remarks>
/// The client's idempotency key has the following restrictions:
/// <br /> 1. It must be at least <see cref="Settings.UserKeyMinLength"/>.
/// <br /> 2. It must be at least <see cref="Settings.UserKeyMaxLength"/>.
/// <br /> 3. It must not contain any '|'s.
/// <br />
/// This middleware requires the <see cref="HttpContext.User"/>'s
/// <see cref="IIdentity.Name"/> to be set to a unique value that is not
/// non-null or whitespace.
/// </remarks>
public class IdempotentPosts : IMiddleware
{
    private readonly IIdempotentPostsCache _cache;
    private readonly Settings _settings;
    private readonly ILogger<IdempotentPosts> _logger;

    public IdempotentPosts(
        IIdempotentPostsCache cache,
        IOptions<Settings> settings,
        ILogger<IdempotentPosts> logger)
    {
        _cache = cache;
        _settings = settings.Value;
        _logger = logger;
    }

    public static void RegisterTo(
        WebApplicationBuilder builder)
    {
        builder.Services.AddTransient<IdempotentPosts>();
        builder.Services.AddSingleton<IValidateOptions<Settings>, ValidateIdempotentPostsSettings>();
        builder.Services.AddOptions<Settings>()
            .Bind(builder.Configuration.GetRequiredSection("Middleware:IdempotentPosts"))
            .ValidateOnStart();
    }

    /// <remarks>
    /// Not suitable for sensitive data.
    /// </remarks>
    private const string ClientIdempotencyKeyHeader = "X-Idempotency-Key";
    private const string ExpiresHeader = "X-Idempotency-Key-Expires";

    public async Task InvokeAsync(
        HttpContext context,
        RequestDelegate next)
    {
        // An alternate implementation could also cache the result of the POST and
        // return that upon duplicate work being submitted, but that is definitely
        // at risk of being too much work for too little payoff.

        if (!context.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        StringValues rawClientIdempotencyKey;
        if (!context.Request.Headers.TryGetValue(ClientIdempotencyKeyHeader, out rawClientIdempotencyKey)
            || string.IsNullOrWhiteSpace(rawClientIdempotencyKey))
        {
            _logger.LogInformation(
                "When making POST requests, header {KeyHeader} must be supplied with a value that is not null or whitespace",
                ClientIdempotencyKeyHeader);
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync(
                $"When making POST requests, header '{ClientIdempotencyKeyHeader}' must be supplied with a value that is not null or whitespace",
                context.RequestAborted);
            return;
        }

        string clientIdempotencyKey = rawClientIdempotencyKey.First()!;
        if (clientIdempotencyKey.Length < _settings.UserKeyMinLength ||
            clientIdempotencyKey.Length > _settings.UserKeyMaxLength)
        {
            _logger.LogInformation(
                "Value for header {KeyHeader} is too long or too short, it must be at least {UserKeyMinLength} and at most {UserKeyMaxLength} characters",
                ClientIdempotencyKeyHeader,
                _settings.UserKeyMinLength,
                _settings.UserKeyMaxLength);
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync(
                $"Value for header '{ClientIdempotencyKeyHeader}' too long or too short, it must be at least {_settings.UserKeyMinLength} and at most {_settings.UserKeyMaxLength} characters",
                context.RequestAborted);
            return;
        }

        if (clientIdempotencyKey.Contains("|"))
        {
            _logger.LogInformation(
                "Value for header {KeyHeader} contains '|', which is not allowed",
                ClientIdempotencyKeyHeader);
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync(
                $"Value for header {ClientIdempotencyKeyHeader} contains '|', which is not allowed",
                context.RequestAborted);
            return;
        }

        string? clientIdentity = context.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(clientIdentity))
        {
            string msg =
                $"{nameof(IdempotentPosts)} has received a {nameof(HttpContext)} "
                + $"without a {nameof(context.User.Identity)} or a "
                + $"{nameof(context.User.Identity.Name)}.";
            throw new InvalidOperationException(msg);
        }

        string path = context.Request.Path.ToString().ToLower();
        string cacheKey = path + "|" + clientIdempotencyKey + "|" + clientIdentity;

        DateTime? storedExpires = await _cache.ContainsAsync(
            cacheKey,
            context.RequestAborted);
        if (storedExpires is not null)
        {
            _logger.LogInformation(
                "Will not repeat a POST operation for key '{Key}' and path '{Path}'; the existing pair will expire at {Expires}",
                clientIdempotencyKey,
                path,
                storedExpires);
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(
                new FoundActiveKey(
                    Message: FoundActiveKey.DefaultMessage,
                    Uri: path,
                    ClientIdempotencyKey: clientIdempotencyKey,
                    ClientIdentity: clientIdentity,
                    Expires: storedExpires.Value));
            return;
        }

        DateTime expires = DateTime.Now.AddHours(
            _settings.KeyExpirationHours);
        _logger.LogInformation(
            "POST tentatively has user key '{Key}' for path '{Path}' with expiration {Expires}; will consider caching on response",
            clientIdempotencyKey,
            path,
            expires);

        context.Response.Headers.Append(
            key: ClientIdempotencyKeyHeader,
            value: clientIdempotencyKey);
        context.Response.Headers.Append(
            key: ExpiresHeader,
            value: expires.ToString("o", CultureInfo.InvariantCulture));
        await next(context);

        if (context.Response.StatusCode >= 300)
        {
            _logger.LogInformation("Not caching key, response's status code is at least 300");
            return;
        }

        _logger.LogInformation("Caching");
        await _cache.InsertAsync(
            cacheKey,
            expires,
            context.RequestAborted);
    }

    private record FoundActiveKey(
        string Message,
        string Uri,
        string ClientIdempotencyKey,
        string ClientIdentity,
        DateTime Expires)
    {
        public const string DefaultMessage =
            "There is an existing key for this URI that has not yet expired. "
            + "The server will not repeat this idempotent operation, at least "
            + "not until the client's existing idempotency key against the "
            + "target URI has expired.";
    }

    public static void SetupSwaggerGen(
        SwaggerGenOptions options)
    {
        options.OperationFilter<HeaderSwaggerFilter>();
    }

    private class HeaderSwaggerFilter : IOperationFilter
    {
        public void Apply(
            OpenApiOperation operation,
            OperationFilterContext context)
        {
            if (context.ApiDescription.HttpMethod
                    ?.Equals("POST", StringComparison.OrdinalIgnoreCase) is not true)
            {
                return;
            }

            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = ClientIdempotencyKeyHeader,
                In = ParameterLocation.Header,
                Required = true,
                Schema = new OpenApiSchema
                {
                    Type = "string"
                }
            });
        }
    }

    public class Settings
    {
        [Range(1, 24 * 7)]
        public int KeyExpirationHours { get; set; }

        [Range(1, 128)]
        public int UserKeyMinLength { get; set; }

        [Range(1, 128)]
        public int UserKeyMaxLength { get; set; }
    }
}

[OptionsValidator]
public partial class ValidateIdempotentPostsSettings
    : IValidateOptions<IdempotentPosts.Settings>;

/// <remarks>
/// Be sure implementations will clean themselves out, and when the data isn't
/// immediately removed upon expires, then they will need to exclude no
/// longer active keys when searching the cache.
/// </remarks>
public interface IIdempotentPostsCache
{
    Task<DateTime?> ContainsAsync(
        string key,
        CancellationToken cancellationToken);

    Task InsertAsync(
        string key,
        DateTime expires,
        CancellationToken cancellationToken);
}

public class IdempotentPostsInMemoryCache : IIdempotentPostsCache
{
    private readonly IMemoryCache _memoryCache;
    private readonly Settings _settings;

    public IdempotentPostsInMemoryCache(
        IOptions<Settings> settings)
    {
        _settings = settings.Value;
        _memoryCache = new MemoryCache(
            new MemoryCacheOptions()
            {
                SizeLimit = settings.Value.KeyLimit,
                ExpirationScanFrequency = TimeSpan.FromSeconds(
                    settings.Value.ExpirationScanFrequencySec),
            });
    }

    public Task<DateTime?> ContainsAsync(
        string key,
        CancellationToken cancellationToken)
    {
        DateTime? result = null;
        if (_memoryCache.TryGetValue(key, out DateTime expires))
            result = expires;
        return Task.FromResult(result);
    }

    /// <remarks>
    /// Be warned: if the <see cref="Settings.KeyLimit"/> is reached, inserts
    /// will silently fail until the
    /// <see cref="Settings.ExpirationScanFrequencySec"/> timer cleans out
    /// expired keys.
    /// </remarks>
    public Task InsertAsync(
        string key,
        DateTime expires,
        CancellationToken cancellationToken)
    {
        _memoryCache.Set(
            key: key,
            value: expires,
            options: new MemoryCacheEntryOptions()
            {
                Size = 1,
                AbsoluteExpirationRelativeToNow =
                    TimeSpan.FromSeconds(_settings.CacheSec),
            });
        return Task.CompletedTask;
    }

    public static void RegisterTo(
        WebApplicationBuilder builder)
    {
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<IIdempotentPostsCache, IdempotentPostsInMemoryCache>();
        builder.Services.AddSingleton<IValidateOptions<Settings>, ValidateIdempotentPostsInMemoryCacheSettings>();
        builder.Services.AddOptions<Settings>()
            .Bind(builder.Configuration.GetRequiredSection("Middleware:IdempotentPostsInMemoryCache"))
            .ValidateOnStart();
    }

    public class Settings
    {
        /// <summary>
        /// The number of seconds that the idempotency key should be cached in
        /// memory. Critically, this is in units second while
        /// <see cref="IdempotentPosts.Settings.KeyExpirationHours"/> is in
        /// hours because it could be argued that the retry will come quickly
        /// after the (presumed) network failure, so this better anticipates
        /// realistic timeframes to more aggressively control memory usage
        /// (independent of the cache's size limiting).
        /// </summary>
        [Range(1, 60 * 60 * 24)]
        public int CacheSec { get; set; }

        [Range(1, 4096)]
        public int KeyLimit { get; set; }

        [Range(1, 60 * 60)]
        public int ExpirationScanFrequencySec { get; set; }
    }
}

[OptionsValidator]
public partial class ValidateIdempotentPostsInMemoryCacheSettings
    : IValidateOptions<IdempotentPostsInMemoryCache.Settings>;
