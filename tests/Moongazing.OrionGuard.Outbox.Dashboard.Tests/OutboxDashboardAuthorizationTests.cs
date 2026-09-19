namespace Moongazing.OrionGuard.Outbox.Dashboard.Tests;

using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;

public sealed class OutboxDashboardAuthorizationTests
{
    private const string FailedPath = "/_orion/outbox/failed";

    [Fact]
    public async Task Dashboard_rejects_anonymous_callers_when_the_host_has_no_fallback_policy()
    {
        using var host = await StartAsync(configureAuthorization: _ => { });
        using var client = host.GetTestClient();

        var listing = await client.GetAsync(FailedPath);
        var replay = await client.PostAsync($"/_orion/outbox/{Guid.NewGuid()}/replay", content: null);
        var discard = await client.PostAsync($"/_orion/outbox/{Guid.NewGuid()}/discard", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, listing.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, discard.StatusCode);
    }

    [Fact]
    public async Task Dashboard_admits_an_authenticated_user_when_the_host_has_no_fallback_policy()
    {
        using var host = await StartAsync(configureAuthorization: _ => { });
        using var client = host.GetTestClient();

        var response = await client.SendAsync(Get(FailedPath, user: "operator"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_applies_the_host_fallback_policy_instead_of_the_weaker_default_policy()
    {
        using var host = await StartAsync(configureAuthorization: o =>
            o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireRole("outbox-ops").Build());
        using var client = host.GetTestClient();

        var withoutRole = await client.SendAsync(Get(FailedPath, user: "operator"));
        var withRole = await client.SendAsync(Get(FailedPath, user: "operator", role: "outbox-ops"));

        Assert.Equal(HttpStatusCode.Forbidden, withoutRole.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withRole.StatusCode);
    }

    [Fact]
    public async Task Dashboard_applies_a_fallback_policy_supplied_by_a_custom_policy_provider()
    {
        // The provider supplies the fallback without populating AuthorizationOptions, so a check
        // against the options alone would attach the weaker default policy instead.
        using var host = await StartAsync(
            configureAuthorization: _ => { },
            configureServices: services => services.AddSingleton<IAuthorizationPolicyProvider, RoleFallbackPolicyProvider>());
        using var client = host.GetTestClient();

        var withoutRole = await client.SendAsync(Get(FailedPath, user: "operator"));
        var withRole = await client.SendAsync(Get(FailedPath, user: "operator", role: "outbox-ops"));

        Assert.Equal(HttpStatusCode.Forbidden, withoutRole.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withRole.StatusCode);
    }

    [Fact]
    public async Task Dashboard_requires_the_named_policy_when_one_is_configured()
    {
        using var host = await StartAsync(
            configureAuthorization: o => o.AddPolicy("OutboxOps", p => p.RequireRole("outbox-ops")),
            configureDashboard: o => o.AuthorizationPolicyName = "OutboxOps");
        using var client = host.GetTestClient();

        var withoutRole = await client.SendAsync(Get(FailedPath, user: "operator"));
        var withRole = await client.SendAsync(Get(FailedPath, user: "operator", role: "outbox-ops"));

        Assert.Equal(HttpStatusCode.Forbidden, withoutRole.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withRole.StatusCode);
    }

    [Fact]
    public async Task Dashboard_is_anonymous_only_when_AllowAnonymous_is_set()
    {
        using var host = await StartAsync(
            configureAuthorization: _ => { },
            configureDashboard: o => o.AllowAnonymous = true);
        using var client = host.GetTestClient();

        var response = await client.GetAsync(FailedPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static HttpRequestMessage Get(string path, string user, string? role = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(HeaderAuthenticationHandler.UserHeader, user);
        if (role is not null)
        {
            request.Headers.Add(HeaderAuthenticationHandler.RoleHeader, role);
        }
        return request;
    }

    private static Task<IHost> StartAsync(
        Action<AuthorizationOptions> configureAuthorization,
        Action<OutboxDashboardOptions>? configureDashboard = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var databaseName = "dashboard-auth-" + Guid.NewGuid().ToString("N");
        return new HostBuilder()
            .ConfigureWebHost(builder =>
            {
                builder.UseTestServer();
                builder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddDbContext<AuthTestDbContext>(options => options
                        .UseInMemoryDatabase(databaseName)
                        .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)));
                    services.AddAuthentication(HeaderAuthenticationHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(HeaderAuthenticationHandler.SchemeName, _ => { });
                    services.AddAuthorization(configureAuthorization);
                    configureServices?.Invoke(services);
                });
                builder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapOutboxDashboard<AuthTestDbContext>(configureDashboard));
                });
            })
            .StartAsync();
    }

    private sealed class HeaderAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "TestHeader";
        public const string UserHeader = "X-Test-User";
        public const string RoleHeader = "X-Test-Role";

        public HeaderAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeader, out var user))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim> { new(ClaimTypes.Name, user.ToString()) };
            if (Request.Headers.TryGetValue(RoleHeader, out var role))
            {
                claims.Add(new Claim(ClaimTypes.Role, role.ToString()));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }

    private sealed class RoleFallbackPolicyProvider : IAuthorizationPolicyProvider
    {
        private static readonly AuthorizationPolicy RequireOutboxOpsRole =
            new AuthorizationPolicyBuilder().RequireRole("outbox-ops").Build();

        private readonly DefaultAuthorizationPolicyProvider inner;

        public RoleFallbackPolicyProvider(IOptions<AuthorizationOptions> options)
        {
            inner = new DefaultAuthorizationPolicyProvider(options);
        }

        public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => inner.GetDefaultPolicyAsync();

        public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() =>
            Task.FromResult<AuthorizationPolicy?>(RequireOutboxOpsRole);

        public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName) => inner.GetPolicyAsync(policyName);
    }

    private sealed class AuthTestDbContext : DbContext
    {
        public AuthTestDbContext(DbContextOptions<AuthTestDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<OutboxMessage>().ToTable("OutboxMessages").HasKey(x => x.Id);
    }
}
