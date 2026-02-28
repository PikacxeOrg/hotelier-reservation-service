namespace Hotelier.Events;

/// <summary>
/// Published when a reservation is approved by the host.
/// Consumed by availability-service (mark dates unavailable),
/// notification-service (notify guest),
/// reservation-service (auto-reject overlapping requests).
/// </summary>
public record ReservationApproved
{
    public Guid ReservationId { get; init; }
    public Guid GuestId { get; init; }
    public Guid HostId { get; init; }
    public Guid AccommodationId { get; init; }
    public DateTime FromDate { get; init; }
    public DateTime ToDate { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
