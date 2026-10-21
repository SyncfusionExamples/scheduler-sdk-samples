using System.Net.Mail;
using System.Text;
using RangeReservationPoc.Models;

namespace RangeReservationPoc.Services;

/// <summary>
/// UC-12 confirmation / denial notices. On-prem friendly:
///  - "Email:SmtpHost" configured  -> sent through the organization's internal SMTP relay;
///  - otherwise                    -> written as .eml files to a local pickup folder (App_Data/mail-pickup).
/// Every notice is also kept in the in-memory log shown on the dashboard and in the admin queue.
/// </summary>
public class NotificationService
{
    private readonly InMemoryDataStore _store;
    private readonly IConfiguration _config;
    private readonly ILogger<NotificationService> _log;
    private readonly string _pickupDir;

    public NotificationService(InMemoryDataStore store, IConfiguration config, IWebHostEnvironment env, ILogger<NotificationService> log)
    {
        _store = store;
        _config = config;
        _log = log;
        _pickupDir = Path.Combine(env.ContentRootPath, config["Email:PickupDirectory"] ?? "App_Data/mail-pickup");
    }

    public void SendDecision(IReadOnlyList<Reservation> items, bool approved)
    {
        if (items.Count == 0) return;
        var first = items[0];
        var group = _store.FindGroup(first.GroupId);
        var submitter = _store.FindUser(first.SubmittedById);
        var range = _store.FindRange(first.RangeId);
        var recipients = new[] { submitter?.Email, group?.ContactEmail }.Where(e => !string.IsNullOrWhiteSpace(e)).Distinct().ToList();

        var subject = approved
            ? $"Reservation confirmed - {first.RequestId} - {range?.Name}"
            : $"Reservation request denied - {first.RequestId} - {range?.Name}";

        var body = new StringBuilder();
        body.AppendLine($"Hello {submitter?.Name ?? group?.Name},");
        body.AppendLine();
        body.AppendLine(approved
            ? $"Your reservation request {first.RequestId} for {range?.Name} has been APPROVED."
            : $"Your reservation request {first.RequestId} for {range?.Name} has been DENIED.");
        if (!approved) body.AppendLine($"Reason: {first.DenialReason}");
        body.AppendLine();
        body.AppendLine($"Group: {group?.Name}");
        body.AppendLine($"Title: {first.Subject}");
        foreach (var r in items.Where(x => x.RecurrenceID is null))
        {
            body.Append($" - {r.StartTime:dddd, MMM d, yyyy h:mm tt} - {r.EndTime:h:mm tt}");
            if (!string.IsNullOrEmpty(r.RecurrenceRule)) body.Append($" (recurring: {r.RecurrenceRule})");
            body.AppendLine();
        }
        body.AppendLine($"Headcount: {first.Headcount} · Firearms: {string.Join(", ", first.FirearmTypes)}");
        body.AppendLine();
        body.AppendLine("Reviewed by: " + (first.ReviewedBy ?? "Range Administration"));

        var record = new NotificationRecord
        {
            Id = _store.NextNotificationId(), SentAt = DateTime.Now, GroupId = first.GroupId, RequestId = first.RequestId,
            To = string.Join("; ", recipients), Subject = subject, Body = body.ToString()
        };
        record.DeliveredVia = Deliver(recipients, subject, record.Body);
        lock (_store.Sync) _store.Notifications.Insert(0, record);
    }

    private string Deliver(List<string?> recipients, string subject, string body)
    {
        try
        {
            var from = _config["Email:From"] ?? "reservations@range.example";
            using var msg = new MailMessage { From = new MailAddress(from), Subject = subject, Body = body };
            foreach (var r in recipients) msg.To.Add(r!);
            if (msg.To.Count == 0) return "not sent (no recipient)";

            var host = _config["Email:SmtpHost"];
            using var client = new SmtpClient();
            if (!string.IsNullOrWhiteSpace(host))
            {
                client.Host = host;
                client.Port = int.TryParse(_config["Email:SmtpPort"], out var p) ? p : 25;
                client.EnableSsl = bool.TryParse(_config["Email:EnableSsl"], out var ssl) && ssl;
                client.Send(msg);
                return $"SMTP ({host})";
            }
            Directory.CreateDirectory(_pickupDir);
            client.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
            client.PickupDirectoryLocation = _pickupDir;
            client.Send(msg);
            return "pickup folder (App_Data/mail-pickup)";
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Notification delivery failed");
            return "delivery failed: " + ex.Message;
        }
    }
}
