namespace Fcg.Notifications.Email;

/// <summary>Configuração do canal de e-mail (seção "Email" do appsettings/env vars).</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Provider de envio: "Console" (default) ou "Smtp".</summary>
    public string Provider { get; set; } = "Console";

    public SmtpOptions Smtp { get; set; } = new();

    public sealed class SmtpOptions
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 587;
        public string User { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string From { get; set; } = "noreply@fcg.com";
    }
}
