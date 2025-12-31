using Invest.Service.Interfaces;

namespace Investment.Service.Interfaces
{
    public interface IRepositoryManager
    {
        ICategoryRepository Category { get; }
        IUserAuthenticationRepository UserAuthentication { get; }
        Task SaveAsync();
    }
}
