namespace RangeReservationPoc.Models;

/// <summary>Event shape sent to the Syncfusion Scheduler (reservations + blackout blocks).</summary>
public class ScheduleEventDto
{
    public int Id { get; set; }
    public string Subject { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool IsAllDay { get; set; }
    public string? RecurrenceRule { get; set; }
    public int? RecurrenceID { get; set; }
    public string? RecurrenceException { get; set; }
    public int FacilityId { get; set; }
    public int RangeId { get; set; }

    /// <summary>Syncfusion block event - hard-blocks the time range in the UI.</summary>
    public bool IsBlock { get; set; }
    /// <summary>Syncfusion read-only event - cannot be opened / dragged / resized.</summary>
    public bool IsReadonly { get; set; }

    /// <summary>"reservation", "masked" (someone else's booking shown to a customer) or "blackout".</summary>
    public string Kind { get; set; } = "reservation";
    public bool IsOwn { get; set; }
    public string? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public CustomerTier? Tier { get; set; }
    public string? FirearmType { get; set; }
    public string? Caliber { get; set; }
    public int? Participants { get; set; }
    public string? QualificationId { get; set; }
    public bool RsoRequested { get; set; }
    public string? Description { get; set; }
    public string? BookingGroupId { get; set; }
    public bool AdminOverride { get; set; }
}

/// <summary>Reservation payload coming from the Scheduler (editor, drag/resize, multi-slot cart).</summary>
public class ReservationInput
{
    public int? Id { get; set; }
    public string? Subject { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool IsAllDay { get; set; }
    public string? RecurrenceRule { get; set; }
    public int? RecurrenceID { get; set; }
    public string? RecurrenceException { get; set; }
    public int? FacilityId { get; set; }
    public int RangeId { get; set; }
    public string? CustomerId { get; set; }
    public string? FirearmType { get; set; }
    public string? Caliber { get; set; }
    public int? Participants { get; set; }
    public string? QualificationId { get; set; }
    public bool RsoRequested { get; set; }
    public string? Description { get; set; }
}

/// <summary>One transaction: everything in it is validated together and committed all-or-nothing.</summary>
public class ReservationBatchRequest
{
    public List<ReservationInput> Added { get; set; } = new();
    public List<ReservationInput> Changed { get; set; } = new();
    public List<ReservationInput> Deleted { get; set; } = new();
    /// <summary>Admin only: bypass tier policies (conflicts, blackouts and range safety rules are still enforced).</summary>
    public bool AdminOverride { get; set; }
    /// <summary>Validate only - nothing is saved (used by "Check availability").</summary>
    public bool DryRun { get; set; }
}

public class BatchResult
{
    public bool Success => Errors.Count == 0;
    public bool DryRun { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<int> SavedIds { get; set; } = new();
    public string? BookingGroupId { get; set; }
    public int OccurrencesValidated { get; set; }
}

public class BlackoutInput
{
    public int? Id { get; set; }
    public string Title { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool IsAllDay { get; set; }
    public string? RecurrenceRule { get; set; }
    public int? FacilityId { get; set; }
    public int? RangeId { get; set; }
    public string? Reason { get; set; }
}
