using Microsoft.EntityFrameworkCore;
using Invest.Service.Interfaces;
using Invest.Core.Entities;
using Investment.Repo.Context;
using Invest.Repo.GenericRepository.Service;

namespace Invest.Service.Services;

public class CategoryRepository : RepositoryBase<Theme>, ICategoryRepository
{
    public CategoryRepository(RepositoryContext repositoryContext) : base(repositoryContext)
    {
    }

    public async Task Create(Theme category) => await CreateAsync(category);

    public async Task Remove(Theme category) => await RemoveAsync(category);

    public async Task<IEnumerable<Theme>> GetAll(bool trackChanges)
        => await FindAllAsync(trackChanges).Result.OrderBy(c => c.Name).ToListAsync();

    public async Task<Theme> Get(int categoryId, bool trackChanges)
    {
        var categories = await FindByConditionAsync(c => c.Id.Equals(categoryId), trackChanges).Result.SingleOrDefaultAsync();
        
        return categories!;
    }
}
