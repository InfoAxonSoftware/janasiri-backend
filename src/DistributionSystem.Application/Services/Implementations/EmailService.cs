using System.Net;
using System.Net.Mail;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DistributionSystem.Application.Services.Implementations;

public class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public EmailService(
        IConfiguration configuration,
        ILogger<EmailService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _configuration = configuration;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public async Task SendEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        // Business requirement: email delivery is globally disabled unless explicitly enabled.
        var emailEnabled = _configuration.GetValue<bool>("SmtpSettings:Enabled");
        if (!emailEnabled)
        {
            _logger.LogInformation("Email sending is disabled. Skipping email to {To}. Subject: {Subject}", toEmail, subject);
            return;
        }

        var smtpHost = _configuration["SmtpSettings:Host"];
        if (string.IsNullOrWhiteSpace(smtpHost))
        {
            _logger.LogWarning("SMTP is not configured (SmtpSettings:Host is empty). Email to {To} skipped.", toEmail);
            return;
        }

        var port = int.TryParse(_configuration["SmtpSettings:Port"], out var p) ? p : 587;
        var username = _configuration["SmtpSettings:Username"] ?? "";
        var password = _configuration["SmtpSettings:Password"] ?? "";
        var fromEmail = _configuration["SmtpSettings:FromEmail"] ?? "noreply@janasiri.lk";
        var fromName = _configuration["SmtpSettings:FromName"] ?? "Janasiri Distributors";
        var enableSsl = _configuration["SmtpSettings:EnableSsl"] != "false"; // default true

        try
        {
#pragma warning disable CS0618 // SmtpClient is obsolete but functional; MailKit can replace later
            using var client = new SmtpClient(smtpHost, port)
            {
                Credentials = new NetworkCredential(username, password),
                EnableSsl = enableSsl
            };

            var message = new MailMessage
            {
                From = new MailAddress(fromEmail, fromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            message.To.Add(toEmail);

            await client.SendMailAsync(message, cancellationToken);
#pragma warning restore CS0618

            _logger.LogInformation("Email sent to {To}: {Subject}", toEmail, subject);

            // If the recipient exists as a user, create a notification so they get a toast in the UI.
            // Use a new scope so we don't rely on the caller's scoped DI lifetime.
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

            var user = await unitOfWork.Repository<Domain.Entities.User>().Query()
                .FirstOrDefaultAsync(u => u.Email == toEmail, cancellationToken);

            if (user != null)
            {
                await notificationService.SendNotificationAsync(
                    user.Id,
                    Domain.Enums.NotificationType.General,
                    "Email Sent",
                    $"An email with subject '{subject}' was sent to you.",
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To}: {Subject}", toEmail, subject);
            // Don't throw — email failure shouldn't break the main flow
        }
    }

    public async Task SendOrderNotificationToAdminAsync(string orderNumber, string customerInfo, decimal totalAmount, CancellationToken cancellationToken = default)
    {
        var adminEmail = _configuration["SmtpSettings:AdminEmail"] ?? _configuration["SwaggerContact:Email"] ?? "support@janasiri.lk";

        var subject = $"New Order #{orderNumber}";
        var body = $"""
            <div style="font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;">
                <h2 style="color: #1e40af;">New Order Received</h2>
                <table style="border-collapse: collapse; width: 100%;">
                    <tr><td style="padding: 8px; border: 1px solid #e5e7eb; font-weight: bold;">Order Number</td>
                        <td style="padding: 8px; border: 1px solid #e5e7eb;">#{orderNumber}</td></tr>
                    <tr><td style="padding: 8px; border: 1px solid #e5e7eb; font-weight: bold;">Customer</td>
                        <td style="padding: 8px; border: 1px solid #e5e7eb;">{customerInfo}</td></tr>
                    <tr><td style="padding: 8px; border: 1px solid #e5e7eb; font-weight: bold;">Total Amount</td>
                        <td style="padding: 8px; border: 1px solid #e5e7eb;">Rs. {totalAmount:N2}</td></tr>
                </table>
                <p style="margin-top: 16px; color: #6b7280;">Please log in to the admin dashboard to review and approve this order.</p>
            </div>
            """;

        await SendEmailAsync(adminEmail, subject, body, cancellationToken);
    }
}
