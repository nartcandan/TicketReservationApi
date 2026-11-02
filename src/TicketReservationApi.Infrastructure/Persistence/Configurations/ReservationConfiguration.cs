using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketReservationApi.Domain.Entities;

namespace TicketReservationApi.Infrastructure.Persistence.Configurations;

public class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("Reservations");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasOne(r => r.Event)
            .WithMany(e => e.Reservations)
            .HasForeignKey(r => r.EventId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.User)
            .WithMany(u => u.Reservations)
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Intentionally not unique yet: the filtered unique index guaranteeing K2
        // is added during the concurrency experiment (PLAN.md, step 4).
        builder.HasIndex(r => new { r.EventId, r.UserId });
        builder.HasIndex(r => r.UserId);
    }
}
