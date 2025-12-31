using Microsoft.EntityFrameworkCore;
using Invest.Service.Interfaces;
using Invest.Core.Entities;
using Investment.Repo.Context;
using Invest.Repo.GenericRepository.Service;

namespace Invest.Service.Services;

public class CategoryRepository : RepositoryBase<Category>, ICategoryRepository
{
    public CategoryRepository(RepositoryContext repositoryContext) : base(repositoryContext)
    {
    }

    public async Task Create(Category category) => await CreateAsync(category);

    public async Task Remove(Category category) => await RemoveAsync(category);

    public async Task<IEnumerable<Category>> GetAll(bool trackChanges)
        => await FindAllAsync(trackChanges).Result.OrderBy(c => c.Name).ToListAsync();

    public async Task<Category> Get(int categoryId, bool trackChanges)
    {
        var categories = await FindByConditionAsync(c => c.Id.Equals(categoryId), trackChanges).Result.SingleOrDefaultAsync();
        
        return categories!;
    }
}
