using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.Grpc.Tests;

public sealed class OrionGuardInterceptorTests
{
    private sealed class CreateUserRequest
    {
        public string Email { get; init; } = "";
    }

    private sealed class EmailRequiredValidator : AbstractValidator<CreateUserRequest>
    {
        public EmailRequiredValidator()
        {
            RuleFor(r => !string.IsNullOrEmpty(r.Email), "Email is required.", "Email");
        }
    }

    private sealed class AsyncOnlyValidator : AbstractValidator<CreateUserRequest>
    {
        public AsyncOnlyValidator()
        {
            RuleForAsync(async r =>
            {
                await Task.Yield();
                return r.Email != "taken@example.com";
            }, "Email is already taken.", "Email");
        }
    }

    private sealed class BlockedDomains
    {
        public bool IsBlocked(string email) => email.EndsWith("@blocked.example", StringComparison.Ordinal);
    }

    private sealed class BlockedDomainValidator : IValidator<CreateUserRequest>
    {
        private readonly BlockedDomains blockedDomains;

        public BlockedDomainValidator(BlockedDomains blockedDomains)
        {
            this.blockedDomains = blockedDomains;
        }

        public GuardResult Validate(CreateUserRequest value) =>
            blockedDomains.IsBlocked(value.Email)
                ? GuardResult.Failure("Email", "Email domain is blocked.")
                : GuardResult.Success();

        public Task<GuardResult> ValidateAsync(CreateUserRequest value, CancellationToken cancellationToken = default) =>
            Task.FromResult(Validate(value));
    }

    private sealed class TestServerCallContext : ServerCallContext
    {
        private readonly Dictionary<object, object> userState = new();

        public TestServerCallContext(IServiceProvider requestServices)
        {
            // grpc-dotnet's GetHttpContext() reads the HttpContext from this user-state key when the call
            // is not running inside the ASP.NET Core server, which is how its own test helpers set it up.
            userState["__HttpContext"] = new DefaultHttpContext { RequestServices = requestServices };
        }

        protected override string MethodCore => "/users.Users/Create";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "ipv4:127.0.0.1:5000";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore { get; } = new();
        protected override CancellationToken CancellationTokenCore => CancellationToken.None;
        protected override Metadata ResponseTrailersCore { get; } = new();
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore { get; } = new(null, new Dictionary<string, List<AuthProperty>>());
        protected override IDictionary<object, object> UserStateCore => userState;

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) =>
            throw new NotSupportedException();

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }

    private sealed class ListStreamReader<T> : IAsyncStreamReader<T>
    {
        private readonly Queue<T> remaining;

        public ListStreamReader(params T[] messages)
        {
            remaining = new Queue<T>(messages);
        }

        public T Current { get; private set; } = default!;

        public Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (!remaining.TryDequeue(out var next)) return Task.FromResult(false);
            Current = next;
            return Task.FromResult(true);
        }
    }

    private sealed class NullStreamWriter<T> : IServerStreamWriter<T>
    {
        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(T message) => Task.CompletedTask;
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        // Validators are registered as scoped in these tests, and scope validation makes resolving them
        // from the root provider throw, so every test also proves resolution uses the request scope.
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static CreateUserRequest Request(string email) => new() { Email = email };

    [Fact]
    public async Task Unary_call_with_an_invalid_request_fails_with_InvalidArgument_and_the_errors_trailer()
    {
        using var provider = BuildProvider(s => s.AddScoped<IValidator<CreateUserRequest>, EmailRequiredValidator>());
        using var scope = provider.CreateScope();
        var interceptor = new OrionGuardInterceptor(provider);

        var exception = await Assert.ThrowsAsync<RpcException>(() => interceptor.UnaryServerHandler(
            Request(""),
            new TestServerCallContext(scope.ServiceProvider),
            (_, _) => Task.FromResult("created")));

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
        Assert.Equal("Email is required.", exception.Status.Detail);
        Assert.Equal(
            """[{"ParameterName":"Email","Message":"Email is required."}]""",
            exception.Trailers.GetValue("validation-errors-json"));
    }

    [Fact]
    public async Task Unary_call_with_a_valid_request_reaches_the_service_method()
    {
        using var provider = BuildProvider(s => s.AddScoped<IValidator<CreateUserRequest>, EmailRequiredValidator>());
        using var scope = provider.CreateScope();
        var interceptor = new OrionGuardInterceptor(provider);

        var response = await interceptor.UnaryServerHandler(
            Request("ada@example.com"),
            new TestServerCallContext(scope.ServiceProvider),
            (_, _) => Task.FromResult("created"));

        Assert.Equal("created", response);
    }

    [Fact]
    public async Task Unary_call_enforces_async_rules()
    {
        using var provider = BuildProvider(s => s.AddScoped<IValidator<CreateUserRequest>, AsyncOnlyValidator>());
        using var scope = provider.CreateScope();
        var interceptor = new OrionGuardInterceptor(provider);

        var exception = await Assert.ThrowsAsync<RpcException>(() => interceptor.UnaryServerHandler(
            Request("taken@example.com"),
            new TestServerCallContext(scope.ServiceProvider),
            (_, _) => Task.FromResult("created")));

        Assert.Equal("Email is already taken.", exception.Status.Detail);
    }

    [Fact]
    public async Task Unary_call_resolves_scoped_validators_from_the_request_scope()
    {
        using var provider = BuildProvider(s =>
        {
            s.AddScoped<BlockedDomains>();
            s.AddScoped<IValidator<CreateUserRequest>, BlockedDomainValidator>();
        });
        using var scope = provider.CreateScope();
        // The interceptor is a singleton built from the root provider, exactly as AddOrionGuardGrpc registers it.
        var interceptor = new OrionGuardInterceptor(provider);

        var exception = await Assert.ThrowsAsync<RpcException>(() => interceptor.UnaryServerHandler(
            Request("eve@blocked.example"),
            new TestServerCallContext(scope.ServiceProvider),
            (_, _) => Task.FromResult("created")));

        Assert.Equal("Email domain is blocked.", exception.Status.Detail);
    }

    [Fact]
    public async Task Server_streaming_call_validates_the_request_before_the_service_method_runs()
    {
        using var provider = BuildProvider(s => s.AddScoped<IValidator<CreateUserRequest>, AsyncOnlyValidator>());
        using var scope = provider.CreateScope();
        var interceptor = new OrionGuardInterceptor(provider);
        var serviceMethodRan = false;

        var exception = await Assert.ThrowsAsync<RpcException>(() => interceptor.ServerStreamingServerHandler(
            Request("taken@example.com"),
            new NullStreamWriter<string>(),
            new TestServerCallContext(scope.ServiceProvider),
            (_, _, _) =>
            {
                serviceMethodRan = true;
                return Task.CompletedTask;
            }));

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
        Assert.False(serviceMethodRan);
    }

    [Fact]
    public async Task Client_streaming_call_fails_at_the_first_invalid_message()
    {
        using var provider = BuildProvider(s => s.AddScoped<IValidator<CreateUserRequest>, AsyncOnlyValidator>());
        using var scope = provider.CreateScope();
        var interceptor = new OrionGuardInterceptor(provider);
        var processed = new List<string>();

        var exception = await Assert.ThrowsAsync<RpcException>(() => interceptor.ClientStreamingServerHandler(
            new ListStreamReader<CreateUserRequest>(Request("ada@example.com"), Request("taken@example.com"), Request("bob@example.com")),
            new TestServerCallContext(scope.ServiceProvider),
            async (stream, _) =>
            {
                while (await stream.MoveNext(CancellationToken.None))
                {
                    processed.Add(stream.Current.Email);
                }
                return "done";
            }));

        Assert.Equal("Email is already taken.", exception.Status.Detail);
        Assert.Equal(new[] { "ada@example.com" }, processed);
    }

    [Fact]
    public async Task Duplex_streaming_call_validates_each_message_from_the_request_scope()
    {
        using var provider = BuildProvider(s =>
        {
            s.AddScoped<BlockedDomains>();
            s.AddScoped<IValidator<CreateUserRequest>, BlockedDomainValidator>();
        });
        using var scope = provider.CreateScope();
        var interceptor = new OrionGuardInterceptor(provider);

        var exception = await Assert.ThrowsAsync<RpcException>(() => interceptor.DuplexStreamingServerHandler(
            new ListStreamReader<CreateUserRequest>(Request("ada@example.com"), Request("eve@blocked.example")),
            new NullStreamWriter<string>(),
            new TestServerCallContext(scope.ServiceProvider),
            async (stream, _, _) =>
            {
                while (await stream.MoveNext(CancellationToken.None))
                {
                }
            }));

        Assert.Equal("Email domain is blocked.", exception.Status.Detail);
    }
}
