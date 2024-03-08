using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.OpenApi.Models;

using Swashbuckle.AspNetCore.SwaggerGen;

namespace SawyerCSharpWebApi.Middleware;

public class ApiKeyAuthentication
    : AuthenticationHandler<ApiKeyAuthentication.Settings>
{
    public const string AuthScheme = "ApiKey";
    private const string Header = "X-API-Key";

    [Obsolete("Obsolete")]
    public ApiKeyAuthentication(
        IOptionsMonitor<Settings> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock)
        : base(options, logger, encoder, clock)
    { }

    public ApiKeyAuthentication(
        IOptionsMonitor<Settings> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Context.Request.Headers.TryGetValue(Header, out StringValues apiKey))
            return Task.FromResult(
                AuthenticateResult.Fail($"Missing header '{Header}'"));

        if (string.IsNullOrWhiteSpace(apiKey))
            return Task.FromResult(
                AuthenticateResult.Fail($"Header '{Header}' has a null or whitespace value"));

        ReadOnlySpan<byte> actualKey =
            MemoryMarshal.Cast<char, byte>(apiKey.First().AsSpan());
        foreach (KeyValuePair<string, string> keyToName in Options.ApiKeyToIdentityName)
        {
            ReadOnlySpan<byte> expectedKey =
                MemoryMarshal.Cast<char, byte>(keyToName.Key.AsSpan());
            if (CryptographicOperations.FixedTimeEquals(expectedKey, actualKey))
            {
                Claim[] claims = [new Claim(ClaimTypes.Name, keyToName.Value)];
                ClaimsIdentity identity = new(claims, Scheme.Name);
                ClaimsPrincipal principal = new(identity);
                return Task.FromResult(
                    AuthenticateResult.Success(
                        new AuthenticationTicket(principal, AuthScheme)));
            }
        }

        return Task.FromResult(
            AuthenticateResult.Fail($"Header '{Header}' contains a non-matching API key"));
    }

    public static void Add(
        WebApplicationBuilder builder)
    {
        Settings settings = new();
        builder.Configuration
            .GetRequiredSection("Middleware:ApiKeyAuthentication")
            .Bind(settings);
        ValidateOptionsResult results =
            new ValidateApiKeyAuthenticationSettings()
                .Validate(nameof(ApiKeyAuthentication), settings);
        if (results.Failed)
            throw new InvalidOperationException(results.FailureMessage);

        builder.Services.AddAuthentication(AuthScheme)
            .AddScheme<Settings, ApiKeyAuthentication>(
                AuthScheme,
                options => options.ApiKeyToIdentityName = settings.ApiKeyToIdentityName);
    }

    public static void SetupSwaggerGen(
        SwaggerGenOptions options)
    {
        options.AddSecurityDefinition("apiKey", new OpenApiSecurityScheme
        {
            Name = Header,
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Description = $"API key authorization header. Example: \"{Header}: {{token}}\"",
            Scheme = "ApiKeyScheme",
        });

        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "apiKey",
                    }
                },
                Array.Empty<string>()
            }
        });
    }

    public class Settings : AuthenticationSchemeOptions
    {
        /// <summary>
        /// This maps an API key to its <see cref="IIdentity.Name"/>.
        /// </summary>
        /// <remarks>
        /// Depending on the exact project, it may be more appropriate to downgrade
        /// this to a list of keys if not a single key.
        /// </remarks>
        [Required]
        [MinLength(1)]
        public Dictionary<string, string> ApiKeyToIdentityName { get; set; } = [];
    }
}

[OptionsValidator]
public partial class ValidateApiKeyAuthenticationSettings
    : IValidateOptions<ApiKeyAuthentication.Settings>;
