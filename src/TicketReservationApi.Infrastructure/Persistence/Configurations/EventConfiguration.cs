using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketReservationApi.Domain.Entities;

namespace TicketReservationApi.Infrastructure.Persistence.Configurations;

public class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("Events", t =>
        {
            t.HasCheckConstraint("CK_Events_Capacity", "[Capacity] > 0");
            t.HasCheckConstraint("CK_Events_ReservedCount", "[ReservedCount] >= 0 AND [ReservedCount] <= [Capacity]");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Title).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.Property(e => e.Venue).HasMaxLength(200).IsRequired();

        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(e => e.RowVersion).IsRowVersion();

        builder.HasIndex(e => e.StartsAt);
    }
}
