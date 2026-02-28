namespace ReservationService.Domain;

/// <summary>
/// Published when a guest cancels an approved reservation.
/// Consumed by availability-service (free dates),
/// notification-service (notify host).
/// </summary>
public record ReservationCancelled
{
    public Guid ReservationId { get; init; }
    public Guid UserId { get; init; }
    public Guid AccommodationId { get; init; }
    public Guid HostId { get; init; }
    public DateTime FromDate { get; init; }
    public DateTime ToDate { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
