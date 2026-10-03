using System.Net;
using System.Net.Mail;

namespace FlowOps.Api.Email;

public interface IEmailDelivery
{
    Task SendAsync(string address, string subject, string body);
}

public sealed class SmtpEmailDelivery(IConfiguration config, IHostEnvironment environment) : IEmailDelivery
{
    public async Task SendAsync(string address, string subject, string body)
    {
        using var message = new MailMessage(config["Email:From"] ?? "flowops@localhost", address,
            subject, body);
        using var client = new SmtpClient(config["Email:Host"] ?? "localhost",
            int.TryParse(config["Email:Port"], out var port) ? port : 1025)
        {
            EnableSsl = config.GetValue("Email:UseTls", !environment.IsDevelopment())
        };
        if (!string.IsNullOrWhiteSpace(config["Email:Username"]))
            client.Credentials = new NetworkCredential(config["Email:Username"], config["Email:Password"]);
        await client.SendMailAsync(message);
    }
}
