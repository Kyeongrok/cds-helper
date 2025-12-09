using CdsHelper.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace CdsHelper.Api.Data;

public class AppDbContext : DbContext
{
    public DbSet<CityEntity> Cities { get; set; } = null!;
    public DbSet<BookEntity> Books { get; set; } = null!;
    public DbSet<BookCityEntity> BookCities { get; set; } = null!;

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CityEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever(); // JSON에서 직접 ID 지정
            entity.Property(e => e.Name).IsRequired().HasMaxLength(50);
            entity.Property(e => e.CulturalSphere).HasMaxLength(50);
        });

        modelBuilder.Entity<BookEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
        });

        modelBuilder.Entity<BookCityEntity>(entity =>
        {
            entity.HasKey(e => new { e.BookId, e.CityId });

            entity.HasOne(e => e.Book)
                .WithMany(b => b.BookCities)
                .HasForeignKey(e => e.BookId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.City)
                .WithMany(c => c.BookCities)
                .HasForeignKey(e => e.CityId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
