using APIContagem.Models;

namespace APIContagem.Data;

public interface IContagemRepository
{
    void Insert(ResultadoContador resultado);
}
