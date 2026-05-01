using Invest.Core.Entities;

namespace Invest.Service.Interfaces;

public interface ICategoryRepository
{
    Task<IEnumerable<Theme>> GetAll(bool trackChanges);
    Task<Theme> Get(int categoryId, bool trackChanges);
    Task Create(Theme category);
    Task Remove(Theme category);
}
