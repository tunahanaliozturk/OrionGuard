using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moongazing.OrionGuard.AspNetCore.ExceptionHandling;
using Moongazing.OrionGuard.AspNetCore.Options;
using Moongazing.OrionGuard.Domain.Exceptions;
using Moongazing.OrionGuard.Domain.Rules;

namespace Moongazing.OrionGuard.AspNetCore.Tests.ExceptionHandling;

public class OrionGuardExceptionHandler_BusinessRuleTests
{
    private sealed class BrokenRule : BusinessRule
    {
        public override bool IsBroken() => true;
        public override string DefaultMessage => "Order must have at least one item.";
    }

    private static (HttpContext ctx, MemoryStream body) NewContext()
    {
        var ctx = new DefaultHttpContext();
        var body = new MemoryStream();
        ctx.Response.Body = body;
        return (ctx, body);
    }

    [Fact]
    public async Task TryHandleAsync_ShouldReturn422_WhenUseProblemDetailsTrueAndDefaults()
    {
        var (ctx, body) = NewContext();
        var options = new OrionGuardAspNetCoreOptions { UseProblemDetails = true };
        var handler = new OrionGuardExceptionHandler(NullLogger<OrionGuardExceptionHandler>.Instance, options);

        var handled = await handler.TryHandleAsync(ctx, new BusinessRuleValidationException(new BrokenRule()), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, ctx.Response.StatusCode);

        body.Position = 0;
        var pd = JsonSerializer.Deserialize<ValidationProblemDetails>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(pd);
        Assert.True(pd!.Errors.ContainsKey(nameof(BrokenRule)));
    }

    [Fact]
    public async Task TryHandleAsync_ShouldRespectCustomStatusCode()
    {
        var (ctx, body) = NewContext();
        var options = new OrionGuardAspNetCoreOptions { UseProblemDetails = true, BusinessRuleStatusCode = StatusCodes.Status400BadRequest };
        var handler = new OrionGuardExceptionHandler(NullLogger<OrionGuardExceptionHandler>.Instance, options);

        await handler.TryHandleAsync(ctx, new BusinessRuleValidationException(new BrokenRule()), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task TryHandleAsync_ShouldFallBackToSimpleJson_WhenUseProblemDetailsFalse()
    {
        var (ctx, body) = NewContext();
        var options = new OrionGuardAspNetCoreOptions { UseProblemDetails = false };
        var handler = new OrionGuardExceptionHandler(NullLogger<OrionGuardExceptionHandler>.Instance, options);

        var handled = await handler.TryHandleAsync(ctx, new BusinessRuleValidationException(new BrokenRule()), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, ctx.Response.StatusCode);

        body.Position = 0;
        var json = Encoding.UTF8.GetString(body.ToArray());
        Assert.Contains(nameof(BrokenRule), json);
        Assert.Contains("Order must have at least one item.", json);
    }
}
