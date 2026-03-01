using System.ComponentModel.DataAnnotations;

namespace ReservationService.Domain;

public enum ReservationStatus
{
    Pending,
    Approved,
    Denied,
    Cancelled
}

public class Reservation : TrackableEntity
{
    /// <summary>
    /// The guest who made the reservation.
    /// </summary>
    [Required]
    public Guid UserId { get; set; }

    [Required]
    public Guid AccommodationId { get; set; }

    /// <summary>
    /// The host who owns the accommodation.
    /// </summary>
    [Required]
    public Guid HostId { get; set; }

    [Required]
    public DateOnly FromDate { get; set; }

    [Required]
    public DateOnly ToDate { get; set; }

    [Required]
    [Range(1, 100)]
    public int NumOfGuests { get; set; }

    [Required]
    public ReservationStatus Status { get; set; } = ReservationStatus.Pending;
}
