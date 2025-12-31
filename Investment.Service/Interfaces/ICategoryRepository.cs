using Invest.Core.Entities;

namespace Invest.Service.Interfaces;

public interface ICategoryRepository
{
    Task<IEnumerable<Category>> GetAll(bool trackChanges);
    Task<Category> Get(int categoryId, bool trackChanges);
    Task Create(Category category);
    Task Remove(Category category);
}
