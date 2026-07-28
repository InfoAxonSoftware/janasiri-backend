using System.Net;
using System.Text.Json;
using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.Exceptions;

namespace DistributionSystem.API.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception occurred: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var (statusCode, message, errors) = exception switch
        {
            NotFoundException ex => (HttpStatusCode.NotFound, ex.Message, (IEnumerable<string>?)null),
            ForbiddenException ex => (HttpStatusCode.Forbidden, ex.Message, (IEnumerable<string>?)null),
            BusinessException ex => (HttpStatusCode.BadRequest, ex.Message, (IEnumerable<string>?)null),
            AppValidationException ex => (HttpStatusCode.UnprocessableEntity, ex.Message, ex.Errors.SelectMany(e => e.Value)),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "Unauthorized access", (IEnumerable<string>?)null),
            _ => (HttpStatusCode.InternalServerError, "An internal server error occurred", (IEnumerable<string>?)null)
        };

        context.Response.StatusCode = (int)statusCode;

        var response = ApiResponse<object>.ErrorResponse(message, errors);
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await context.Response.WriteAsync(json);
    }
}
