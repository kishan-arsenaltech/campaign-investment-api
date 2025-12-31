using AutoMapper;
using Investment.Core.Dtos;
using Investment.Core.Entities;
using Investment.Service.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Investment.Service.Services
{
    internal sealed class UserAuthenticationRepository : IUserAuthenticationRepository
    {
        private readonly UserManager<User> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly JwtConfig _jwtConfig;
        private readonly IMapper _mapper;
        private readonly IMailService _mailService;
        private User? _user;
        private User? _userLoginFromAdmin;

        public UserAuthenticationRepository(UserManager<User> userManager, RoleManager<IdentityRole> roleManager, JwtConfig jwtConfig, IMapper mapper, IMailService mailService)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _jwtConfig = jwtConfig;
            _mapper = mapper;
            _mailService = mailService;
        }

        public async Task<string> CreateTokenAsync()
        {
            var signingCredentials = GetSigningCredentials();
            var claims = await GetClaims();
            var tokenOptions = GenerateTokenOptions(signingCredentials, claims);
            return new JwtSecurityTokenHandler().WriteToken(tokenOptions);
        }

        private SigningCredentials GetSigningCredentials()
        {
            var jwtSecret = _jwtConfig.JwtSecret;
            var key = Encoding.UTF8.GetBytes(jwtSecret);
            var secret = new SymmetricSecurityKey(key);
            return new SigningCredentials(secret, SecurityAlgorithms.HmacSha256);
        }

        private async Task<List<Claim>> GetClaims()
        {
            var rolesUser = await _userManager.GetRolesAsync(_user!);
            var isRoleAdmin = rolesUser.Contains("Admin");

            var isLoginAdminToUser = isRoleAdmin && _userLoginFromAdmin != null;

            var userToUse = isLoginAdminToUser ? _userLoginFromAdmin : _user;

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, userToUse!.UserName!),
                new Claim(ClaimTypes.Email, userToUse.Email!),
                new Claim("id", userToUse.Id),
            };

            var roles = isLoginAdminToUser ? await _userManager.GetRolesAsync(userToUse) : rolesUser;
            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
            claims.Add(new Claim("role", roles[0]));
            return claims;
        }

        private JwtSecurityToken GenerateTokenOptions(SigningCredentials signingCredentials, List<Claim> claims)
        {
            var tokenOptions = new JwtSecurityToken
            (
                issuer: _jwtConfig.JwtConfigName,
                audience: _jwtConfig.JwtConfigName,
                claims: claims,
                expires: DateTime.Now.AddDays(Convert.ToDouble(_jwtConfig.JwtExpiresIn)),
                signingCredentials: signingCredentials
            );
            return tokenOptions;
        }

        private ClaimsPrincipal GetClaimsFromToken(string token)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_jwtConfig.JwtSecret);
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidIssuer = _jwtConfig.JwtConfigName,
                ValidAudience = _jwtConfig.JwtConfigName,
                IssuerSigningKey = new SymmetricSecurityKey(key)
            };

            try
            {
                ClaimsPrincipal claimsPrincipal = tokenHandler.ValidateToken(token, validationParameters, out SecurityToken validatedToken);
                return claimsPrincipal;
            }
            catch (Exception)
            {
                throw;
            }
        }

        public async Task<bool> SendCode(string email)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
                return false;

            _ = _mailService.SendResetMailAsync(email, "Your password reset code is: ", $"Hi {user.FirstName}, <br />We received a request to update your password. <br />Your temporary code to change your password is: <b>CODE</b>");
            return true;
        }

        public bool CheckCode(string email, int code)
        {
            return _mailService.IsCodeCorrect(code, email);
        }

        public async Task<IdentityResult> ResetUserPasswordAsync(ResetPasswordDto changePasswordData)
        {
            var user = await _userManager.FindByEmailAsync(changePasswordData.Email);
            var token = await _userManager.GeneratePasswordResetTokenAsync(user!);
            user!.IsActive = true;
            var result = await _userManager.ResetPasswordAsync(user, token, changePasswordData.Password);
            return result;
        }

        public async Task<IdentityResult> RegisterUserAsync(UserRegistrationDto userRegistration, string role)
        {
            var user = _mapper.Map<User>(userRegistration);
            user.UserName = user.UserName!.ToLower().Trim();
            user.PictureFileName = null;
            user.Address = "";
            user.AccountBalance = 0;
            user.IsActive = false;
            user.DateCreated = DateTime.Now;
            var result = await _userManager.CreateAsync(user, userRegistration.Password!);

            if (!await _roleManager.RoleExistsAsync(role))
                await _roleManager.CreateAsync(new IdentityRole(role));

            if (result.Succeeded && await _roleManager.RoleExistsAsync(role))
            {
                await _userManager.AddToRoleAsync(user, role);
            }

            return result;
        }

        public async Task<IdentityResult> EditUserData(EditUserDto editUser)
        {
            ClaimsPrincipal claimsPrincipal = GetClaimsFromToken(editUser.Token!);
            string userName = claimsPrincipal.FindFirst(ClaimTypes.Name)?.Value!;
            if (!string.IsNullOrEmpty(userName))
            {
                _user = await _userManager.FindByNameAsync(userName);
                _user!.Address = editUser.Address;
                _user.Email = editUser.Email;
                _user.FirstName = editUser.FirstName;
                _user.LastName = editUser.LastName;
                _user.PictureFileName = editUser.PictureFileName;
                _user.EmailFromGroupsOn = editUser.EmailFromGroupsOn;
                _user.EmailFromUsersOn = editUser.EmailFromUsersOn;
                _user.OptOutEmailNotifications = editUser.OptOutEmailNotifications;
                _user.IsApprouveRequired = editUser.IsApprouveRequired;
                _user.IsUserHidden = editUser.IsUserHidden;
                _user.IsAnonymousInvestment = editUser.IsAnonymousInvestment;
                return await _userManager.UpdateAsync(_user);
            }
            else
                return IdentityResult.Failed();
        }

        public async Task<bool> ValidateUserAsync(UserLoginDto userLogin)
        {
            _user = await _userManager.FindByEmailAsync(userLogin.Email);
            if (_user == null)
                _user = await _userManager.FindByNameAsync(userLogin.Email);

            var result = _user != null && await _userManager.CheckPasswordAsync(_user, userLogin.Password) && (_user.IsActive == true || await _userManager.IsInRoleAsync(_user, UserRoles.Admin));
            return result;
        }

        public async Task<bool> ValidateAdminToUserAsync(string userToken, string email)
        {
            _user = await GetUser(userToken);
            _userLoginFromAdmin = !string.IsNullOrEmpty(email)
                ? await _userManager.FindByEmailAsync(email)
                : null;

            return _userLoginFromAdmin != null && _userLoginFromAdmin.IsActive == true && _user != null;
        }

        public async Task<User?> GetUser(string token)
        {
            ClaimsPrincipal claimsPrincipal = GetClaimsFromToken(token);
            string userName = claimsPrincipal.FindFirst(ClaimTypes.Name)?.Value!;

            return await _userManager.FindByNameAsync(userName);
        }

        public async Task<User?> GetUserById(string userId)
        {
            return await _userManager.FindByIdAsync(userId);
        }

        public async Task<User?> GetUserByUserName(string userId)
        {
            return await _userManager.FindByNameAsync(userId);
        }

        public async Task<User?> GetUserByEmail(string email)
        {
            return await _userManager.FindByEmailAsync(email);
        }

        public async Task<IdentityResult> UpdateUser(User user)
        {
            return await _userManager.UpdateAsync(user);
        }
    }
}
