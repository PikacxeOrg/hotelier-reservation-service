namespace ReservationService.Domain;

/// <summary>
/// Published when a reservation request is rejected by the host.
/// Consumed by notification-service (notify guest).
/// </summary>
public record ReservationRejected
{
    public Guid ReservationId { get; init; }
    public Guid UserId { get; init; }
    public Guid AccommodationId { get; init; }
    public Guid HostId { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
