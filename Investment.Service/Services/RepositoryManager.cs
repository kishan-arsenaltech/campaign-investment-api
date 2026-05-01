using AutoMapper;
using Invest.Core.Models;
using Invest.Core.Settings;
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
        private RepositoryContext _repositoryContext;

        private ICategoryRepository? _categoryRepository;
        private IUserAuthenticationRepository? _userAuthenticationRepository;
        private UserManager<User> _userManager;
        private RoleManager<ApplicationRole> _roleManager;
        private IMapper _mapper;
        private JwtConfig _jwtConfig;
        private IMailService _mailService;
        private EmailQueue _emailQueue;
        private AppSecrets _appSecrets;

        public RepositoryManager(RepositoryContext repositoryContext, UserManager<User> userManager, RoleManager<ApplicationRole> roleManager, IMapper mapper, JwtConfig jwtConfig, IMailService mailService, EmailQueue emailQueue, AppSecrets appSecrets)
        {
            _repositoryContext = repositoryContext;
            _userManager = userManager;
            _roleManager = roleManager;
            _mapper = mapper;
            _jwtConfig = jwtConfig;
            _mailService = mailService;
            _emailQueue = emailQueue;
            _appSecrets = appSecrets;
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
                    _userAuthenticationRepository = new UserAuthenticationRepository(_userManager, _roleManager, _jwtConfig, _mapper, _mailService, _repositoryContext, _emailQueue, _appSecrets);
                return _userAuthenticationRepository;
            }
        }
        public Task SaveAsync() => _repositoryContext.SaveChangesAsync();
    }
}
