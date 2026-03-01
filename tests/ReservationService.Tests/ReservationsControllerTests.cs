using System.Security.Claims;

using FluentAssertions;

using Hotelier.Events;

using MassTransit;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using ReservationService.Api;
using ReservationService.Domain;
using ReservationService.Infrastructure;

namespace ReservationService.Tests;

public class ReservationsControllerTests : IDisposable
{
    private readonly ReservationDbContext _db;
    private readonly Mock<IPublishEndpoint> _publisher;
    private readonly Mock<IAccommodationServiceClient> _accommodationClient;
    private readonly Mock<IAvailabilityServiceClient> _availabilityClient;
    private readonly ReservationsController _sut;

    private readonly Guid _guestId = Guid.NewGuid();
    private readonly Guid _hostId = Guid.NewGuid();
    private readonly Guid _accommodationId = Guid.NewGuid();

    public ReservationsControllerTests()
    {
        var options = new DbContextOptionsBuilder<ReservationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new ReservationDbContext(options);
        _publisher = new Mock<IPublishEndpoint>();
        _accommodationClient = new Mock<IAccommodationServiceClient>();
        _availabilityClient = new Mock<IAvailabilityServiceClient>();
        var logger = new Mock<ILogger<ReservationsController>>();

        _sut = new ReservationsController(
            _db, _publisher.Object,
            _accommodationClient.Object,
            _availabilityClient.Object,
            logger.Object);

        SetupDefaultMocks();
    }

    public void Dispose() => _db.Dispose();

    // ------ helpers ------

    private void SetUser(Guid userId, string role = "Guest")
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role)
        };
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
            }
        };
    }

    private void SetupDefaultMocks()
    {
        _accommodationClient
            .Setup(x => x.GetAccommodationAsync(It.IsAny<Guid>()))
            .ReturnsAsync(new AccommodationInfo
            {
                Id = _accommodationId,
                HostId = _hostId,
                AutoApproval = false,
                MinGuests = 1,
                MaxGuests = 10
            });

        _availabilityClient
            .Setup(x => x.CheckAvailabilityAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(new AvailabilityCheckResult { IsAvailable = true });
    }

    private CreateReservationRequest MakeRequest(DateOnly? from = null, DateOnly? to = null, int guests = 2)
        => new()
        {
            AccommodationId = _accommodationId,
            FromDate = from ?? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10),
            ToDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(15),
            NumOfGuests = guests
        };

    private Reservation SeedReservation(
        ReservationStatus status = ReservationStatus.Pending,
        Guid? guestId = null, Guid? hostId = null, Guid? accommodationId = null,
        DateOnly? from = null, DateOnly? to = null)
    {
        var r = new Reservation
        {
            UserId = guestId ?? _guestId,
            AccommodationId = accommodationId ?? _accommodationId,
            HostId = hostId ?? _hostId,
            FromDate = from ?? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10),
            ToDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(15),
            NumOfGuests = 2,
            Status = status,
            CreatedBy = (guestId ?? _guestId).ToString()
        };
        _db.Reservations.Add(r);
        _db.SaveChanges();
        return r;
    }

    // =======================================================
    //  CREATE
    // =======================================================

    [Fact]
    public async Task Create_ValidRequest_ReturnsPendingReservation()
    {
        SetUser(_guestId);

        var result = await _sut.Create(MakeRequest());

        var created = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var body = created.Value.Should().BeOfType<ReservationResponse>().Subject;
        body.Status.Should().Be(ReservationStatus.Pending);
        body.UserId.Should().Be(_guestId);
        body.HostId.Should().Be(_hostId);
    }

    [Fact]
    public async Task Create_PublishesReservationCreatedEvent()
    {
        SetUser(_guestId);

        await _sut.Create(MakeRequest());

        _publisher.Verify(p => p.Publish(
            It.Is<ReservationCreated>(e =>
                e.GuestId == _guestId &&
                e.HostId == _hostId &&
                e.AccommodationId == _accommodationId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_AutoApproval_ReturnsApprovedAndPublishesBothEvents()
    {
        SetUser(_guestId);

        _accommodationClient
            .Setup(x => x.GetAccommodationAsync(_accommodationId))
            .ReturnsAsync(new AccommodationInfo
            {
                Id = _accommodationId,
                HostId = _hostId,
                AutoApproval = true,
                MinGuests = 1,
                MaxGuests = 10
            });

        var result = await _sut.Create(MakeRequest());

        var created = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var body = created.Value.Should().BeOfType<ReservationResponse>().Subject;
        body.Status.Should().Be(ReservationStatus.Approved);

        _publisher.Verify(p => p.Publish(
            It.IsAny<ReservationApproved>(),
            It.IsAny<CancellationToken>()), Times.Once);

        _publisher.Verify(p => p.Publish(
            It.IsAny<ReservationCreated>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_AutoApproval_RejectsOverlappingPending()
    {
        SetUser(_guestId);

        var from = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10);
        var to = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(15);

        // Seed an existing pending reservation that overlaps
        var overlapping = SeedReservation(
            ReservationStatus.Pending,
            guestId: Guid.NewGuid(), // different guest
            from: from.AddDays(-1),
            to: to.AddDays(1));

        _accommodationClient
            .Setup(x => x.GetAccommodationAsync(_accommodationId))
            .ReturnsAsync(new AccommodationInfo
            {
                Id = _accommodationId,
                HostId = _hostId,
                AutoApproval = true,
                MinGuests = 1,
                MaxGuests = 10
            });

        await _sut.Create(MakeRequest(from, to));

        var rejected = await _db.Reservations.FindAsync(overlapping.Id);
        rejected!.Status.Should().Be(ReservationStatus.Denied);
    }

    [Fact]
    public async Task Create_BadDateRange_ReturnsBadRequest()
    {
        SetUser(_guestId);

        var result = await _sut.Create(MakeRequest(
            from: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(15),
            to: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10)));

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Create_PastDate_ReturnsBadRequest()
    {
        SetUser(_guestId);

        var result = await _sut.Create(MakeRequest(
            from: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-5),
            to: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1)));

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Create_TooManyGuests_ReturnsBadRequest()
    {
        SetUser(_guestId);

        var result = await _sut.Create(MakeRequest(guests: 50));

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Create_AccommodationNotFound_ReturnsBadRequest()
    {
        SetUser(_guestId);

        _accommodationClient
            .Setup(x => x.GetAccommodationAsync(It.IsAny<Guid>()))
            .ReturnsAsync((AccommodationInfo?)null);

        var result = await _sut.Create(MakeRequest());

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Create_NotAvailable_ReturnsConflict()
    {
        SetUser(_guestId);

        _availabilityClient
            .Setup(x => x.CheckAvailabilityAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(new AvailabilityCheckResult { IsAvailable = false });

        var result = await _sut.Create(MakeRequest());

        result.Should().BeOfType<ConflictObjectResult>();
    }

    // =======================================================
    //  GET by ID
    // =======================================================

    [Fact]
    public async Task GetById_GuestCanViewOwn()
    {
        var r = SeedReservation();
        SetUser(_guestId);

        var result = await _sut.GetById(r.Id);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<ReservationResponse>().Subject;
        body.Id.Should().Be(r.Id);
    }

    [Fact]
    public async Task GetById_HostCanViewTheirReservation()
    {
        var r = SeedReservation();
        SetUser(_hostId, "Host");

        var result = await _sut.GetById(r.Id);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetById_OtherUserForbidden()
    {
        var r = SeedReservation();
        SetUser(Guid.NewGuid());

        var result = await _sut.GetById(r.Id);

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetById_NotFound_Returns404()
    {
        SetUser(_guestId);

        var result = await _sut.GetById(Guid.NewGuid());

        result.Should().BeOfType<NotFoundResult>();
    }

    // =======================================================
    //  GET /mine
    // =======================================================

    [Fact]
    public async Task GetMine_ReturnsGuestReservationsOnly()
    {
        SeedReservation(); // belongs to _guestId
        SeedReservation(guestId: Guid.NewGuid()); // different guest

        SetUser(_guestId);

        var result = await _sut.GetMyReservations();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var items = ok.Value.Should().BeAssignableTo<IEnumerable<ReservationResponse>>().Subject.ToList();
        items.Should().HaveCount(1);
        items[0].UserId.Should().Be(_guestId);
    }

    [Fact]
    public async Task GetMine_WithStatusFilter_Filters()
    {
        SeedReservation(ReservationStatus.Pending);
        SeedReservation(ReservationStatus.Approved);

        SetUser(_guestId);

        var result = await _sut.GetMyReservations(ReservationStatus.Approved);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var items = ok.Value.Should().BeAssignableTo<IEnumerable<ReservationResponse>>().Subject.ToList();
        items.Should().HaveCount(1);
        items[0].Status.Should().Be(ReservationStatus.Approved);
    }

    // =======================================================
    //  GET /host
    // =======================================================

    [Fact]
    public async Task GetHost_ReturnsHostReservationsOnly()
    {
        SeedReservation(); // host = _hostId
        SeedReservation(hostId: Guid.NewGuid()); // different host

        SetUser(_hostId, "Host");

        var result = await _sut.GetHostReservations();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var items = ok.Value.Should().BeAssignableTo<IEnumerable<ReservationResponse>>().Subject.ToList();
        items.Should().HaveCount(1);
    }

    // =======================================================
    //  DELETE (pending only)
    // =======================================================

    [Fact]
    public async Task Delete_PendingReservation_Succeeds()
    {
        var r = SeedReservation(ReservationStatus.Pending);
        SetUser(_guestId);

        var result = await _sut.Delete(r.Id);

        result.Should().BeOfType<NoContentResult>();
        (await _db.Reservations.FindAsync(r.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Delete_ApprovedReservation_ReturnsConflict()
    {
        var r = SeedReservation(ReservationStatus.Approved);
        SetUser(_guestId);

        var result = await _sut.Delete(r.Id);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task Delete_NotOwner_ReturnsForbid()
    {
        var r = SeedReservation();
        SetUser(Guid.NewGuid());

        var result = await _sut.Delete(r.Id);

        result.Should().BeOfType<ForbidResult>();
    }

    // =======================================================
    //  CANCEL (approved only, 1 day before)
    // =======================================================

    [Fact]
    public async Task Cancel_ApprovedReservation_Succeeds()
    {
        var r = SeedReservation(ReservationStatus.Approved);
        SetUser(_guestId);

        var result = await _sut.Cancel(r.Id);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<ReservationResponse>().Subject;
        body.Status.Should().Be(ReservationStatus.Cancelled);

        _publisher.Verify(p => p.Publish(
            It.Is<ReservationCancelled>(e => e.ReservationId == r.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancel_PendingReservation_ReturnsConflict()
    {
        var r = SeedReservation(ReservationStatus.Pending);
        SetUser(_guestId);

        var result = await _sut.Cancel(r.Id);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task Cancel_TooCloseToStart_ReturnsConflict()
    {
        // Starts tomorrow — cannot cancel
        var r = SeedReservation(
            ReservationStatus.Approved,
            from: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
            to: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3));

        SetUser(_guestId);

        var result = await _sut.Cancel(r.Id);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task Cancel_NotOwner_ReturnsForbid()
    {
        var r = SeedReservation(ReservationStatus.Approved);
        SetUser(Guid.NewGuid());

        var result = await _sut.Cancel(r.Id);

        result.Should().BeOfType<ForbidResult>();
    }

    // =======================================================
    //  APPROVE
    // =======================================================

    [Fact]
    public async Task Approve_PendingReservation_Succeeds()
    {
        var r = SeedReservation(ReservationStatus.Pending);
        SetUser(_hostId, "Host");

        var result = await _sut.Approve(r.Id);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<ReservationResponse>().Subject;
        body.Status.Should().Be(ReservationStatus.Approved);

        _publisher.Verify(p => p.Publish(
            It.Is<ReservationApproved>(e => e.ReservationId == r.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Approve_RejectsOverlappingPending()
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10);
        var to = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(15);

        var r = SeedReservation(ReservationStatus.Pending, from: from, to: to);
        var overlapping = SeedReservation(
            ReservationStatus.Pending,
            guestId: Guid.NewGuid(),
            from: from.AddDays(-1),
            to: to.AddDays(1));

        SetUser(_hostId, "Host");

        await _sut.Approve(r.Id);

        var rejected = await _db.Reservations.FindAsync(overlapping.Id);
        rejected!.Status.Should().Be(ReservationStatus.Denied);

        _publisher.Verify(p => p.Publish(
            It.Is<ReservationRejected>(e => e.ReservationId == overlapping.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Approve_AlreadyApproved_ReturnsConflict()
    {
        var r = SeedReservation(ReservationStatus.Approved);
        SetUser(_hostId, "Host");

        var result = await _sut.Approve(r.Id);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task Approve_NotHost_ReturnsForbid()
    {
        var r = SeedReservation(ReservationStatus.Pending);
        SetUser(Guid.NewGuid(), "Host");

        var result = await _sut.Approve(r.Id);

        result.Should().BeOfType<ForbidResult>();
    }

    // =======================================================
    //  REJECT
    // =======================================================

    [Fact]
    public async Task Reject_PendingReservation_Succeeds()
    {
        var r = SeedReservation(ReservationStatus.Pending);
        SetUser(_hostId, "Host");

        var result = await _sut.Reject(r.Id, new RejectReservationRequest { Reason = "Fully booked" });

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<ReservationResponse>().Subject;
        body.Status.Should().Be(ReservationStatus.Denied);

        _publisher.Verify(p => p.Publish(
            It.Is<ReservationRejected>(e =>
                e.ReservationId == r.Id && e.Reason == "Fully booked"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reject_AlreadyApproved_ReturnsConflict()
    {
        var r = SeedReservation(ReservationStatus.Approved);
        SetUser(_hostId, "Host");

        var result = await _sut.Reject(r.Id);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task Reject_NotHost_ReturnsForbid()
    {
        var r = SeedReservation(ReservationStatus.Pending);
        SetUser(Guid.NewGuid(), "Host");

        var result = await _sut.Reject(r.Id);

        result.Should().BeOfType<ForbidResult>();
    }

    // =======================================================
    //  GUEST HISTORY
    // =======================================================

    [Fact]
    public async Task GuestHistory_ReturnsCounts()
    {
        SeedReservation(ReservationStatus.Approved);
        SeedReservation(ReservationStatus.Cancelled);
        SeedReservation(ReservationStatus.Cancelled);

        SetUser(_hostId, "Host");

        var result = await _sut.GetGuestHistory(_guestId);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        // use dynamic because anonymous type
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        doc.RootElement.GetProperty("totalReservations").GetInt32().Should().Be(3);
        doc.RootElement.GetProperty("cancelledReservations").GetInt32().Should().Be(2);
    }

    // =======================================================
    //  INTERNAL: completed stay check
    // =======================================================

    [Fact]
    public async Task HasCompletedStay_Accommodation_ReturnsTrue()
    {
        // Past approved reservation
        SeedReservation(
            ReservationStatus.Approved,
            from: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10),
            to: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-5));

        var result = await _sut.HasCompletedStay(_guestId, _accommodationId, "Accommodation");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        doc.RootElement.GetProperty("hasCompleted").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task HasCompletedStay_Host_ReturnsTrue()
    {
        SeedReservation(
            ReservationStatus.Approved,
            from: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10),
            to: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-5));

        var result = await _sut.HasCompletedStay(_guestId, _hostId, "Host");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        doc.RootElement.GetProperty("hasCompleted").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task HasCompletedStay_NoCompletedStay_ReturnsFalse()
    {
        // Future reservation — not completed yet
        SeedReservation(
            ReservationStatus.Approved,
            from: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5),
            to: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10));

        var result = await _sut.HasCompletedStay(_guestId, _accommodationId, "Accommodation");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        doc.RootElement.GetProperty("hasCompleted").GetBoolean().Should().BeFalse();
    }
}
