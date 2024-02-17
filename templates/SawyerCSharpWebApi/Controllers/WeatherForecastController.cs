using System.ComponentModel.DataAnnotations;

using Asp.Versioning;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using SawyerCSharpWebApi.Middleware;

namespace SawyerCSharpWebApi;

[ApiController]
[Route($"{ControllerConsts.UriRoot}/[controller]")]
[ApiVersion(1)]
// Recall that multiple ApiVersion attributes can be attached to a controller,
//      and that it has a Deprecated parameter.
// Recall that the version can be attached to endpoints instead of controllers.
public class WeatherForecastController : ControllerBase
{
    private static readonly string[] Summaries =
    [
        "Freezing",
        "Bracing",
        "Chilly",
        "Cool",
        "Mild",
        "Warm",
        "Balmy",
        "Hot",
        "Sweltering",
        "Scorching"
    ];

    private readonly TraceGuid _traceGuid;
    private readonly ILogger<WeatherForecastController> _logger;

    public WeatherForecastController(
        TraceGuid traceGuid,
        ILogger<WeatherForecastController> logger)
    {
        _traceGuid = traceGuid;
        _logger = logger;
    }

    [HttpGet]
    public Task<ActionResult<IEnumerable<WeatherForecast>>> GetAsync(
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("{Dtm}", DateTime.Now.ToString());
        ActionResult<IEnumerable<WeatherForecast>> resp = Ok(
            Enumerable.Range(1, 5).Select(index => new WeatherForecast
            {
                DateTime = DateTime.Now,
                TemperatureC = Random.Shared.Next(-20, 55),
                Summary = Summaries[Random.Shared.Next(Summaries.Length)]
            })
                .ToArray());
        return Task.FromResult(resp);
    }

    [HttpPost]
    public Task<ActionResult<Foo>> PostAsync(
        [FromBody] Foo foo,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Request guid (again): {Guid}", _traceGuid.Value);
        _logger.LogInformation("Instance has N value of {N}", foo.N);
        ActionResult<Foo> resp = Ok(foo);
        return Task.FromResult(resp);
    }

    [HttpGet("demo0")]
    public async Task<ActionResult> Demo0(
        string msg)
    {
        for (int i = 0; i < 5; i++)
        {
            _logger.LogInformation("Demo 0: {Message}", msg);
            await Task.Delay(1000);
        }

        return Ok();
    }

    [HttpGet("demo1")]
    public async Task<ActionResult> Demo1(
        string msg)
    {
        for (int i = 0; i < 5; i++)
        {
            _logger.LogInformation("Demo 1: {Message}", msg);
            await Task.Delay(1000);
        }

        return Ok();
    }

    [HttpGet("crash")]
    [AllowAnonymous]
    public ActionResult Crash()
    {
        throw new Exception("some server error, IDK");
    }

    /// <summary>
    /// Foo is a demo type.
    /// </summary>
    /// <param name="N">An int</param>
    public record Foo(
        [Range(1, int.MaxValue)] int N);

    public class WeatherForecast
    {
        public DateTime DateTime { get; set; }

        public int TemperatureC { get; set; }

        public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);

        public string? Summary { get; set; }
    }
}
