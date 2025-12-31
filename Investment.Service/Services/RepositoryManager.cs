using AutoMapper;
using Invest.Service.Interfaces;
using Invest.Service.Services;
using Investment.Core.Entities;
using Investment.Repo.Context;
using Investment.Service.Interfaces;
using Microsoft.AspNetCore.Identity;

namespace Investment.Service.Services
{
    public class RepositoryManager : IRepositoryManager
    {
        private ICategoryRepository? _categoryRepository;
        private RepositoryContext _repositoryContext;
        private IUserAuthenticationRepository? _userAuthenticationRepository;
        private UserManager<User> _userManager;
        private RoleManager<IdentityRole> _roleManager;
        private IMapper _mapper;
        private JwtConfig _jwtConfig;
        private IMailService _mailService;

        public RepositoryManager(RepositoryContext repositoryContext, UserManager<User> userManager, RoleManager<IdentityRole> roleManager, IMapper mapper, JwtConfig jwtConfig, IMailService mailService)
        {
            _repositoryContext = repositoryContext;
            _userManager = userManager;
            _roleManager = roleManager;
            _mapper = mapper;
            _jwtConfig = jwtConfig;
            _mailService = mailService;
        }

        public ICategoryRepository Category
        {
            get
            {
                if (_categoryRepository is null)
                    _categoryRepository = new CategoryRepository(_repositoryContext);
                return _categoryRepository;
            }
        }

        public IUserAuthenticationRepository UserAuthentication
        {
            get
            {
                if (_userAuthenticationRepository is null)
                    _userAuthenticationRepository = new UserAuthenticationRepository(_userManager, _roleManager, _jwtConfig, _mapper, _mailService);
                return _userAuthenticationRepository;
            }
        }

        public Task SaveAsync() => _repositoryContext.SaveChangesAsync();
    }
}
