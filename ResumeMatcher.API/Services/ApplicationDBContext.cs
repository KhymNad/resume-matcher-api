using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) {}

    public DbSet<Skill> Skills => Set<Skill>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Skill>()
            .Property(s => s.Id)
            .HasDefaultValueSql("gen_random_uuid()");
    }
}

[Table("skills")]
public class Skill
{
    [Column("id")]
    public Guid Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = "";

    [Column("type")]
    public string? Type { get; set; }

    [Column("source")]
    public string? Source { get; set; }
}

