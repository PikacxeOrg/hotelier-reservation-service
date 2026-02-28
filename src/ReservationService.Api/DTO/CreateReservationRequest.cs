using System.ComponentModel.DataAnnotations;

namespace ReservationService.Api;

public class CreateReservationRequest
{
    [Required]
    public Guid AccommodationId { get; set; }

    [Required]
    public DateTime FromDate { get; set; }

    [Required]
    public DateTime ToDate { get; set; }

    [Required]
    [Range(1, 100)]
    public int NumOfGuests { get; set; }
}
