namespace ReservationService.Domain;

/// <summary>
/// Published when a reservation request is created.
/// Consumed by notification-service (notify host).
/// </summary>
public record ReservationCreated
{
    public Guid ReservationId { get; init; }
    public Guid UserId { get; init; }
    public Guid AccommodationId { get; init; }
    public Guid HostId { get; init; }
    public DateTime FromDate { get; init; }
    public DateTime ToDate { get; init; }
    public int NumOfGuests { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
