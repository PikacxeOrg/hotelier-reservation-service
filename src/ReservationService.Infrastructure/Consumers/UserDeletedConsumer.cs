using Hotelier.Events;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using ReservationService.Domain;

namespace ReservationService.Infrastructure;

/// <summary>
/// When a user deletes their account, cancel all their pending/approved reservations.
/// For guests: cancel their reservations.
/// For hosts: cancel all reservations on their accommodations.
/// </summary>
public class UserDeletedConsumer(
    ReservationDbContext db,
    ILogger<UserDeletedConsumer> logger)
    : IConsumer<UserDeleted>
{
    public async Task Consume(ConsumeContext<UserDeleted> context)
    {
        var msg = context.Message;
        logger.LogInformation("User {UserId} ({UserType}) deleted – cleaning up reservations", msg.UserId, msg.UserType);

        List<Reservation> affected;

        if (msg.UserType == "Host")
        {
            affected = await db.Reservations
                .Where(r => r.HostId == msg.UserId
                            && (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Approved))
                .ToListAsync();
        }
        else
        {
            affected = await db.Reservations
                .Where(r => r.UserId == msg.UserId
                            && (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Approved))
                .ToListAsync();
        }

        foreach (var reservation in affected)
        {
            reservation.Status = ReservationStatus.Cancelled;
            reservation.ModifiedBy = "system:user-deleted";
        }

        if (affected.Count > 0)
        {
            await db.SaveChangesAsync();
            logger.LogInformation("Cancelled {Count} reservations for deleted user {UserId}", affected.Count, msg.UserId);
        }
    }
}
