using System.Net;

namespace SawyerCSharpWebApi.Middleware;

/// <summary>
/// Server errors, usually caused by uncaught exceptions, can easily leak
/// information that would rather be kept internal. As such, obfuscating the
/// payload of uncaught exceptions hardens the application.
/// </summary>
/// <remarks>
/// This is definitely something that an API gateway should be doing.
/// <br />
/// Some exceptions, like configuration validation exceptions, can't be caught
/// at this stage.
/// </remarks>
public class ObfuscatePayloadOfServerErrors : IMiddleware
{
    private readonly ILogger<ObfuscatePayloadOfServerErrors> _logger;

    public ObfuscatePayloadOfServerErrors(
        ILogger<ObfuscatePayloadOfServerErrors> logger)
    {
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (Exception exc)
        {
            _logger.LogError(exc, "An unhandled exception occurred, returning a 500");
            if (context.Response.HasStarted)
            {
                const string msg =
                    "The response has already started being written; aborting the pipeline to ensure no data leak is leaked to the client"; ;
                _logger.LogError(msg);
                throw new InvalidOperationException(msg);
            }

            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync(
                "Payload of server error has been scrubbed to ensure sensitive data is not leaked.",
                context.RequestAborted);
        }
    }
}
