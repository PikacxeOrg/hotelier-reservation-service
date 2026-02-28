using ReservationService.Domain;

using Microsoft.EntityFrameworkCore;

namespace ReservationService.Infrastructure;

public class ReservationDbContext : DbContext
{
    public ReservationDbContext(DbContextOptions<ReservationDbContext> options) : base(options) { }

    public DbSet<Reservation> Reservations => Set<Reservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Reservation>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.HasIndex(r => r.UserId);
            entity.HasIndex(r => r.AccommodationId);
            entity.HasIndex(r => r.HostId);
            entity.HasIndex(r => new { r.AccommodationId, r.FromDate, r.ToDate });
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<TrackableEntity>())
        {
            if (entry.State == EntityState.Modified)
                entry.Entity.ModifiedTimestamp = DateTime.UtcNow;
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
