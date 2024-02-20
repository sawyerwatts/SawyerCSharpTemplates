# Sawyer's WebApi Template

This is a template for the .NET SDK to make a slightly more batteries
included web API csproj, subject to my preferences.

Shoutout to [StickFigure](https://github.com/stickfigure) for their amazing
article on REST API design
([here](https://github.com/stickfigure/blog/wiki/How-to-%28and-how-not-to%29-design-REST-APIs)).
This has a ton of great ideas, some of which are implemented here.

## Features

- For more information on these features, check out [Program.cs](./Program.cs),
  [Middleware/](./Middleware), and the various `appsettings*.json` file(s).
- Additionally, [Controllers/WeatherForecastController.cs](Controllers/WeatherForecastController.cs)
  and [SawyerCSharpWebApi.http](./SawyerCSharpWebApi.http) have examples of using
  a lot of these features.
- [TODO.md](./TODO.md) has finalization instructions for users of the
  template.
- Every settings POCO is validated on start up, so running the project will
  help you quickly identify missing settings.

### Authentication

This template defaults to requiring requests to be from an authenticated user
and have the authentication handler populate `HttpContext.User.Identity.Name`.
Additionally, this template has a few pre-built options for authentication.

Once a user has authenticated, they are logged with their host and requested
URL.

When instantiating a project from this template, there is a required parameter
to choose the authentication type.

#### API Key

This template will, via the `ApiKeyAuthenticationSchemeHandler` class, require
requests contain header `X-API-Key`, and authenticate the key against the
key(s) in the `IConfiguration`, and once the key is confirmed valid, set the
request's identity to the configured identity.

This class will tell the SwaggerGen that this authentication format is
required.

#### JWT Authentication

This template will, via the `JwtAuthentication` class, validate that the JWT:

- Comes from the correct authority
- Is sent to the correct audience,
- Ensures that the token is active with a configurable clock skew
- Uses an algorithm from the configurable list
- Uses the configurable issuer signing key to validate the JWT's signature
- Is sent with HTTPS metadata
- Load the sub claim into `HttpContext.User.Identity.Name`

This class will tell the SwaggerGen that this authentication format is
required.

### Idempotent POSTs

For background, POST requests struggle with exactly-once semantics as POSTs
will attempt to create, but if a network error occurs on the response (or
other situations occur), the client will not know if their creation request as
fullfilled. PUTs are preferable for this reason, but PUTs are not always
possible.

This template will, via the `IdempotentPosts` class, configure middleware to
require a `X-Idempotency-Token` request header. This allows the template to cache
the client's idempotency token (plus their identity and the endpoint they are
POSTing to) in order to achieve exactly once semantics for a configurable
period of time. With this middleware,
clients can confidently resubmit (within that configurable timeframe) as many
times as needed to ensure their request goes through. When resubmitting a
completed POST, `409: Conflict` is returned.

This class tells the SwaggerGen that the header is required for POST requests.

### Trace/Correlation/Request IDs

This template will, via the `TraceGuid` class, configure middleware to read in
(or if not supplied, create) a trace GUID from request header `X-Trace-GUID`,
add it to the logging context so the value is present in logs for that request,
load the GUID into the scoped service, and return the GUID in the response
using the `X-Trace-GUID` header.

### `Accept` validation

By default, ASP.NET Core will return `415: Unsupported media type` when a
request is received in a `Content-Type` that cannot be parsed. This template
will tell ASP.NET Core to return `406: Not acceptable` when the
`Accept` is not supported.

### API Versioning

This template will detail the supported and deprecated versions of an API
via headers, and it will validate that the version supplied actually exists,
especially when using URL-based versioning.

### Obfuscation of payload on server responses

Server errors will likely leak implementation details and/or sensitive
information. As such, uncaught exceptions that occur in the request pipeline
are captured by middleware `ObfuscatePayloadOfServerErrors` to ensure the
payload of that exception (which would have been the stack trace and possibly
other information) is replaced with a secure message.

Ideally, this would be handled by the API Gateway, but just in case, this
functionality is implemented here as well. If the user of the template is
confident that their API Gateway will handle this functionality (and the
functionality is desired), it is recommended to remove this middleware.

### Rate Limiting

This template configures the rate limiting middleware with configurable
defaults, as well as global a configuring concurrency limit and a per identity
(or host) window limit. Additionally, instead of returning
`503: Service unavailable` when a limit is reached, this template will return
`429: Too many requests`.

Ideally, this would be handled by the API Gateway, but just in case, this
functionality is implemented here as well. If the user of the template is
confident that their API Gateway will handle this functionality (and the
functionality is desired), it is recommended to remove this middleware.

### Request Timeouts

This template configures the request timeout middleware with configurable
defaults. Note that `OperationCanceledException` is thrown by the .NET
request timeout middleware when the period is reached.

### Health checks

This has an example of setting up a health check, and it maps the health
checks to `{baseURL}/_health` (configurable in `Program.cs`).

### Swagger

This template automatically loads the XML Docs into SwaggerGen, as well as
versioning the specification and containing other middleware's headers and
authentication requirements.

### Logging via Serilog

This template comes pre-baked with Serilog configured as the implementation to
the built in `ILogger<T>`, and these settings are controlled via the
`appsettings*.json` files. Serilog is configured to buffer file writes and
to write file and console logs via a background thread.

## TODO

Users of this template have some tasks to do to ensure the template is best
utilized.

### Health checks

- Replace `SampleHealthCheck` with a real health check.
- [Program.cs](./Program.cs)'s `MapHealthChecks` could use `RequireHost`
  in addition to `AllowAnonymous`.
- Ideally, have monitoring in place to periodically check that the API is still
  healthy.

### Middleware

Check the request pipeline in [Program.cs](./Program.cs) and check each of the
classes in [Middleware/](./Middleware) to ensure they are relevant for the
web API being built.

#### API Gateway Behaviors

Some middleware in this template would be best handled by an API Gateway, if it
exists. Here is the list of middleware to hopefully remove from this template:

- [ObfuscatePayloadOfServerErrors.cs](./Middleware/ObfuscatePayloadOfServerErrors.cs)
- [RateLimiting.cs](./Middleware/RateLimiting.cs)
- [RequestTimeouts.cs](./Middleware/RequestTimeouts.cs) (maybe, if you're
  feeling spicy and the environment makes sense)

#### Idempotent POSTs

If POSTs are in scope for the API, removing that middleware would be prudent.
Otherwise, the current implementation only uses an in memory cache to store its
idempotency tokens. Depending on your environment, it may be mildly to severely
critical to supplement that type with a persistance/distributed cache and/or
sticky sessions.

#### Authentication

If an API key or JWT were selected, the `appsettings*.json` files are not
configured with any authentication settings. These will need to be set up under
the `"Middleware"` section of the `IConfiguration`. See the chosen
authentication class for more.

### Request size limiting

This template does *not* have request size limits in place. As such, it is
recommended to configure an upstream resource to enforce this limit. The
observed consencus for the rule of thumb is to not allow requests with a body
larger than 1 MB. (NGINX defaults to 1 MB and IIS defaults to 30 MB).

### Alerting

It is very likely that alerting around CPU and RAM usage is beneficial.
Additionally, when deploying on the cloud, alerting around Billing is prudent
as well.
