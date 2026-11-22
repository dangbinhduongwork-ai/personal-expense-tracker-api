using ExpenseTracker.Application.Entities;
using ExpenseTracker.Infrastructure.Data.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseTracker.Infrastructure.Data.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories", table =>
        {
            table.HasCheckConstraint("CK_Categories_Type", "[Type] IN (N'Income', N'Expense')");
        });

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(100);

        // MaxLength(10) prevents EF Core from mapping string-converted enum to nvarchar(max),
        // which would fail SQL Server index key creation.
        builder.Property(c => c.Type)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(10);

        builder.Property(c => c.CreatedAt)
            .IsRequired()
            .HasColumnType("datetime2");

        builder.Property(c => c.UpdatedAt)
            .IsRequired()
            .HasColumnType("datetime2");

        // .HasFilter(null) overrides EF Core SQL Server's default "WHERE [UserId] IS NOT NULL"
        // convention so system categories (UserId IS NULL) remain unique per (Type, Name).
        builder.HasIndex(c => new { c.UserId, c.Type, c.Name })
            .IsUnique()
            .HasDatabaseName("UX_Categories_UserId_Type_Name")
            .HasFilter(null);

        // Explicit Cascade overrides EF Core's ClientSetNull default on optional FKs:
        // personal categories belong to the user and are deleted if the user is removed.
        builder.HasOne(c => c.User)
            .WithMany(u => u.Categories)
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasData(SystemCategorySeed.GetSystemCategories());
    }
}

