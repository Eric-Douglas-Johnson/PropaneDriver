using Azure;
using Azure.Communication.Email;
using Azure.Identity;

namespace PropaneDriver.Server.Services
{
    public class EmailService
    {
        private readonly EmailClient _emailClient;
        private readonly string _senderAddress;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
        {
            _logger = logger;

            var endpoint = configuration["AcsEndpoint"]
                ?? throw new InvalidOperationException("AcsEndpoint not configured.");
            _senderAddress = configuration["AcsSenderAddress"]
                ?? throw new InvalidOperationException("AcsSenderAddress not configured.");

            _emailClient = new EmailClient(new Uri(endpoint), new DefaultAzureCredential());
        }

        public async Task<bool> SendPasswordResetAsync(string toEmail, string toName, string resetUrl)
        {
            try
            {
                var subject = "PropaneDriver — Password Reset Request";

                var html = $@"
                    <html>
                    <body style='font-family: Arial, sans-serif; color: #333;'>
                        <h2>Password Reset Request</h2>
                        <p>Hi {System.Net.WebUtility.HtmlEncode(toName)},</p>
                        <p>We received a request to reset your PropaneDriver password. Click the link below to set a new password. This link will expire in 1 hour.</p>
                        <p><a href='{resetUrl}' style='display: inline-block; padding: 10px 20px; background-color: #0d6efd; color: white; text-decoration: none; border-radius: 5px;'>Reset Password</a></p>
                        <p>Or paste this URL into your browser:</p>
                        <p><code>{resetUrl}</code></p>
                        <p>If you didn't request this, you can safely ignore this email.</p>
                        <p>— PropaneDriver</p>
                    </body>
                    </html>";

                var plainText = $"Hi {toName},\n\nClick the link below to reset your PropaneDriver password (expires in 1 hour):\n\n{resetUrl}\n\nIf you didn't request this, ignore this email.";

                var emailMessage = new EmailMessage(
                    senderAddress: _senderAddress,
                    content: new EmailContent(subject)
                    {
                        PlainText = plainText,
                        Html = html
                    },
                    recipients: new EmailRecipients(new[] { new EmailAddress(toEmail) }));

                var operation = await _emailClient.SendAsync(WaitUntil.Completed, emailMessage);
                return operation.HasValue && operation.Value.Status == EmailSendStatus.Succeeded;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send password reset email to {Email}", toEmail);
                return false;
            }
        }
    }
}
