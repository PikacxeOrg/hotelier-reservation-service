using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using ReservationService.Domain;
using ReservationService.Infrastructure;

namespace ReservationService.Api;

/// <summary>
/// Internal endpoints for service-to-service communication.
/// No authorization — protected by network-level isolation.
/// </summary>
[ApiController]
[Route("api/reservations/internal")]
public class ReservationsInternalController(
    ReservationDbContext db,
    ILogger<ReservationsInternalController> logger) : ControllerBase
{
    /// <summary>
    /// Check whether a user can safely delete their account.
    /// Returns { canDelete: true/false, activeCount: int, reason: string? }.
    ///
    /// Guest: blocked if any Approved reservation with ToDate >= today.
    /// Host:  blocked if any Pending/Approved reservation on their
    ///        accommodations with ToDate >= today.
    /// </summary>
    [HttpGet("can-delete/{userId:guid}")]
    public async Task<IActionResult> CanDeleteUser(Guid userId, [FromQuery] string userType)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        int activeCount;

        if (string.Equals(userType, "Host", StringComparison.OrdinalIgnoreCase))
        {
            activeCount = await db.Reservations.CountAsync(r =>
                r.HostId == userId
                && (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Approved)
                && r.ToDate >= today);
        }
        else
        {
            // Guest (or any other type)
            activeCount = await db.Reservations.CountAsync(r =>
                r.UserId == userId
                && r.Status == ReservationStatus.Approved
                && r.ToDate >= today);
        }

        if (activeCount > 0)
        {
            var reason = string.Equals(userType, "Host", StringComparison.OrdinalIgnoreCase)
                ? $"Cannot delete account: you have {activeCount} active or pending reservation(s) on your accommodations."
                : $"Cannot delete account: you have {activeCount} active reservation(s). Cancel or complete them first.";

            logger.LogInformation(
                "User {UserId} ({UserType}) cannot delete — {Count} blocking reservations",
                userId, userType, activeCount);

            return Ok(new CanDeleteResponse { CanDelete = false, ActiveCount = activeCount, Reason = reason });
        }

        return Ok(new CanDeleteResponse { CanDelete = true });
    }

    /// <summary>
    /// Check whether an accommodation has Approved or Pending reservations
    /// overlapping a given date range.
    /// Used by availability-service to enforce "no changes if reservations exist".
    /// </summary>
    [HttpGet("has-reservations")]
    public async Task<IActionResult> HasReservationsInPeriod(
        [FromQuery] Guid accommodationId,
        [FromQuery] DateOnly fromDate,
        [FromQuery] DateOnly toDate)
    {
        var count = await db.Reservations.CountAsync(r =>
            r.AccommodationId == accommodationId
            && (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Approved)
            && r.FromDate < toDate
            && r.ToDate > fromDate);

        if (count > 0)
        {
            logger.LogInformation(
                "Accommodation {AccommodationId} has {Count} reservation(s) in period {From}-{To}",
                accommodationId, count, fromDate, toDate);

            return Ok(new HasReservationsResponse
            {
                HasReservations = true,
                Count = count,
                Reason = $"Cannot modify availability: {count} reservation(s) exist in this period."
            });
        }

        return Ok(new HasReservationsResponse { HasReservations = false });
    }
}

public class CanDeleteResponse
{
    public bool CanDelete { get; set; }
    public int ActiveCount { get; set; }
    public string? Reason { get; set; }
}

public class HasReservationsResponse
{
    public bool HasReservations { get; set; }
    public int Count { get; set; }
    public string? Reason { get; set; }
}
