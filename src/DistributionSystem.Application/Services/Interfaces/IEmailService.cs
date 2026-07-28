namespace DistributionSystem.Application.Services.Interfaces;

public interface IEmailService
{
    Task SendEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default);
    Task SendOrderNotificationToAdminAsync(string orderNumber, string customerInfo, decimal totalAmount, CancellationToken cancellationToken = default);
}
