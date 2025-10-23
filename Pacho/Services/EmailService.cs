using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Pacho.Models;

namespace Pacho.Services
{
    public class EmailService : IEmailService
    {
        private readonly EmailSettings _settings;
        public EmailService(IOptions<EmailSettings> settings) => _settings = settings.Value;

        public async Task SendAsync(string to, string subject, string htmlBody)
        {
            var msg = new MimeMessage();
            msg.From.Add(new MailboxAddress("Pacho", _settings.UserName));     // remitente = cuenta autenticada
            msg.To.Add(MailboxAddress.Parse(to));
            msg.Subject = subject;

            // 🧾 Mostrar credenciales (sin exponer la contraseña completa)
            Console.WriteLine("---------------------------------------------------");
            Console.WriteLine("📧 Verificando credenciales SMTP cargadas:");
            Console.WriteLine($"  🏠 Servidor:  {_settings.SmtpServer}");
            Console.WriteLine($"  🔌 Puerto:    {_settings.Port}");
            Console.WriteLine($"  👤 Usuario:   {_settings.UserName}");
            Console.WriteLine($"  🔑 Password:  {_settings.Password}");
            Console.WriteLine("---------------------------------------------------");

            var builder = new BodyBuilder { HtmlBody = htmlBody };
            msg.Body = builder.ToMessageBody();

            //var smtp = new SmtpClient();
            //await smtp.ConnectAsync(_settings.SmtpServer, _settings.Port, SecureSocketOptions.StartTls);
            //await smtp.AuthenticateAsync(_settings.UserName, _settings.Password); // usa App Password
            //await smtp.SendAsync(msg);
            //await smtp.DisconnectAsync(true);

            using (var client = new SmtpClient())
            {
                // Conectar al servidor SMTP de Gmail
                await client.ConnectAsync(_settings.SmtpServer, _settings.Port, MailKit.Security.SecureSocketOptions.StartTls);

                // Autenticarse con la cuenta de Gmail
                client.Authenticate(_settings.UserName, _settings.Password);

                // Enviar el mensaje
                await client.SendAsync(msg);

                // Desconectar del servidor
                client.Disconnect(true);
            }
        }
    }

}
