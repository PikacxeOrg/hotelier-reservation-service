using FluentAssertions;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using ReservationService.Api;
using ReservationService.Domain;
using ReservationService.Infrastructure;

namespace ReservationService.Tests;

public class ReservationsInternalControllerTests : IDisposable
{
    private readonly ReservationDbContext _db;
    private readonly ReservationsInternalController _sut;

    public ReservationsInternalControllerTests()
    {
        var options = new DbContextOptionsBuilder<ReservationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new ReservationDbContext(options);
        var logger = new Mock<ILogger<ReservationsInternalController>>();

        _sut = new ReservationsInternalController(_db, logger.Object);
    }

    public void Dispose() => _db.Dispose();

    // ============================================================
    // Guest deletion checks
    // ============================================================

    [Fact]
    public async Task CanDelete_Guest_NoReservations_ReturnsTrue()
    {
        var guestId = Guid.NewGuid();

        var result = await _sut.CanDeleteUser(guestId, "Guest");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<CanDeleteResponse>().Subject;
        body.CanDelete.Should().BeTrue();
    }

    [Fact]
    public async Task CanDelete_Guest_WithApprovedFutureReservation_ReturnsFalse()
    {
        var guestId = Guid.NewGuid();
        _db.Reservations.Add(new Reservation
        {
            UserId = guestId,
            AccommodationId = Guid.NewGuid(),
            HostId = Guid.NewGuid(),
            FromDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5),
            ToDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10),
            NumOfGuests = 2,
            Status = ReservationStatus.Approved
        });
        await _db.SaveChangesAsync();

        var result = await _sut.CanDeleteUser(guestId, "Guest");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<CanDeleteResponse>().Subject;
        body.CanDelete.Should().BeFalse();
        body.ActiveCount.Should().Be(1);
        body.Reason.Should().Contain("active or pending");
    }

    [Fact]
    public async Task CanDelete_Guest_WithOngoingReservation_ReturnsFalse()
    {
        var guestId = Guid.NewGuid();
        _db.Reservations.Add(new Reservation
        {
            UserId = guestId,
            AccommodationId = Guid.NewGuid(),
            HostId = Guid.NewGuid(),
            FromDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2),
            ToDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3), // still ongoing
            NumOfGuests = 1,
            Status = ReservationStatus.Approved
        });
        await _db.SaveChangesAsync();

        var result = await _sut.CanDeleteUser(guestId, "Guest");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<CanDeleteResponse>().Subject;
        body.CanDelete.Should().BeFalse();
    }

    [Fact]
    public async Task CanDelete_Guest_WithCompletedReservation_ReturnsTrue()
    {
        var guestId = Guid.NewGuid();
        _db.Reservations.Add(new Reservation
        {
            UserId = guestId,
            AccommodationId = Guid.NewGuid(),
            HostId = Guid.NewGuid(),
            FromDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10),
            ToDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3), // in the past
            NumOfGuests = 1,
            Status = ReservationStatus.Approved
        });
        await _db.SaveChangesAsync();

        var result = await _sut.CanDeleteUser(guestId, "Guest");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<CanDeleteResponse>().Subject;
        body.CanDelete.Should().BeTrue();
    }

    [Fact]
    public async Task CanDelete_Guest_WithPendingReservation_ReturnsFalse()
    {
        // Pending reservations block guest deletion (same as Approved)
        var guestId = Guid.NewGuid();
        _db.Reservations.Add(new Reservation
        {
            UserId = guestId,
            AccommodationId = Guid.NewGuid(),
            HostId = Guid.NewGuid(),
            FromDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5),
            ToDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10),
            NumOfGuests = 1,
            Status = ReservationStatus.Pending
        });
        await _db.SaveChangesAsync();

        var result = await _sut.CanDeleteUser(guestId, "Guest");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<CanDeleteResponse>().Subject;
        body.CanDelete.Should().BeFalse();
        body.ActiveCount.Should().Be(1);
        body.Reason.Should().Contain("active or pending");
    }

    [Fact]
    public async Task CanDelete_Guest_WithCancelledReservation_ReturnsTrue()
    {
        var guestId = Guid.NewGuid();
        _db.Reservations.Add(new Reservation
        {
            UserId = guestId,
            AccommodationId = Guid.NewGuid(),
            HostId = Guid.NewGuid(),
            FromDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5),
            ToDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10),
            NumOfGuests = 1,
            Status = ReservationStatus.Cancelled
        });
        await _db.SaveChangesAsync();

        var result = await _sut.CanDeleteUser(guestId, "Guest");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<CanDeleteResponse>().Subject;
        body.CanDelete.Should().BeTrue();
    }

    // ============================================================
    // Host deletion checks
    // ============================================================

    [Fact]
    public async Task CanDelete_Host_NoReservations_ReturnsTrue()
    {
        var hostId = Guid.NewGuid();

        var result = await _sut.CanDeleteUser(hostId, "Host");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<CanDeleteResponse>().Subject;
        body.CanDelete.Should().BeTrue();
    }

    [Fact]
    public async Task CanDelete_Host_WithApprovedFutureReservation_ReturnsFalse()
    {
        var hostId = Guid.NewGuid();
        _db.Reservations.Add(new Reservation
        {
            UserId = Guid.NewGuid(),
            AccommodationId = Guid.NewGuid(),
            HostId = hostId,
            FromDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5),
            ToDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10),
            NumOfGuests = 2,
            Status = ReservationStatus.Approved
        });
        await _db.SaveChangesAsync();

        var result = await _sut.CanDeleteUser(hostId, "Host");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<CanDeleteResponse>().Subject;
        body.CanDelete.Should().BeFalse();
        body.ActiveCount.Should().Be(1);
        body.Reason.Should().Contain("active or pending reservation");
    }

    [Fact]
    public async Task CanDelete_Host_WithPendingFutureReservation_ReturnsFalse()
    {
        // Pending reservations DO block host deletion
        var hostId = Guid.NewGuid();
        _db.Reservations.Add(new Reservation
        {
            UserId = Guid.NewGuid(),
            AccommodationId = Guid.NewGuid(),
            HostId = hostId,
            FromDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5),
            ToDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10),
            NumOfGuests = 1,
            Status = ReservationStatus.Pending
        });
        await _db.SaveChangesAsync();

        var result = await _sut.CanDeleteUser(hostId, "Host");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<CanDeleteResponse>().Subject;
        body.CanDelete.Should().BeFalse();
    }

    [Fact]
    public async Task CanDelete_Host_WithPastReservations_ReturnsTrue()
    {
        var hostId = Guid.NewGuid();
        _db.Reservations.Add(new Reservation
        {
            UserId = Guid.NewGuid(),
            AccommodationId = Guid.NewGuid(),
            HostId = hostId,
            FromDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10),
            ToDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3),
            NumOfGuests = 1,
            Status = ReservationStatus.Approved
        });
        await _db.SaveChangesAsync();

        var result = await _sut.CanDeleteUser(hostId, "Host");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<CanDeleteResponse>().Subject;
        body.CanDelete.Should().BeTrue();
    }

    [Fact]
    public async Task CanDelete_Host_MultipleAccommodations_CountsAll()
    {
        var hostId = Guid.NewGuid();
        var acc1 = Guid.NewGuid();
        var acc2 = Guid.NewGuid();

        _db.Reservations.AddRange(
            new Reservation
            {
                UserId = Guid.NewGuid(),
                AccommodationId = acc1,
                HostId = hostId,
                FromDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
                ToDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5),
                NumOfGuests = 2,
                Status = ReservationStatus.Approved
            },
            new Reservation
            {
                UserId = Guid.NewGuid(),
                AccommodationId = acc2,
                HostId = hostId,
                FromDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2),
                ToDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(8),
                NumOfGuests = 1,
                Status = ReservationStatus.Pending
            });
        await _db.SaveChangesAsync();

        var result = await _sut.CanDeleteUser(hostId, "Host");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<CanDeleteResponse>().Subject;
        body.CanDelete.Should().BeFalse();
        body.ActiveCount.Should().Be(2);
    }
}
