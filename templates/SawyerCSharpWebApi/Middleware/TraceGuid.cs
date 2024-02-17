using System.ComponentModel.DataAnnotations;
using System.Net;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.OpenApi.Models;

using Serilog.Context;

using Swashbuckle.AspNetCore.SwaggerGen;

namespace SawyerCSharpWebApi.Middleware;

/// <remarks>
/// When retrieving this service (presumably via constructor dependency
/// injection), it is recommended to <see cref="Value"/> the instance in that
/// constructor. This will ensure that misconfigured pipelines are detected
/// before the request starts being meaningfully executed.
/// <br />
/// This is also known as a request ID or a trace ID.
/// </remarks>
public class TraceGuid
{
    private Guid? _raw;

    public TraceGuid(
        Guid? raw = null)
    {
        _raw = raw;
    }

    /// <remarks>
    /// Don't forget to use this middleware via <see cref="Middleware"/>.
    /// </remarks>
    public static void RegisterTo(
        WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<TraceGuid>();
        builder.Services.AddOptions<Settings>()
            .Bind(builder.Configuration.GetRequiredSection("Middleware:TraceGuid"))
            .ValidateOnStart();
    }

    public Guid Value => _raw
        ?? throw new InvalidOperationException("Null value contained, instance was not configured");

    private Guid? Raw
    {
        get => _raw;
        set
        {
            if (value is null)
                throw new ArgumentNullException(nameof(Raw));

            if (_raw is not null)
                throw new InvalidOperationException(
                    $"Instance is already configured to {_raw}, will not reconfigure to {value}");

            _raw = value;
        }
    }

    private const string Header = "X-Trace-GUID";

    /// <summary>
    /// This will:
    /// <br /> 1. Create a new <see cref="Guid"/> (preferring to read it from
    /// the request's header <see cref="Header"/>, if supplied).
    /// <br /> 2. Initialize the current <see cref="IServiceScope"/>'s
    /// registered <see cref="TraceGuid"/> (which should have already
    /// been registered via <see cref="RegisterTo"/>).
    /// <br /> 3. Load the <see cref="Guid"/> into the response with header
    /// <see cref="Header"/>.
    /// <br /> 4. Load the <see cref="Guid"/> into the current logger's scope.
    /// </summary>
    public static async Task Middleware(
        HttpContext context,
        RequestDelegate next)
    {
        // Recall that ILogger.BeginScope{TState} exists, if needed.
        ILogger<TraceGuid> logger = context
            .RequestServices
            .GetRequiredService<ILogger<TraceGuid>>();

        Settings settings = context.RequestServices
            .GetRequiredService<IOptions<Settings>>()
            .Value;

        Guid trace = Guid.NewGuid();
        if (settings.ReadFromRequestIfPresent
            && context.Request.Headers.TryGetValue(Header, out StringValues supplied))
        {
            if (string.IsNullOrWhiteSpace(supplied))
            {
                logger.LogInformation(
                    "Request received with null or whitespace '{TraceGuidHeader}'",
                    Header);
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync(
                    $"Header '{Header}' supplied but value is null or whitespace",
                    context.RequestAborted);
                return;
            }

            if (!Guid.TryParse(supplied, out trace))
            {
                logger.LogInformation(
                    "Request received '{TraceGuidHeader}' that could not be parsed as a GUID",
                    Header);
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync(
                    $"Header '{Header}' supplied but value could not be parsed as a GUID",
                    context.RequestAborted);
                return;
            }
        }

        context.RequestServices.GetRequiredService<TraceGuid>().Raw = trace;
        context.Response.Headers.Append(
            key: Header,
            value: new StringValues(trace.ToString()));
        using (LogContext.PushProperty("TraceGuid", trace))
            await next(context);
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
            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = Header,
                In = ParameterLocation.Header,
                Required = false,
                Schema = new OpenApiSchema
                {
                    Type = "string"
                }
            });
        }
    }

    public class Settings
    {
        public bool ReadFromRequestIfPresent { get; set; }
    }
}
