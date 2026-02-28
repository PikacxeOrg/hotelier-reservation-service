using Hotelier.Events;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using ReservationService.Domain;

namespace ReservationService.Infrastructure;

/// <summary>
/// When an accommodation is deleted, cancel all its pending/approved reservations.
/// </summary>
public class AccommodationDeletedConsumer(
    ReservationDbContext db,
    ILogger<AccommodationDeletedConsumer> logger)
    : IConsumer<AccommodationDeleted>
{
    public async Task Consume(ConsumeContext<AccommodationDeleted> context)
    {
        var msg = context.Message;
        logger.LogInformation(
            "Accommodation {AccommodationId} deleted – cancelling active reservations",
            msg.AccommodationId);

        var affected = await db.Reservations
            .Where(r => r.AccommodationId == msg.AccommodationId
                        && (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Approved))
            .ToListAsync();

        foreach (var reservation in affected)
        {
            reservation.Status = ReservationStatus.Cancelled;
            reservation.ModifiedBy = "system:accommodation-deleted";
        }

        if (affected.Count > 0)
        {
            await db.SaveChangesAsync();
            logger.LogInformation("Cancelled {Count} reservations for deleted accommodation {Id}",
                affected.Count, msg.AccommodationId);
        }
    }
}
