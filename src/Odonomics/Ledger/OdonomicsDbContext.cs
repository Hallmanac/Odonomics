using Microsoft.EntityFrameworkCore;

namespace Odonomics.Ledger;

public sealed class OdonomicsDbContext(DbContextOptions<OdonomicsDbContext> options) : DbContext(options)
{
    public DbSet<VehicleEntity> Vehicles => Set<VehicleEntity>();
    public DbSet<PostingEntity> Postings => Set<PostingEntity>();
    public DbSet<PriceObservationEntity> PriceObservations => Set<PriceObservationEntity>();
    public DbSet<RunEntity> Runs => Set<RunEntity>();
    public DbSet<VinRecordEntity> VinRecords => Set<VinRecordEntity>();
    public DbSet<NoteEntity> Notes => Set<NoteEntity>();
    public DbSet<DealerEntity> Dealers => Set<DealerEntity>();
    public DbSet<LedgerMigrationEntity> LedgerMigrations => Set<LedgerMigrationEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<VehicleEntity>(entity =>
        {
            entity.HasKey(v => v.Vin);
            entity.HasMany(v => v.Postings).WithOne(p => p.Vehicle).HasForeignKey(p => p.VehicleVin);
            entity.HasMany(v => v.Notes).WithOne(n => n.Vehicle).HasForeignKey(n => n.VehicleVin);
            entity.HasOne(v => v.VinRecord).WithOne(r => r.Vehicle).HasForeignKey<VinRecordEntity>(r => r.Vin);
        });

        modelBuilder.Entity<PostingEntity>(entity =>
        {
            entity.HasIndex(p => new { p.VehicleVin, p.Source, p.Url }).IsUnique();
            entity.HasMany(p => p.PriceObservations).WithOne(o => o.Posting).HasForeignKey(o => o.PostingId);
            entity.HasOne(p => p.Dealer).WithMany(d => d.Postings).HasForeignKey(p => p.DealerId);
        });

        modelBuilder.Entity<VinRecordEntity>().HasKey(r => r.Vin);

        modelBuilder.Entity<DealerEntity>(entity =>
        {
            entity.HasIndex(d => new { d.NormalizedName, d.NormalizedLocation }).IsUnique();
        });

        modelBuilder.Entity<LedgerMigrationEntity>().HasKey(m => m.Name);
    }
}
