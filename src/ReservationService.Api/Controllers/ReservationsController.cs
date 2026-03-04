using System.Security.Claims;

using Hotelier.Events;

using MassTransit;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using ReservationService.Domain;
using ReservationService.Infrastructure;

namespace ReservationService.Api;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ReservationsController(
    ReservationDbContext db,
    IPublishEndpoint publisher,
    IAccommodationServiceClient accommodationClient,
    IAvailabilityServiceClient availabilityClient,
    ILogger<ReservationsController> logger) : ControllerBase
{
    // -------------------------------------------------------
    // POST /api/reservations   (1.8 – create reservation request)
    // -------------------------------------------------------
    [Authorize(Roles = "Guest")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateReservationRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var guestId = GetUserId();
        if (guestId is null) return Unauthorized();

        if (request.FromDate >= request.ToDate)
            return BadRequest(new { message = "FromDate must be before ToDate." });

        if (request.FromDate < DateOnly.FromDateTime(DateTime.UtcNow))
            return BadRequest(new { message = "Cannot create reservations in the past." });

        // Fetch accommodation details (HostId, AutoApproval, guest limits)
        var accommodation = await accommodationClient.GetAccommodationAsync(request.AccommodationId);
        if (accommodation is null)
            return BadRequest(new { message = "Accommodation not found." });

        if (request.NumOfGuests < accommodation.MinGuests || request.NumOfGuests > accommodation.MaxGuests)
            return BadRequest(new
            {
                message = $"Number of guests must be between {accommodation.MinGuests} and {accommodation.MaxGuests}."
            });

        // Check availability (window coverage + pricing)
        var availability = await availabilityClient.CheckAvailabilityAsync(
            request.AccommodationId, request.FromDate, request.ToDate);

        if (!availability.IsAvailable)
            return Conflict(new { message = "Accommodation is not available for the selected dates." });

        // Synchronous overlap guard: reject if an Approved reservation already
        // covers any part of the requested period.  This is necessary because
        // the availability-service marks windows unavailable via an async event
        // (ReservationApproved → RabbitMQ → consumer), so there is a race window
        // during which the availability check above can return IsAvailable = true
        // even though a reservation was just approved.
        var hasApprovedOverlap = await db.Reservations.AnyAsync(r =>
            r.AccommodationId == request.AccommodationId
            && r.Status == ReservationStatus.Approved
            && r.FromDate < request.ToDate
            && r.ToDate > request.FromDate);

        if (hasApprovedOverlap)
            return Conflict(new { message = "These dates are already reserved by another guest." });

        var reservation = new Reservation
        {
            UserId = guestId.Value,
            AccommodationId = request.AccommodationId,
            HostId = accommodation.HostId,
            FromDate = request.FromDate,
            ToDate = request.ToDate,
            NumOfGuests = request.NumOfGuests,
            Status = ReservationStatus.Pending,
            CreatedBy = guestId.Value.ToString()
        };

        // Spec 1.10: auto-approval mode
        if (accommodation.AutoApproval)
        {
            reservation.Status = ReservationStatus.Approved;
            reservation.ModifiedBy = "system:auto-approved";
        }

        db.Reservations.Add(reservation);
        await db.SaveChangesAsync();

        if (reservation.Status == ReservationStatus.Approved)
        {
            await publisher.Publish(new ReservationApproved
            {
                ReservationId = reservation.Id,
                GuestId = reservation.UserId,
                HostId = reservation.HostId,
                AccommodationId = reservation.AccommodationId,
                FromDate = reservation.FromDate,
                ToDate = reservation.ToDate
            });

            // Auto-reject overlapping pending requests
            await RejectOverlapping(reservation);
        }

        await publisher.Publish(new ReservationCreated
        {
            ReservationId = reservation.Id,
            GuestId = reservation.UserId,
            HostId = reservation.HostId,
            AccommodationId = reservation.AccommodationId,
            FromDate = reservation.FromDate,
            ToDate = reservation.ToDate,
            NumOfGuests = reservation.NumOfGuests
        });

        logger.LogInformation(
            "Reservation {Id} created (status={Status}) for accommodation {AccommodationId}",
            reservation.Id, reservation.Status, reservation.AccommodationId);

        return CreatedAtAction(nameof(GetById), new { id = reservation.Id }, MapResponse(reservation));
    }

    // -------------------------------------------------------
    // GET /api/reservations/{id}
    // -------------------------------------------------------
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var guestId = GetUserId();
        if (guestId is null) return Unauthorized();

        var reservation = await db.Reservations.FindAsync(id);
        if (reservation is null) return NotFound();

        // Only the guest or host of the reservation can view it
        if (reservation.UserId != guestId && reservation.HostId != guestId)
            return Forbid();

        return Ok(MapResponse(reservation));
    }

    // -------------------------------------------------------
    // GET /api/reservations/mine   (guest's reservations)
    // -------------------------------------------------------
    [Authorize]
    [HttpGet("mine")]
    public async Task<IActionResult> GetMyReservations([FromQuery] ReservationStatus? status = null)
    {
        var guestId = GetUserId();
        if (guestId is null) return Unauthorized();

        var query = db.Reservations
            .Where(r => r.UserId == guestId.Value)
            .OrderByDescending(r => r.CreatedTimestamp)
            .AsQueryable();

        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value);

        var results = await query.ToListAsync();
        return Ok(results.Select(MapResponse));
    }

    // -------------------------------------------------------
    // GET /api/reservations/host   (host's incoming reservations)
    // -------------------------------------------------------
    [Authorize(Roles = "Host")]
    [HttpGet("host")]
    public async Task<IActionResult> GetHostReservations([FromQuery] ReservationStatus? status = null)
    {
        var hostId = GetUserId();
        if (hostId is null) return Unauthorized();

        var query = db.Reservations
            .Where(r => r.HostId == hostId.Value)
            .OrderByDescending(r => r.CreatedTimestamp)
            .AsQueryable();

        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value);

        var results = await query.ToListAsync();
        return Ok(results.Select(MapResponse));
    }

    // -------------------------------------------------------
    // DELETE /api/reservations/{id}   (1.8 – guest deletes pending request)
    // -------------------------------------------------------
    [Authorize(Roles = "Guest")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var guestId = GetUserId();
        if (guestId is null) return Unauthorized();

        var reservation = await db.Reservations.FindAsync(id);
        if (reservation is null) return NotFound();

        if (reservation.UserId != guestId)
            return Forbid();

        if (reservation.Status != ReservationStatus.Pending)
            return Conflict(new { message = "Only pending reservations can be deleted." });

        db.Reservations.Remove(reservation);
        await db.SaveChangesAsync();

        logger.LogInformation("Reservation {Id} deleted by guest {GuestId}", id, guestId);

        return NoContent();
    }

    // -------------------------------------------------------
    // PUT /api/reservations/{id}/cancel   (1.9 – cancel approved reservation)
    // -------------------------------------------------------
    [Authorize(Roles = "Guest")]
    [HttpPut("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var guestId = GetUserId();
        if (guestId is null) return Unauthorized();

        var reservation = await db.Reservations.FindAsync(id);
        if (reservation is null) return NotFound();

        if (reservation.UserId != guestId)
            return Forbid();

        if (reservation.Status != ReservationStatus.Approved)
            return Conflict(new { message = "Only approved reservations can be cancelled." });

        // Spec 1.9: at least 1 day before start
        if (reservation.FromDate <= DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1))
            return Conflict(new { message = "Cancellation must be at least 1 day before the start date." });

        reservation.Status = ReservationStatus.Cancelled;
        reservation.ModifiedBy = guestId.Value.ToString();
        await db.SaveChangesAsync();

        await publisher.Publish(new ReservationCancelled
        {
            ReservationId = reservation.Id,
            GuestId = reservation.UserId,
            HostId = reservation.HostId,
            AccommodationId = reservation.AccommodationId,
            FromDate = reservation.FromDate,
            ToDate = reservation.ToDate
        });

        logger.LogInformation("Reservation {Id} cancelled by guest {GuestId}", id, guestId);

        return Ok(MapResponse(reservation));
    }

    // -------------------------------------------------------
    // PUT /api/reservations/{id}/approve   (1.10 – host approves)
    // -------------------------------------------------------
    [Authorize(Roles = "Host")]
    [HttpPut("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id)
    {
        var hostId = GetUserId();
        if (hostId is null) return Unauthorized();

        var reservation = await db.Reservations.FindAsync(id);
        if (reservation is null) return NotFound();

        if (reservation.HostId != hostId)
            return Forbid();

        if (reservation.Status != ReservationStatus.Pending)
            return Conflict(new { message = "Only pending reservations can be approved." });

        reservation.Status = ReservationStatus.Approved;
        reservation.ModifiedBy = hostId.Value.ToString();
        await db.SaveChangesAsync();

        await publisher.Publish(new ReservationApproved
        {
            ReservationId = reservation.Id,
            GuestId = reservation.UserId,
            HostId = reservation.HostId,
            AccommodationId = reservation.AccommodationId,
            FromDate = reservation.FromDate,
            ToDate = reservation.ToDate
        });

        // Spec 1.10: auto-reject overlapping pending requests
        await RejectOverlapping(reservation);

        logger.LogInformation("Reservation {Id} approved by host {HostId}", id, hostId);

        return Ok(MapResponse(reservation));
    }

    // -------------------------------------------------------
    // PUT /api/reservations/{id}/reject   (1.10 – host rejects)
    // -------------------------------------------------------
    [Authorize(Roles = "Host")]
    [HttpPut("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectReservationRequest? request = null)
    {
        var hostId = GetUserId();
        if (hostId is null) return Unauthorized();

        var reservation = await db.Reservations.FindAsync(id);
        if (reservation is null) return NotFound();

        if (reservation.HostId != hostId)
            return Forbid();

        if (reservation.Status != ReservationStatus.Pending)
            return Conflict(new { message = "Only pending reservations can be rejected." });

        reservation.Status = ReservationStatus.Denied;
        reservation.ModifiedBy = hostId.Value.ToString();
        await db.SaveChangesAsync();

        await publisher.Publish(new ReservationRejected
        {
            ReservationId = reservation.Id,
            GuestId = reservation.UserId,
            HostId = reservation.HostId,
            AccommodationId = reservation.AccommodationId,
            FromDate = reservation.FromDate,
            ToDate = reservation.ToDate,
            Reason = request?.Reason
        });

        logger.LogInformation("Reservation {Id} rejected by host {HostId}", id, hostId);

        return Ok(MapResponse(reservation));
    }

    // -------------------------------------------------------
    // GET /api/reservations/guest-history/{guestId}   (1.10 – cancellation history)
    // For hosts to view a guest's cancellation count.
    // -------------------------------------------------------
    [Authorize(Roles = "Host")]
    [HttpGet("guest-history/{guestId:guid}")]
    public async Task<IActionResult> GetGuestHistory(Guid guestId)
    {
        var cancelledCount = await db.Reservations.CountAsync(r =>
            r.UserId == guestId && r.Status == ReservationStatus.Cancelled);

        var totalCount = await db.Reservations.CountAsync(r => r.UserId == guestId);

        return Ok(new
        {
            guestId,
            totalReservations = totalCount,
            cancelledReservations = cancelledCount
        });
    }

    // -------------------------------------------------------
    // GET /api/reservations/internal/completed
    // Service-to-service: check if a guest completed a stay
    // at a host's accommodation. Used by rating-service.
    // -------------------------------------------------------
    [AllowAnonymous]
    [HttpGet("internal/completed")]
    public async Task<IActionResult> HasCompletedStay(
        [FromQuery] Guid guestId,
        [FromQuery] Guid targetId,
        [FromQuery] string targetType)
    {
        bool hasCompleted;

        if (string.Equals(targetType, "Host", StringComparison.OrdinalIgnoreCase))
        {
            // Guest had a completed (past, approved) reservation at any of this host's accommodations
            hasCompleted = await db.Reservations.AnyAsync(r =>
                r.UserId == guestId
                && r.HostId == targetId
                && r.Status == ReservationStatus.Approved
                && r.ToDate < DateOnly.FromDateTime(DateTime.UtcNow));
        }
        else
        {
            // Guest had a completed reservation at this specific accommodation
            hasCompleted = await db.Reservations.AnyAsync(r =>
                r.UserId == guestId
                && r.AccommodationId == targetId
                && r.Status == ReservationStatus.Approved
                && r.ToDate < DateOnly.FromDateTime(DateTime.UtcNow));
        }

        return Ok(new { hasCompleted });
    }

    // -------------------------------------------------------
    // Private: auto-reject overlapping pending reservations
    // -------------------------------------------------------
    private async Task RejectOverlapping(Reservation approved)
    {
        var overlapping = await db.Reservations
            .Where(r =>
                r.AccommodationId == approved.AccommodationId
                && r.Id != approved.Id
                && r.Status == ReservationStatus.Pending
                && r.FromDate < approved.ToDate
                && r.ToDate > approved.FromDate)
            .ToListAsync();

        foreach (var r in overlapping)
        {
            r.Status = ReservationStatus.Denied;
            r.ModifiedBy = "system:auto-rejected";

            await publisher.Publish(new ReservationRejected
            {
                ReservationId = r.Id,
                GuestId = r.UserId,
                HostId = r.HostId,
                AccommodationId = r.AccommodationId,
                FromDate = r.FromDate,
                ToDate = r.ToDate,
                Reason = "Automatically rejected: overlapping reservation was approved."
            });
        }

        if (overlapping.Count > 0)
        {
            await db.SaveChangesAsync();
            logger.LogInformation(
                "Auto-rejected {Count} overlapping pending reservation(s) for accommodation {AccommodationId}",
                overlapping.Count, approved.AccommodationId);
        }
    }

    // -------------------------------------------------------
    // Helpers
    // -------------------------------------------------------
    private Guid? GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    private static ReservationResponse MapResponse(Reservation r) => new()
    {
        Id = r.Id,
        UserId = r.UserId,
        AccommodationId = r.AccommodationId,
        HostId = r.HostId,
        FromDate = r.FromDate,
        ToDate = r.ToDate,
        NumOfGuests = r.NumOfGuests,
        Status = r.Status,
        CreatedTimestamp = r.CreatedTimestamp
    };
}
