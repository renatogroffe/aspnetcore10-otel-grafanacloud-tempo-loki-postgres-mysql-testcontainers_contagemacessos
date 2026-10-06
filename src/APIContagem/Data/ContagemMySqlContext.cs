using Microsoft.EntityFrameworkCore;

namespace APIContagem.Data;

public class ContagemMySqlContext : DbContext
{
    public DbSet<HistoricoContagem>? Historicos { get; set; }

    public ContagemMySqlContext(DbContextOptions<ContagemMySqlContext> options) :
        base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HistoricoContagem>(entity =>
        {
            entity.ToTable("HistoricoContagemMySql");
            entity.HasKey(c => c.Id);
        });
    }
}
