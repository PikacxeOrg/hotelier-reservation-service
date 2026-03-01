namespace Hotelier.Events;

/// <summary>
/// Published when a reservation request is rejected by the host.
/// Consumed by notification-service (notify guest).
/// </summary>
public record ReservationRejected
{
    public Guid ReservationId { get; init; }
    public Guid GuestId { get; init; }
    public Guid HostId { get; init; }
    public Guid AccommodationId { get; init; }
    public DateTime FromDate { get; init; }
    public DateTime ToDate { get; init; }
    public string? Reason { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
