using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moongazing.OrionGuard.AspNetCore.Attributes;
using Moongazing.OrionGuard.AspNetCore.Extensions;
using Moongazing.OrionGuard.AspNetCore.Options;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.AspNetCore.Tests.Filters;

public sealed record MvcOrderRequest(string? Code, int Quantity);

// MVC only discovers public top-level controllers, so these cannot be nested in the test class.
[Route("orders")]
public sealed class MvcOrdersController : ControllerBase
{
    [HttpPost]
    [ValidateRequest]
    public IActionResult Create([FromBody] MvcOrderRequest request) => Ok(request);
}

[Route("orders-both-levels")]
[ValidateRequest]
public sealed class MvcOrdersBothLevelsController : ControllerBase
{
    [HttpPost]
    [ValidateRequest]
    public IActionResult Create([FromBody] MvcOrderRequest request) => Ok(request);
}

public sealed class ValidateRequestAttributeTests
{
    private sealed class PositiveQuantityValidator : AbstractValidator<MvcOrderRequest>
    {
        public PositiveQuantityValidator()
        {
            RuleFor(r => r.Quantity > 0, "Quantity must be positive.", "Quantity");
        }
    }

    private sealed class ValidationCounter
    {
        public int Calls;
    }

    private sealed class CountingValidator : IValidator<MvcOrderRequest>
    {
        private readonly ValidationCounter counter;

        public CountingValidator(ValidationCounter counter)
        {
            this.counter = counter;
        }

        public GuardResult Validate(MvcOrderRequest value)
        {
            Interlocked.Increment(ref counter.Calls);
            return GuardResult.Success();
        }

        public Task<GuardResult> ValidateAsync(MvcOrderRequest value, CancellationToken cancellationToken = default) =>
            Task.FromResult(Validate(value));
    }

    private static async Task<IHost> StartAsync(
        Action<OrionGuardAspNetCoreOptions>? configure = null,
        Action<IServiceCollection>? configureServices = null)
    {
        return await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddControllers().AddApplicationPart(typeof(MvcOrdersController).Assembly);
                    services.AddOrionGuardAspNetCore(configure);
                    services.AddValidator<MvcOrderRequest, PositiveQuantityValidator>();
                    configureServices?.Invoke(services);
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                });
            })
            .StartAsync();
    }

    private static async Task<HttpResponseMessage> PostAsync(IHost host, string path, MvcOrderRequest request) =>
        await host.GetTestClient().PostAsJsonAsync(path, request);

    [Fact]
    public async Task Invalid_body_returns_422_ValidationProblemDetails_by_default()
    {
        using var host = await StartAsync();

        var response = await PostAsync(host, "/orders", new MvcOrderRequest("A-1", 0));

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(422, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(
            "Quantity must be positive.",
            body.RootElement.GetProperty("errors").GetProperty("Quantity")[0].GetString());
    }

    [Fact]
    public async Task Invalid_body_uses_the_configured_DefaultStatusCode()
    {
        using var host = await StartAsync(options => options.DefaultStatusCode = 400);

        var response = await PostAsync(host, "/orders", new MvcOrderRequest("A-1", 0));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, body.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Invalid_body_is_written_as_plain_json_when_UseProblemDetails_is_false()
    {
        using var host = await StartAsync(options => options.UseProblemDetails = false);

        var response = await PostAsync(host, "/orders", new MvcOrderRequest("A-1", 0));

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("""{"Quantity":["Quantity must be positive."]}""", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Valid_body_reaches_the_action_after_every_validator_ran_once()
    {
        var counter = new ValidationCounter();
        using var host = await StartAsync(configureServices: services =>
        {
            services.AddSingleton(counter);
            services.AddValidator<MvcOrderRequest, CountingValidator>();
        });

        // The attribute sits on both the controller and the action; validation must still run only once.
        var response = await PostAsync(host, "/orders-both-levels", new MvcOrderRequest("A-1", 2));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, counter.Calls);
    }
}
