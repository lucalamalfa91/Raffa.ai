using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Raffa.Chat.Domain.Memory;

namespace Raffa.Chat.Infrastructure.Configurations;

public sealed class UserProcurementMemoryConfiguration : IEntityTypeConfiguration<UserProcurementMemory>
{
    public void Configure(EntityTypeBuilder<UserProcurementMemory> builder)
    {
        builder.ToTable("user_procurement_memory");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(ValueConverters.EntityIdConverter).ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasConversion(ValueConverters.TenantIdConverter);
        builder.Property(x => x.UserId).HasMaxLength(200);
        builder.Property(x => x.ProfileJson).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
        builder.HasIndex(x => x.ExpiresAt);
    }
}
