using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using MimeKit;
using Raffa.Renewals.Application.CaseWork;

namespace Raffa.Api.Infrastructure;

/// <summary>
/// Reads a supplier email uploaded to a renewal case (plan F1): an <c>.eml</c> (MIME, MimeKit) or an
/// Outlook <c>.msg</c> (MsgReader). Returns the text Raffa registers as the supplier's reply — a short
/// header (from, date, subject) and the new part of the body, with the quoted history cut off so our own
/// earlier email is never read as the supplier's proposal — and the attachments it carried (inline
/// images skipped).
/// </summary>
internal static partial class CaseEmailFileReader
{
    public const int MaxAttachments = 20;

    public static bool IsEmailFile(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() is ".eml" or ".msg";

    public static CaseEmailUpload? Read(string fileName, byte[] bytes, long maxAttachmentBytes)
    {
        try
        {
            return Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".eml" => ReadEml(fileName, bytes, maxAttachmentBytes),
                ".msg" => ReadMsg(fileName, bytes, maxAttachmentBytes),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or IOException or ArgumentException or InvalidOperationException
                                   or OpenMcdf.FileFormatException or NotSupportedException)
        {
            return null;
        }
    }

    private static CaseEmailUpload? ReadEml(string fileName, byte[] bytes, long maxAttachmentBytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var message = MimeMessage.Load(stream);
        var from = message.From.Mailboxes.FirstOrDefault() is { } m ? Who(m.Name, m.Address) : null;
        DateTimeOffset? date = message.Date == DateTimeOffset.MinValue ? null : message.Date;
        var body = message.TextBody is { Length: > 0 } t ? t : HtmlToText(message.HtmlBody);
        var attachments = new List<CaseUpload>();
        foreach (var part in message.Attachments.OfType<MimePart>())
        {
            if (attachments.Count >= MaxAttachments)
            {
                break;
            }

            using var content = new MemoryStream();
            part.Content?.DecodeTo(content);
            var data = content.ToArray();
            if (data.Length == 0 || data.Length > maxAttachmentBytes)
            {
                continue;
            }

            attachments.Add(new CaseUpload(part.FileName ?? "attachment", part.ContentType.MimeType, data));
        }

        return Compose(fileName, "message/rfc822", bytes, from, date, message.Subject, body, attachments);
    }

    private static CaseEmailUpload? ReadMsg(string fileName, byte[] bytes, long maxAttachmentBytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var message = new MsgReader.Outlook.Storage.Message(stream);
        var from = message.Sender is { } s ? Who(s.DisplayName, s.Email) : null;
        var body = message.BodyText is { Length: > 0 } t ? t : HtmlToText(message.BodyHtml);
        var attachments = new List<CaseUpload>();
        foreach (var attachment in message.Attachments.OfType<MsgReader.Outlook.Storage.Attachment>())
        {
            if (attachments.Count >= MaxAttachments)
            {
                break;
            }

            if (attachment.IsInline || attachment.Data is not { Length: > 0 } data || data.Length > maxAttachmentBytes)
            {
                continue;
            }

            var name = attachment.FileName is { Length: > 0 } n ? n : "attachment";
            attachments.Add(new CaseUpload(name, MimeTypes.GetMimeType(name), data));
        }

        return Compose(fileName, "application/vnd.ms-outlook", bytes, from, message.SentOn, message.Subject, body, attachments);
    }

    private static CaseEmailUpload? Compose(
        string fileName, string mimeType, byte[] bytes, string? from, DateTimeOffset? date, string? subject, string? body, List<CaseUpload> attachments)
    {
        var text = NewPart(body ?? string.Empty);
        if (text.Length == 0)
        {
            return null;
        }

        var header = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(from))
        {
            header.Append("From: ").AppendLine(from);
        }

        if (date is { } d)
        {
            header.Append("Date: ").AppendLine(d.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(subject))
        {
            header.Append("Subject: ").AppendLine(subject.Trim());
        }

        if (attachments.Count > 0)
        {
            header.Append("Attachments: ").AppendLine(string.Join(", ", attachments.Select(a => a.FileName)));
        }

        var full = header.Length == 0 ? text : header + Environment.NewLine + text;
        return new CaseEmailUpload(new CaseUpload(Path.GetFileName(fileName), mimeType, bytes), full.Trim(), attachments);
    }

    private static string? Who(string? name, string? address) =>
        (name, address) switch
        {
            ({ Length: > 0 } n, { Length: > 0 } a) when !string.Equals(n, a, StringComparison.OrdinalIgnoreCase) => $"{n} <{a}>",
            (_, { Length: > 0 } a) => a,
            ({ Length: > 0 } n, _) => n,
            _ => null,
        };

    /// <summary>
    /// The new part of a reply: lines quoted with <c>&gt;</c> dropped, and everything from the first
    /// "original message" separator on cut (English and Italian Outlook / Gmail / Apple Mail forms).
    /// </summary>
    public static string NewPart(string body)
    {
        var lines = body.Replace("\r\n", "\n").Split('\n');
        var kept = new List<string>();
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (kept.Any(l => l.Trim().Length > 0) && QuoteSeparator().IsMatch(line.Trim()))
            {
                break;
            }

            if (line.TrimStart().StartsWith('>'))
            {
                continue;
            }

            kept.Add(line);
        }

        return string.Join("\n", kept).Trim();
    }

    private static string HtmlToText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var s = HtmlDrop().Replace(html, string.Empty);
        s = HtmlBreak().Replace(s, "\n");
        s = HtmlTag().Replace(s, string.Empty);
        s = WebUtility.HtmlDecode(s);
        return BlankLines().Replace(s, "\n\n").Trim();
    }

    [GeneratedRegex(@"^(-{2,}\s*(Original Message|Messaggio originale|Forwarded message|Messaggio inoltrato)\s*-{2,}|_{8,}|On .{4,200}wrote:?|Il giorno .{4,200}ha scritto:?|(From|Da|Von|De)\s*:\s+.+@.+|Sent from my .+|Inviato da .+)$", RegexOptions.IgnoreCase)]
    private static partial Regex QuoteSeparator();

    [GeneratedRegex(@"<(script|style|head)[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HtmlDrop();

    [GeneratedRegex(@"<\s*(br|/p|/div|/li|/tr|/h[1-6])\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlBreak();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"\n\s*\n\s*\n+")]
    private static partial Regex BlankLines();
}
