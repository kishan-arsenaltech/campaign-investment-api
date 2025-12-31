// Ignore Spelling: Repo

using Microsoft.EntityFrameworkCore;
using Invest.Repo.GenericRepository.Interface;
using System.Linq.Expressions;
using Investment.Repo.Context;

namespace Invest.Repo.GenericRepository.Service
{
    public abstract class RepositoryBase<T> : IRepositoryBase<T> where T : class
    {
        protected readonly RepositoryContext _repositoryContext;

        public RepositoryBase(RepositoryContext repositoryContext)
        {
            _repositoryContext = repositoryContext;
        }

        public Task<IQueryable<T>> FindAllAsync(bool trackChanges)
        {
            var result = trackChanges
                            ? _repositoryContext.Set<T>()
                            : _repositoryContext.Set<T>().AsNoTracking();

            return Task.FromResult(result);
        }

        public Task<IQueryable<T>> FindByConditionAsync(Expression<Func<T, bool>> expression, bool trackChanges)
        {
            var result = trackChanges
                            ? _repositoryContext.Set<T>().Where(expression)
                            : _repositoryContext.Set<T>().Where(expression).AsNoTracking();
            
            return Task.FromResult(result);
        }

        public async Task CreateAsync(T entity)
        {
            await _repositoryContext.Set<T>().AddAsync(entity);
            await _repositoryContext.SaveChangesAsync();
        }

        public async Task UpdateAsync(T entity)
        {
            _repositoryContext.Set<T>().Update(entity);
            await _repositoryContext.SaveChangesAsync();
        }

        public async Task RemoveAsync(T entity)
        {
            _repositoryContext.Set<T>().Remove(entity);
            await _repositoryContext.SaveChangesAsync();
        }
    }
}
