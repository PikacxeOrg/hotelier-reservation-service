namespace ReservationService.Domain;

/// <summary>
/// Published when a reservation is approved by the host.
/// Consumed by availability-service (mark dates unavailable),
/// notification-service (notify guest),
/// and auto-rejects overlapping pending requests.
/// </summary>
public record ReservationApproved
{
    public Guid ReservationId { get; init; }
    public Guid UserId { get; init; }
    public Guid AccommodationId { get; init; }
    public Guid HostId { get; init; }
    public DateTime FromDate { get; init; }
    public DateTime ToDate { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
