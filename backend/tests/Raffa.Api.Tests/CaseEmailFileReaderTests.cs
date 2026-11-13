using System.Text;
using MimeKit;
using Raffa.Api.Infrastructure;

namespace Raffa.Api.Tests;

/// <summary>Renewal plan F1: a supplier email uploaded as a file is read as the reply (header + new
/// text, quoted history cut) with its attachments.</summary>
public sealed class CaseEmailFileReaderTests
{
    [Fact]
    public void An_eml_reply_gives_sender_subject_the_new_text_and_the_attachments()
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Anna Rossi", "anna@splunk.example"));
        message.To.Add(new MailboxAddress("Luca", "luca@acme.example"));
        message.Subject = "Re: Splunk MSA — renewal proposal";
        message.Date = new DateTimeOffset(2026, 11, 20, 9, 30, 0, TimeSpan.Zero);
        var body = new BodyBuilder
        {
            TextBody = "Dear Luca,\n\nWe can offer USD 255,000 for 24 months.\n\nBest,\nAnna\n\nOn Fri, 13 Nov 2026 at 10:00, Luca <luca@acme.example> wrote:\n> For the Splunk MSA we can offer a longer commitment\n> 24 months at USD 252700",
        };
        body.Attachments.Add("Splunk_OrderForm_draft.pdf", Encoding.UTF8.GetBytes("%PDF-1.7 draft"), new ContentType("application", "pdf"));
        message.Body = body.ToMessageBody();
        using var stream = new MemoryStream();
        message.WriteTo(stream);

        var email = CaseEmailFileReader.Read("reply.eml", stream.ToArray(), 20 * 1024 * 1024);

        Assert.NotNull(email);
        Assert.Contains("From: Anna Rossi <anna@splunk.example>", email!.Text);
        Assert.Contains("Subject: Re: Splunk MSA — renewal proposal", email.Text);
        Assert.Contains("USD 255,000 for 24 months", email.Text);
        Assert.DoesNotContain("252700", email.Text);
        Assert.Contains("Attachments: Splunk_OrderForm_draft.pdf", email.Text);
        var attachment = Assert.Single(email.Attachments);
        Assert.Equal("application/pdf", attachment.MimeType);
        Assert.Equal("%PDF-1.7 draft", Encoding.UTF8.GetString(attachment.Bytes));
        Assert.Equal("reply.eml", email.Source.FileName);
    }

    [Theory]
    [InlineData("Proposta allegata.\n\nIl giorno ven 13 nov 2026, Luca ha scritto:\nvecchio testo", "Proposta allegata.")]
    [InlineData("OK for us.\n\n-----Original Message-----\nFrom: Luca\nold", "OK for us.")]
    [InlineData("Confermiamo.\n________________________________\nDa: Luca <luca@acme.example>\nInviato: ...", "Confermiamo.")]
    [InlineData("Da: Anna <anna@x.example> è il nostro contatto.", "Da: Anna <anna@x.example> è il nostro contatto.")]
    public void Only_the_new_part_of_a_reply_is_kept(string body, string expected) =>
        Assert.Equal(expected, CaseEmailFileReader.NewPart(body));

    [Fact]
    public void A_file_that_is_not_an_email_is_not_read()
    {
        Assert.Null(CaseEmailFileReader.Read("reply.msg", Encoding.UTF8.GetBytes("not an outlook file"), 1024));
        Assert.False(CaseEmailFileReader.IsEmailFile("contract.pdf"));
        Assert.True(CaseEmailFileReader.IsEmailFile("Reply.MSG"));
    }
}
