// Ignore Spelling: Auth Admin Captcha

using AutoMapper;
using Invest.Core.Dtos;
using Invest.Core.Models;
using Invest.Core.Settings;
using Investment.Core.Constants;
using Investment.Core.Dtos;
using Investment.Core.Entities;
using Investment.Extensions;
using Investment.Repo.Context;
using Investment.Service.Filters.ActionFilters;
using Investment.Service.Interfaces;
using Investment.Service.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;

namespace Invest.Controllers;


[Route("api/userauthentication")]
[ApiController]
public class AuthController : BaseApiController
{
    private readonly RepositoryContext _context;
    private readonly AppSecrets _appSecrets;
    private readonly HttpClient _httpClient;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly UserManager<User> _userManager;
    private readonly EmailQueue _emailQueue;

    public AuthController(RepositoryContext context, IRepositoryManager repository, ILoggerManager logger, IMapper mapper, AppSecrets appSecrets, HttpClient httpClient, RoleManager<ApplicationRole> roleManager, UserManager<User> userManager, EmailQueue emailQueue) : base(repository, logger, mapper)
    {
        _context = context;
        _appSecrets = appSecrets;
        _httpClient = httpClient;
        _roleManager = roleManager;
        _userManager = userManager;
        _emailQueue = emailQueue;
    }

    [AllowAnonymous]
    [HttpPost("register")]
    [ServiceFilter(typeof(ValidationFilterAttribute))]
    public async Task<IActionResult> RegisterUser([FromBody] UserRegistrationDto userRegistration)
    {
        if (!string.IsNullOrEmpty(userRegistration.CaptchaToken))
        {
            if (!await VerifyCaptcha(userRegistration.CaptchaToken))
                return BadRequest("CAPTCHA verification failed.");
        }

        var requestOrigin = HttpContext?.Request.Headers["Origin"].ToString();

        var userResult = await _repository.UserAuthentication.RegisterUserAsync(userRegistration, UserRoles.User);
        if (!userResult.Succeeded)
        {
            bool hasDuplicateUserName = userResult.Errors.Any(e => e.Code == "DuplicateUserName");

            if (userRegistration.IsAnonymous && hasDuplicateUserName)
            {
                var userName = userRegistration?.UserName?.ToLower();
                bool existsUserName = _context.Users.Any(x => x.UserName.ToLower() == userName);
                Random random = new Random();

                while (existsUserName)
                {
                    int randomTwoDigit = random.Next(0, 100);
                    string newUserName = $"{userName}{randomTwoDigit}";

                    existsUserName = _context.Users.Any(x => x.UserName == newUserName);

                    if (!existsUserName)
                    {
                        userName = newUserName;
                    }
                }

                var updatedUserRegistration = new UserRegistrationDto
                {
                    UserName = userName,
                    Password = userRegistration!.Password,
                    Email = userRegistration.Email,
                    FirstName = userRegistration.FirstName,
                    LastName = userRegistration.LastName,
                    IsAnonymous = userRegistration.IsAnonymous
                };

                userResult = await _repository.UserAuthentication.RegisterUserAsync(updatedUserRegistration, UserRoles.User);
            }
            if (!userResult.Succeeded)
            {
                return Ok(new { success = false, errors = userResult.Errors });
            }
        }

        var user = await _context.Users.Where(x => x.Email == userRegistration.Email).FirstOrDefaultAsync();
        user!.IsActive = true;
        user.IsFreeUser = true;
        await _repository.UserAuthentication.UpdateUser(user);

        UserLoginDto userLoginDto = new();
        userLoginDto.Email = userRegistration.Email;
        userLoginDto.Password = userRegistration.Password;
        await _repository.UserAuthentication.ValidateUserAsync(userLoginDto);

        var variables = new Dictionary<string, string>
        {
            { "firstName", userRegistration.FirstName! },
            { "userName", userRegistration.UserName! },
            { "resetPasswordUrl", $"{_appSecrets.RequestOrigin}/forgotpassword" },
            { "siteUrl", _appSecrets.RequestOrigin }
        };

        _emailQueue.QueueEmail(async (sp) =>
        {
            var emailService = sp.GetRequiredService<IEmailTemplateService>();

            await emailService.SendTemplateEmailAsync(
                userRegistration.IsAnonymous
                    ? EmailTemplateCategory.WelcomeAnonymousUser
                    : EmailTemplateCategory.WelcomeRegisteredUser,
                user.Email,
                variables
            );
        });

        return Ok(new { success = true, data = await _repository.UserAuthentication.CreateTokenAsync() });
    }

    [HttpPost("admin/login")]
    [ServiceFilter(typeof(ValidationFilterAttribute))]
    public async Task<IActionResult> AdminAuthenticate([FromBody] UserLoginDto user)
    {
        var isValid = await _repository.UserAuthentication.ValidateUserAsync(user);

        if (!isValid)
            return Unauthorized();

        var dbUser = await _context.Users.FirstOrDefaultAsync(x => x.Email.ToLower() == user.Email || x.UserName.ToLower() == user.Email);

        if (dbUser == null)
            return Unauthorized();

        var roles = await _userManager.GetRolesAsync(dbUser);

        if (roles.Contains(UserRoles.Admin) || roles.Contains(UserRoles.SuperAdmin))
        {
            await _repository.UserAuthentication.SendAdminCode(dbUser.Email);

            return Ok(new { requires2FA = false, email = dbUser.Email });
        }

        return Unauthorized();
    }

    [HttpPost("login")]
    [ServiceFilter(typeof(ValidationFilterAttribute))]
    public async Task<IActionResult> Authenticate([FromBody] UserLoginDto user)
    {
        return !await _repository.UserAuthentication.ValidateUserAsync(user)
            ? Unauthorized()
            : Ok(new { Token = await _repository.UserAuthentication.CreateTokenAsync() });
    }

    public async Task<bool> VerifyCaptcha(string token)
    {
        var requestContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("secret", _appSecrets.CaptchaSecretKey),
            new KeyValuePair<string, string>("response", token)
        });

        var response = await _httpClient.PostAsync("https://hcaptcha.com/siteverify", requestContent);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        bool isSuccess = doc.RootElement.GetProperty("success").GetBoolean();

        return isSuccess;
    }

    [HttpPost("reset-password")]
    [ServiceFilter(typeof(ValidationFilterAttribute))]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto resetPasswordData)
    {
        var userResult = await _repository.UserAuthentication.ResetUserPasswordAsync(resetPasswordData);
        return !userResult.Succeeded ? new BadRequestObjectResult(userResult) : Ok();
    }

    [HttpPost("send-code")]
    [ServiceFilter(typeof(ValidationFilterAttribute))]
    public async Task<IActionResult> SendCode([FromBody] EmailReceiveDto email)
    {
        if (!string.IsNullOrWhiteSpace(email.CaptchaToken))
        {
            if (!await VerifyCaptcha(email.CaptchaToken))
                return BadRequest("CAPTCHA verification failed.");
        }

        if (string.IsNullOrEmpty(email.Email))
            return BadRequest();
        var res = await _repository.UserAuthentication.SendCode(email.Email);
        return StatusCode(200);
    }

    [HttpPost("check-code")]
    [ServiceFilter(typeof(ValidationFilterAttribute))]
    public Task<IActionResult> CheckCode([FromBody] ResetCodeDto resetCode)
    {
        var res = _repository.UserAuthentication.CheckCode(resetCode.Email, resetCode.Code);
        IActionResult result = res ? StatusCode(200) : new NotFoundResult();
        return Task.FromResult(result);
    }

    [HttpPost("verify-2fa")]
    [ServiceFilter(typeof(ValidationFilterAttribute))]
    public async Task<IActionResult> Verify2FA([FromBody] ResetCodeDto resetCode)
    {
        var user = await _repository.UserAuthentication.GetUserByEmail(resetCode.Email);

        if (user == null)
            return Ok(new { success = false, Message = "Verification code is incorrect or has expired. Please request a new code and try again." });

        var isValid = _repository.UserAuthentication.CheckCode(resetCode.Email, resetCode.Code);

        if (!isValid)
            return Ok(new { success = false, Message = "Verification code is incorrect or has expired. Please request a new code and try again." });

        var token = await _repository.UserAuthentication.CreateTokenAsync(user);

        return Ok(new { token });
    }

    [HttpPost("login-admin-to-user")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AuthenticateAdminToUser([FromBody] UserLoginFromAdmin userLoginFromAdmin)
    {
        return await _repository.UserAuthentication
            .ValidateAdminToUserAsync(userLoginFromAdmin.UserToken, userLoginFromAdmin.Email)
                ? Ok(new { Token = await _repository.UserAuthentication.CreateTokenAsync() })
                : BadRequest();
    }
}
