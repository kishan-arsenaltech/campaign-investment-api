using Investment.Core.Dtos;
using Investment.Core.Entities;
using Microsoft.AspNetCore.Identity;

namespace Investment.Service.Interfaces
{
    public interface IUserAuthenticationRepository
    {
        Task<IdentityResult> RegisterUserAsync(UserRegistrationDto userForRegistration, string role);
        Task<bool> ValidateUserAsync(UserLoginDto loginDto);
        Task<bool> ValidateAdminToUserAsync(string userToken, string userEmail);
        Task<string> CreateTokenAsync(User? user = null);
        Task<IdentityResult> ResetUserPasswordAsync(ResetPasswordDto changePasswordData);
        Task<bool> SendCode(string email);
        Task<bool> SendAdminCode(string email);
        bool CheckCode(string email, int code);
        Task<IdentityResult> EditUserData(EditUserDto editUserDto);
        Task<User> GetUser(string token);
        Task<User> GetUserById(string userId);
        Task<User> GetUserByUserName(string userName);
        Task<User> GetUserByEmail(string email);
        Task<IdentityResult> UpdateUser(User user);
    }
}
