using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SawyerCSharpWebApi.HealthChecks;

public class SampleHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            return Task.FromResult(HealthCheckResult.Healthy());
        }
        // All exceptions (except a cancellation exception) will be caught by
        // the healthcheck library, so healthchecks only need to catch this
        // exception (and attach helpful message).
        catch (OperationCanceledException exc)
        {
            return Task.FromResult(HealthCheckResult.Degraded("The health check was cancelled, likely due to timeout", exc));
        }
    }
}
