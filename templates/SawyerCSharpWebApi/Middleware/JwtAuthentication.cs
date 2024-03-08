using System.ComponentModel.DataAnnotations;
using System.Text;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

using Swashbuckle.AspNetCore.SwaggerGen;

namespace SawyerCSharpWebApi.Middleware;

public static class JwtAuthentication
{
    public const string AuthScheme = "jwt";

    /// <summary>
    /// This will ensure that the JWT:
    /// <br /> - Comes from the correct authority
    /// <br /> - Is sent to the correct audience,
    /// <br /> - Ensures that the token is active with a configurable clock skew
    /// <br /> - Uses an algorithm from the configurable list
    /// <br /> - Uses the configurable issuer signing key to validate the JWT's signature
    /// <br /> - Is sent with HTTPS metadata
    /// <br /> - Load the sub claim into `HttpContext.User.Identity.Name` (but
    /// this will <i>not</i> ensure that the sub claim is present. That is
    /// delegated to <see cref="PolicyServiceCollectionExtensions.AddAuthorization(Microsoft.Extensions.DependencyInjection.IServiceCollection)"/>).
    /// </summary>
    /// <param name="builder"></param>
    public static void Add(
        WebApplicationBuilder builder)
    {
        Settings settings = new();
        builder.Configuration
            .GetRequiredSection("Middleware:JwtAuthentication")
            .Bind(settings);
        ValidateOptionsResult results =
            new ValidateJwtAuthenticationSettings()
                .Validate(nameof(JwtAuthentication), settings);
        if (results.Failed)
            throw new InvalidOperationException(results.FailureMessage);

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = settings.Issuer,
                    ValidAudience = settings.Audience,
                    ValidAlgorithms = settings.Algorithms,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.IssuerSigningKey)),
                    ValidateSignatureLast = true,
                    ClockSkew = TimeSpan.FromMinutes(settings.ClockSkewSec),
                    // This uses sub instead of name since sub is unique.
                    NameClaimType = "sub",
                };

                // Otherwise, sub claim will be changed to http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier
                options.MapInboundClaims = false;

#if !DEBUG
                options.RequireHttpsMetadata = true;
#endif
            });
    }

    public static void SetupSwaggerGen(
        SwaggerGenOptions options)
    {
        options.AddSecurityDefinition("bearerAuth", new OpenApiSecurityScheme
        {
            Description = "JWT authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
            Name = "Authorization",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
        });

        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "bearerAuth",
                    }
                },
                Array.Empty<string>()
            }
        });
    }

    public class Settings
    {
        [Required]
        public string Issuer { get; set; } = "";

        [Required]
        public string Audience { get; set; } = "";

        [Required]
        [MinLength(1)]
        public List<string> Algorithms { get; set; } = [];

        [Required]
        public string IssuerSigningKey { get; set; } = "";

        [Range(1, 60 * 3)]
        public int ClockSkewSec { get; set; } = 90;
    }
}

[OptionsValidator]
public partial class ValidateJwtAuthenticationSettings
    : IValidateOptions<JwtAuthentication.Settings>;
