using SawyerCSharpConsole;

using Serilog;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();
builder.Services.AddSerilog();

Driver.RegisterTo(builder);

IHost host = builder.Build();
ILogger<Program> logger = host.Services.GetRequiredService<ILogger<Program>>();
try
{
    // Mimic the behavior supplied when running hosted services:
    // 1. Cancellation token
    // 2. Update cancellation token on first interception of interrupt signal.
    //     1. Force-closing the app on second interception of interrupt signal.
    //     2. Else, after five seconds, force-close the app.
    using CancellationTokenSource source = new();
    bool graceful = true;
    Console.CancelKeyPress += new ConsoleCancelEventHandler((
        _,
        cancelEvent) =>
    {
        if (graceful)
        {
            logger.LogWarning(
                "Received interrupt signal, attempting to shut down gracefully but will force-close in 5 seconds. Send again to immediately force-close");
            source.Cancel();
            cancelEvent.Cancel = true;
            graceful = false;
            // CA1806: Instance is never used, but that's fine since it will
            // still handle the delay before exiting.
#pragma warning disable CA1806
            new Timer(
                _ =>
                {
                    logger.LogCritical("Timeout reached, force-closing app");
                    Environment.Exit(1);
                },
                state: null,
                dueTime: 5000,
                period: 0);
#pragma warning restore CA1806
        }
        else
        {
            logger.LogCritical("Second interrupt received, force-closing the app");
        }
    });

    logger.LogInformation("Instantiating app services");
    Driver d = host.Services.GetRequiredService<Driver>();
    logger.LogInformation("Running app");
    await d.RunAsync(source.Token);
    logger.LogInformation("App completed");
}
catch (Exception exc)
{
    logger.LogCritical(exc, "An unhandled exception occurred, the app has crashed");
    throw;
}
finally
{
    // CA1849 wants to async-ly flush, which would end the program.
#pragma warning disable CA1849
    Log.CloseAndFlush();
#pragma warning restore CA1849
}
