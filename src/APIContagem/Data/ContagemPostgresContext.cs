using Microsoft.EntityFrameworkCore;

namespace APIContagem.Data;

public class ContagemPostgresContext : DbContext
{
    public DbSet<HistoricoContagem>? Historicos { get; set; }

    public ContagemPostgresContext(DbContextOptions<ContagemPostgresContext> options) :
        base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HistoricoContagem>(entity =>
        {
            entity.ToTable("HistoricoContagemPostgres");
            entity.HasKey(c => c.Id);
        });
    }
}