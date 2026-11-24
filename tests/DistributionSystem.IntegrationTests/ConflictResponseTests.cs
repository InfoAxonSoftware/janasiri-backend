using DistributionSystem.API.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DistributionSystem.IntegrationTests;

public class ConflictResponseTests
{
    [Fact]
    public async Task Stale_Trash_update_returns_HTTP_409_instead_of_500()
    {
        var context = new DefaultHttpContext();
        await using var body = new MemoryStream();
        context.Response.Body = body;
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new DbUpdateConcurrencyException("stale Admin Trash write"),
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        body.Position = 0;
        using var reader = new StreamReader(body);
        var responseBody = await reader.ReadToEndAsync();
        Assert.Contains("changed by another request", responseBody);
        Assert.DoesNotContain("stale Admin Trash write", responseBody);
    }
}
