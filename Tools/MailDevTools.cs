using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace MailDevMcp.Tools;

[McpServerToolType]
public class MailDevTools(IHttpClientFactory httpClientFactory)
{
    private const string ContainerName = "mdmcp-maildev";
    private const string CertVolumeName = "mdmcp-maildev-certs";
    private const int DefaultSmtpPort = 1025;
    private const int DefaultApiPort = 1080;
    private const string ConnectionErrorMessage = "Cannot connect to MailDev. Please make sure MailDev is running.";

    [McpServerTool, Description("Start the MailDev Docker container.")]
    public static async Task<string> StartMaildev(
        [Description("SMTP port number (default: 1025)")] int smtpPort = DefaultSmtpPort,
        [Description("Web UI / API port number (default: 1080)")] int apiPort = DefaultApiPort,
        [Description("SMTP authentication username (omit to disable authentication)")] string? smtpUser = null,
        [Description("SMTP authentication password (required when smtpUser is specified)")] string? smtpPassword = null,
        [Description("Enable TLS for SMTP connections (default: false)")] bool enableSsl = false)
    {
        var (exitCode, output, _) = await RunDockerAsync($"inspect -f \"{{{{.State.Running}}}}\" {ContainerName}");
        if (exitCode == 0 && output.Trim().Contains("true"))
        {
            return $"MailDev is already running.\n- SMTP: localhost:{smtpPort}\n- Web UI: http://localhost:{apiPort}";
        }
        if (exitCode == 0)
        {
            await RunDockerAsync($"rm -f {ContainerName}");
        }
        if (enableSsl)
        {
            await RunDockerAsync($"volume rm -f {CertVolumeName}");
            await RunDockerAsync($"volume create {CertVolumeName}");
            var certResult = await RunDockerAsync(
                $"run --rm -v {CertVolumeName}:/certs alpine sh -c "
                + $"\"apk add --no-cache openssl > /dev/null 2>&1 "
                + $"&& openssl req -x509 -newkey rsa:2048 -keyout /certs/key.pem -out /certs/cert.pem "
                + $"-days 365 -nodes -subj '/CN=localhost' 2>/dev/null "
                + $"&& chmod 644 /certs/key.pem /certs/cert.pem\"");
            if (certResult.ExitCode != 0)
            {
                return $"Failed to generate self-signed certificate.\n{certResult.Error}";
            }
        }
        var hasAuth = !string.IsNullOrEmpty(smtpUser);
        var args = new StringBuilder($"run -d --name {ContainerName} -p {smtpPort}:1025 -p {apiPort}:1080");
        if (enableSsl)
        {
            args.Append($" -v {CertVolumeName}:/cert:ro");
        }
        args.Append(" maildev/maildev");
        if (hasAuth)
        {
            args.Append($" --incoming-user {smtpUser}");
            if (!string.IsNullOrEmpty(smtpPassword))
            {
                args.Append($" --incoming-pass {smtpPassword}");
            }
        }
        if (enableSsl)
        {
            args.Append(" --incoming-secure --incoming-cert /cert/cert.pem --incoming-key /cert/key.pem");
        }
        var result = await RunDockerAsync(args.ToString());
        if (result.ExitCode != 0)
        {
            return $"Failed to start MailDev.\n{result.Error}";
        }
        await Task.Delay(2000);
        var sb = new StringBuilder();
        sb.AppendLine("MailDev started.");
        sb.AppendLine($"- SMTP: localhost:{smtpPort}");
        sb.AppendLine($"- Web UI: http://localhost:{apiPort}");
        sb.AppendLine($"- Container ID: {result.Output.Trim()[..12]}");
        sb.AppendLine($"- Auth: {(hasAuth ? $"enabled (user: {smtpUser})" : "disabled")}");
        sb.AppendLine($"- TLS: {(enableSsl ? "enabled (self-signed certificate)" : "disabled")}");
        sb.AppendLine();
        sb.AppendLine("Recommended Mail.json settings:");
        sb.AppendLine($"  \"SmtpHost\": \"localhost\"");
        sb.AppendLine($"  \"SmtpPort\": {smtpPort}");
        sb.AppendLine($"  \"SmtpUserName\": {(hasAuth ? $"\"{smtpUser}\"" : "null")}");
        sb.AppendLine($"  \"SmtpPassword\": {(!string.IsNullOrEmpty(smtpPassword) ? $"\"{smtpPassword}\"" : "null")}");
        sb.AppendLine($"  \"SmtpEnableSsl\": {(enableSsl ? "true" : "false")}");
        if (enableSsl)
        {
            sb.AppendLine($"  \"ServerCertificateValidationCallback\": true  // Required for self-signed certificate");
            sb.AppendLine($"  \"SecureSocketOptions\": \"SslOnConnect\"");
        }
        return sb.ToString();
    }

    [McpServerTool, Description("Stop and remove the MailDev Docker container.")]
    public static async Task<string> StopMaildev()
    {
        var (exitCode, _, error) = await RunDockerAsync($"rm -f {ContainerName}");
        await RunDockerAsync($"volume rm -f {CertVolumeName}");
        return exitCode == 0
            ? "MailDev stopped and removed."
            : $"Failed to stop MailDev.\n{error}";
    }

    [McpServerTool, Description("Check the running status of MailDev.")]
    public static async Task<string> MaildevStatus()
    {
        var (exitCode, output, _) = await RunDockerAsync(
            $"inspect -f \"{{{{.State.Status}}}} (Ports: {{{{range $p, $conf := .NetworkSettings.Ports}}}}{{{{$p}}}}->{{{{(index $conf 0).HostPort}}}} {{{{end}}}})\" {ContainerName}");
        if (exitCode != 0)
        {
            return "MailDev is not running (container not found).";
        }
        var sb = new StringBuilder();
        sb.AppendLine($"MailDev status: {output.Trim()}");
        var envResult = await RunDockerAsync($"inspect -f \"{{{{range .Config.Env}}}}{{{{println .}}}}{{{{end}}}}\" {ContainerName}");
        if (envResult.ExitCode == 0)
        {
            var cmdResult = await RunDockerAsync($"inspect -f \"{{{{json .Config.Cmd}}}}\" {ContainerName}");
            var cmd = cmdResult.ExitCode == 0 ? cmdResult.Output.Trim() : "";
            var hasAuth = cmd.Contains("--incoming-user");
            var hasSsl = cmd.Contains("--incoming-secure");
            sb.AppendLine($"- Auth: {(hasAuth ? "enabled" : "disabled")}");
            sb.AppendLine($"- TLS: {(hasSsl ? "enabled" : "disabled")}");
        }
        return sb.ToString();
    }

    [McpServerTool, Description("Retrieve the list of received emails in MailDev.")]
    public async Task<string> ListEmails()
    {
        try
        {
            var client = httpClientFactory.CreateClient("MailDev");
            var response = await client.GetAsync("/email");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var emails = doc.RootElement.EnumerateArray().ToList();
            if (emails.Count == 0)
            {
                return "No emails received.";
            }
            var lines = emails.Select((e, i) => FormatEmailSummary(e, i));
            return $"Received emails ({emails.Count}):\n\n{string.Join("\n\n", lines)}";
        }
        catch (HttpRequestException)
        {
            return ConnectionErrorMessage;
        }
    }

    [McpServerTool, Description("Get the details of an email by ID, including attachment information.")]
    public async Task<string> GetEmail(
        [Description("Email ID (obtained from list_emails)")] string id)
    {
        try
        {
            var client = httpClientFactory.CreateClient("MailDev");
            var response = await client.GetAsync($"/email/{id}");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var e = doc.RootElement;
            var from = FormatAddresses(e, "from");
            var to = FormatAddresses(e, "to");
            var subject = e.GetProperty("subject").GetString() ?? "(no subject)";
            var text = e.TryGetProperty("text", out var t) ? t.GetString() : "";
            var result = $"From: {from}\nTo: {to}\nSubject: {subject}\n\nBody:\n{text}";
            if (e.TryGetProperty("attachments", out var attachments))
            {
                var attLines = attachments.EnumerateArray().Select((a, i) =>
                {
                    var fileName = a.GetProperty("fileName").GetString();
                    var contentType = a.TryGetProperty("contentType", out var ct)
                        ? ct.GetString()
                        : "unknown";
                    var size = a.TryGetProperty("length", out var len)
                        ? len.GetInt64()
                        : 0;
                    return $"  [{i}] {fileName} ({contentType}, {size} bytes)";
                });
                result += $"\n\nAttachments ({attachments.GetArrayLength()}):\n{string.Join("\n", attLines)}";
            }
            return result;
        }
        catch (HttpRequestException)
        {
            return ConnectionErrorMessage;
        }
    }

    [McpServerTool, Description("Verify that an email attachment matches the original data by comparing Base64-encoded content.")]
    public async Task<string> VerifyAttachment(
        [Description("Email ID")] string emailId,
        [Description("Attachment index (0-based)")] int attachmentIndex,
        [Description("Base64-encoded string of the original data")] string originalBase64)
    {
        try
        {
            var client = httpClientFactory.CreateClient("MailDev");
            var response = await client.GetAsync($"/email/{emailId}");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var e = doc.RootElement;
            if (!e.TryGetProperty("attachments", out var attachments)
                || attachmentIndex >= attachments.GetArrayLength())
            {
                return $"Attachment (index: {attachmentIndex}) not found.";
            }
            var attachment = attachments[attachmentIndex];
            var fileName = attachment.GetProperty("fileName").GetString();
            var (contentBase64, fetchError) = await FetchAttachmentBase64Async(client, attachment, emailId);
            if (fetchError != null) return fetchError;
            var originalBytes = Convert.FromBase64String(originalBase64);
            var receivedBytes = Convert.FromBase64String(contentBase64!);
            if (originalBytes.Length != receivedBytes.Length)
            {
                return $"❌ File corrupted: size mismatch\n"
                     + $"  Original: {originalBytes.Length} bytes\n"
                     + $"  Received: {receivedBytes.Length} bytes\n"
                     + $"  File name: {fileName}";
            }
            if (!originalBytes.AsSpan().SequenceEqual(receivedBytes))
            {
                var diffCount = originalBytes.Zip(receivedBytes, (a, b) => a != b)
                    .Count(d => d);
                return $"❌ File corrupted: content mismatch\n"
                     + $"  Size: {originalBytes.Length} bytes (match)\n"
                     + $"  Different bytes: {diffCount}\n"
                     + $"  File name: {fileName}";
            }
            return $"✅ File intact: exact match with original\n"
                 + $"  Size: {originalBytes.Length} bytes\n"
                 + $"  File name: {fileName}";
        }
        catch (FormatException)
        {
            return "❌ Failed to decode Base64 string. Please check the input value.";
        }
        catch (HttpRequestException)
        {
            return ConnectionErrorMessage;
        }
    }

    [McpServerTool, Description("Delete a single email by ID.")]
    public async Task<string> DeleteEmail(
        [Description("Email ID to delete (obtained from list_emails)")] string id)
    {
        try
        {
            var client = httpClientFactory.CreateClient("MailDev");
            var response = await client.DeleteAsync($"/email/{id}");
            return response.IsSuccessStatusCode
                ? $"Email '{id}' deleted."
                : $"Failed to delete email '{id}' (HTTP {(int)response.StatusCode}).";
        }
        catch (HttpRequestException)
        {
            return ConnectionErrorMessage;
        }
    }

    [McpServerTool, Description("Delete all received emails in MailDev.")]
    public async Task<string> DeleteAllEmails()
    {
        try
        {
            var client = httpClientFactory.CreateClient("MailDev");
            var response = await client.DeleteAsync("/email/all");
            return response.IsSuccessStatusCode
                ? "All emails deleted."
                : $"Failed to delete emails (HTTP {(int)response.StatusCode}).";
        }
        catch (HttpRequestException)
        {
            return ConnectionErrorMessage;
        }
    }

    [McpServerTool, Description("Wait until a new email matching the given criteria arrives, or until the timeout is reached. Useful for test automation after triggering an email send action.")]
    public async Task<string> WaitForEmail(
        [Description("Maximum number of seconds to wait (default: 30)")] int timeoutSeconds = 30,
        [Description("Filter by subject (case-insensitive partial match, optional)")] string? subject = null,
        [Description("Filter by sender address (case-insensitive partial match, optional)")] string? from = null,
        [Description("Filter by recipient address (case-insensitive partial match, optional)")] string? to = null)
    {
        var client = httpClientFactory.CreateClient("MailDev");
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        const int pollIntervalMs = 1000;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await client.GetAsync("/email");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var match = doc.RootElement.EnumerateArray().FirstOrDefault(e => MatchesFilter(e, subject, from, to));
                    if (match.ValueKind != JsonValueKind.Undefined)
                    {
                        var id = match.GetProperty("id").GetString();
                        var subj = match.GetProperty("subject").GetString() ?? "(no subject)";
                        var fromAddr = FormatAddresses(match, "from");
                        return $"Email arrived.\n- ID: {id}\n- Subject: {subj}\n- From: {fromAddr}";
                    }
                }
            }
            catch (HttpRequestException) { }
            await Task.Delay(pollIntervalMs);
        }
        return $"Timed out after {timeoutSeconds}s waiting for email"
            + (subject != null ? $" with subject '{subject}'" : "")
            + (from != null ? $" from '{from}'" : "")
            + (to != null ? $" to '{to}'" : "")
            + ".";
    }

    [McpServerTool, Description("Get the HTML body of an email by ID.")]
    public async Task<string> GetEmailHtml(
        [Description("Email ID (obtained from list_emails)")] string id)
    {
        try
        {
            var client = httpClientFactory.CreateClient("MailDev");
            var response = await client.GetAsync($"/email/{id}");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var e = doc.RootElement;
            if (e.TryGetProperty("html", out var html) && html.ValueKind == JsonValueKind.String)
            {
                return html.GetString() ?? "(empty HTML body)";
            }
            return "(This email has no HTML body. Use GetEmail to retrieve the plain-text body.)";
        }
        catch (HttpRequestException)
        {
            return ConnectionErrorMessage;
        }
    }

    [McpServerTool, Description("Search received emails by subject, sender, or recipient. Returns matching emails.")]
    public async Task<string> SearchEmails(
        [Description("Filter by subject (case-insensitive partial match, optional)")] string? subject = null,
        [Description("Filter by sender address (case-insensitive partial match, optional)")] string? from = null,
        [Description("Filter by recipient address (case-insensitive partial match, optional)")] string? to = null)
    {
        try
        {
            var client = httpClientFactory.CreateClient("MailDev");
            var response = await client.GetAsync("/email");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var matches = doc.RootElement.EnumerateArray()
                .Where(e => MatchesFilter(e, subject, from, to))
                .ToList();
            if (matches.Count == 0)
            {
                return "No emails matched the given criteria.";
            }
            var lines = matches.Select((e, i) => FormatEmailSummary(e, i));
            return $"Found {matches.Count} email(s):\n\n{string.Join("\n\n", lines)}";
        }
        catch (HttpRequestException)
        {
            return ConnectionErrorMessage;
        }
    }

    [McpServerTool, Description("Get the raw Base64-encoded content of an email attachment.")]
    public async Task<string> GetAttachmentContent(
        [Description("Email ID (obtained from list_emails)")] string emailId,
        [Description("Attachment index (0-based)")] int attachmentIndex)
    {
        try
        {
            var client = httpClientFactory.CreateClient("MailDev");
            var response = await client.GetAsync($"/email/{emailId}");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var e = doc.RootElement;
            if (!e.TryGetProperty("attachments", out var attachments)
                || attachmentIndex >= attachments.GetArrayLength())
            {
                return $"Attachment (index: {attachmentIndex}) not found.";
            }
            var attachment = attachments[attachmentIndex];
            var fileName = attachment.GetProperty("fileName").GetString();
            var contentType = attachment.TryGetProperty("contentType", out var ct) ? ct.GetString() : "unknown";
            var size = attachment.TryGetProperty("length", out var len) ? len.GetInt64() : 0;
            var (contentBase64, fetchError) = await FetchAttachmentBase64Async(client, attachment, emailId);
            if (fetchError != null) return fetchError;
            return $"File name: {fileName}\nContent-Type: {contentType}\nSize: {size} bytes\nBase64:\n{contentBase64}";
        }
        catch (HttpRequestException)
        {
            return ConnectionErrorMessage;
        }
    }

    private static string FormatEmailSummary(JsonElement email, int index)
    {
        var id = email.GetProperty("id").GetString();
        var from = FormatAddresses(email, "from");
        var to = FormatAddresses(email, "to");
        var subject = email.GetProperty("subject").GetString() ?? "(no subject)";
        var attachmentCount = email.TryGetProperty("attachments", out var att) ? att.GetArrayLength() : 0;
        return $"[{index}] ID: {id}\n    From: {from}\n    To: {to}\n    Subject: {subject}\n    Attachments: {attachmentCount}";
    }

    private static async Task<(string? Base64, string? Error)> FetchAttachmentBase64Async(
        HttpClient client, JsonElement attachment, string emailId)
    {
        var fileName = attachment.GetProperty("fileName").GetString();
        if (attachment.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
        {
            return (c.GetString()!, null);
        }
        var response = await client.GetAsync($"/email/{emailId}/attachment/{fileName}");
        if (!response.IsSuccessStatusCode)
        {
            return (null, $"Failed to retrieve attachment '{fileName}'.");
        }
        var bytes = await response.Content.ReadAsByteArrayAsync();
        return (Convert.ToBase64String(bytes), null);
    }

    private static bool MatchesFilter(JsonElement email, string? subject, string? from, string? to)
    {
        if (subject != null)
        {
            var s = email.TryGetProperty("subject", out var sv) ? sv.GetString() ?? "" : "";
            if (!s.Contains(subject, StringComparison.OrdinalIgnoreCase)) return false;
        }
        if (from != null)
        {
            var fromStr = FormatAddresses(email, "from");
            if (!fromStr.Contains(from, StringComparison.OrdinalIgnoreCase)) return false;
        }
        if (to != null)
        {
            var toStr = FormatAddresses(email, "to");
            if (!toStr.Contains(to, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private static string FormatAddresses(JsonElement email, string field)
    {
        if (!email.TryGetProperty(field, out var addresses))
        {
            return "(none)";
        }
        return string.Join(", ", addresses.EnumerateArray().Select(a =>
        {
            var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
            var address = a.GetProperty("address").GetString();
            return string.IsNullOrEmpty(name) ? address! : $"{name} <{address}>";
        }));
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunDockerAsync(
        string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, output, error);
    }
}
