using CdsHelper.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace CdsHelper.Api.Data;

public class AppDbContext : DbContext
{
    public DbSet<CityEntity> Cities { get; set; } = null!;

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CityEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(50);
            entity.Property(e => e.CulturalSphere).HasMaxLength(50);
        });
    }
}
