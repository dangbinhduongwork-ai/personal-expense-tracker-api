using ExpenseTracker.Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExpenseTracker.Infrastructure.Data.Configurations;

internal sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("Transactions", table =>
        {
            table.HasCheckConstraint("CK_Transactions_Amount_Positive", "[Amount] > 0");
            table.HasCheckConstraint("CK_Transactions_Type", "[Type] IN (N'Income', N'Expense')");
        });

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.Amount)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(t => t.Type)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(10);

        builder.Property(t => t.Description)
            .HasMaxLength(1000);

        builder.Property(t => t.TransactionDate)
            .IsRequired()
            .HasColumnType("date");

        builder.Property(t => t.CreatedAt)
            .IsRequired()
            .HasColumnType("datetime2");

        builder.Property(t => t.UpdatedAt)
            .IsRequired()
            .HasColumnType("datetime2");

        builder.HasOne(t => t.User)
            .WithMany(u => u.Transactions)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict (ON DELETE NO ACTION in SQL Server) enforces BR-09 and avoids
        // SQL Server Msg 1785 multiple cascade paths through Users -> Categories -> Transactions.
        builder.HasOne(t => t.Category)
            .WithMany(c => c.Transactions)
            .HasForeignKey(t => t.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => new { t.UserId, t.TransactionDate })
            .HasDatabaseName("IX_Transactions_UserId_TransactionDate");

        builder.HasIndex(t => new { t.UserId, t.Type, t.TransactionDate })
            .HasDatabaseName("IX_Transactions_UserId_Type_TransactionDate")
            .IncludeProperties(t => new { t.Amount, t.CategoryId });

        builder.HasIndex(t => t.CategoryId)
            .HasDatabaseName("IX_Transactions_CategoryId");
    }
}

