using Microsoft.EntityFrameworkCore;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    public DbSet<FileMetadata> Files => Set<FileMetadata>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // uploaded_at TIMESTAMP DEFAULT NOW() -- the database fills it in on insert.
        // The value is stored as a UTC timestamp; marking it as UTC on read makes
        // it serialize with a "Z" suffix, matching the Go version's RFC 3339 output.
        modelBuilder.Entity<FileMetadata>()
            .Property(f => f.UploadedAt)
            .HasColumnType("timestamp without time zone")
            .HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAdd()
            .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
    }
}
