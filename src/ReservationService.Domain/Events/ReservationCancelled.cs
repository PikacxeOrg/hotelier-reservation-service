namespace Hotelier.Events;

/// <summary>
/// Published when a guest cancels an approved reservation.
/// Consumed by availability-service (free dates),
/// notification-service (notify host).
/// </summary>
public record ReservationCancelled
{
    public Guid ReservationId { get; init; }
    public Guid GuestId { get; init; }
    public Guid HostId { get; init; }
    public Guid AccommodationId { get; init; }
    public DateOnly FromDate { get; init; }
    public DateOnly ToDate { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
