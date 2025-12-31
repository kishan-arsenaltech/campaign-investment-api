// Ignore Spelling: Auth Admin Captcha

using AutoMapper;
using Invest.Core.Dtos;
using Investment.Core.Dtos;
using Investment.Core.Entities;
using Investment.Extensions;
using Investment.Repo.Context;
using Investment.Service.Filters.ActionFilters;
using Investment.Service.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace Invest.Controllers;


[Route("api/userauthentication")]
[ApiController]
public class AuthController : BaseApiController
{
    private readonly IMailService _mailService;
    private readonly RepositoryContext _context;
    private readonly KeyVaultConfigService _keyVaultConfigService;
    private readonly HttpClient _httpClient;
    private readonly IWebHostEnvironment _environment;

    public AuthController(RepositoryContext context, IRepositoryManager repository, ILoggerManager logger, IMapper mapper, IMailService mailService, KeyVaultConfigService keyVaultConfigService, HttpClient httpClient, IWebHostEnvironment environment) : base(repository, logger, mapper)
    {
        _mailService = mailService;
        _context = context;
        _keyVaultConfigService = keyVaultConfigService;
        _httpClient = httpClient;
        _environment = environment;
    }

    private async Task AddUserToKlaviyo(User user)
    {
        string klaviyoApiKey = _keyVaultConfigService.GetKlaviyoApiKey();
        string klaviyoListKey = _keyVaultConfigService.GetKlaviyoListKey();

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Klaviyo-API-Key {klaviyoApiKey}");
        _httpClient.DefaultRequestHeaders.Add("revision", "2024-05-15");

        var payload = new
        {
            data = new
            {
                type = "profile",
                attributes = new
                {
                    email = user.Email,
                    first_name = user.FirstName,
                    last_name = user.LastName,
                    properties = new
                    {
                        isFreeUser = user.IsFreeUser,
                        name = user.FirstName + " " + user.LastName
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("https://a.klaviyo.com/api/profiles/", content);
        var responseContent = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            using var doc = JsonDocument.Parse(responseContent);
            var profileId = doc.RootElement.GetProperty("data").GetProperty("id").GetString();

            user.KlaviyoProfileId = profileId;

            var listPayload = new
            {
                data = new[]
                {
                    new
                    {
                        type = "profile",
                        id = profileId
                    }
                }
            };
            var listJson = JsonSerializer.Serialize(listPayload);
            var listContent = new StringContent(listJson, Encoding.UTF8, "application/json");
            string url = $"https://a.klaviyo.com/api/lists/{klaviyoListKey}/relationships/profiles/";

            await _httpClient.PostAsync(url, listContent);
        }
        await _repository.SaveAsync();
    }

    [AllowAnonymous]
    [HttpPost("register")]
    [ServiceFilter(typeof(ValidationFilterAttribute))]
    public async Task<IActionResult> RegisterUser([FromBody] UserRegistrationDto userRegistration, bool sendEmail = true, bool activate = true)
    {
        if (!string.IsNullOrEmpty(userRegistration.CaptchaToken))
        {
            if (!await VerifyCaptcha(userRegistration.CaptchaToken))
                return BadRequest("CAPTCHA verification failed.");
        }

        var allEmailTasks = new List<Task>();
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

        var user = _context.Users.Where(x => x.Email == userRegistration.Email).FirstOrDefault();
        user!.IsActive = activate;
        user.IsFreeUser = true;
        await _repository.UserAuthentication.UpdateUser(user);

        if (_environment.EnvironmentName == "Production")
            await AddUserToKlaviyo(user);

        UserLoginDto userLoginDto = new();
        userLoginDto.Email = userRegistration.Email;
        userLoginDto.Password = userRegistration.Password;
        await _repository.UserAuthentication.ValidateUserAsync(userLoginDto);

        _ = Task.Run(async () =>
        {
            string subject = "Welcome - Let’s Move Capital That Matters 💥";

            if (sendEmail)
            {
                var body = $@"
                            <html>
                                <body>
                                    <p><b>Hi {userRegistration.FirstName},</b></p>
                                    <p>Welcome to - the movement turning philanthropic dollars into <b>powerful, catalytic investments</b> that fuel real change.</p>
                                    <p>You’ve just joined what we believe will become the <b>largest community of catalytic capital champions</b> on the planet. Whether you're a donor, funder, or impact-curious investor - you're in the right place.</p>
                                    <p>Here’s what you can do right now:</p>
                                    <p>🔎 <b>1. Discover Investments Aligned with Your Values</b></p>
                                    <p style='margin-bottom: 0px;'>Use your <b>DAF, foundation, or donation capital</b> to fund vetted companies, VC funds, and loan structures — not just nonprofits.</p>
                                    <p style='margin-top: 0px;'>➡️ <a href='{requestOrigin}/find'>Browse live investment opportunities</a></p>
                                    <p>🤝 <b>2. Connect with Like-Minded Peers</b></p>
                                    <p style='margin-bottom: 0px;'>Follow friends and colleagues, share opportunities, or keep your giving private — you’re in control.</p>
                                    <p>🗣️ <b>3. Join or Start a Group</b></p>
                                    <p style='margin-bottom: 0px;'>Find (or create!) groups around shared causes and funding themes — amplify what matters to you.</p>
                                    <p style='margin-top: 0px;'>➡️ <a href='{requestOrigin}/community'>See active groups and start your own</a></p>
                                    <p>🚀 <b>4. Recommend Deals You Believe In</b></p>
                                    <p style='margin-bottom: 0px;'>Champion investments that should be seen — and funded — by others in the community.</p>
                                    <p>We’re here to help you put your capital to work — boldly, effectively, and in community.</p>
                                    <p>Thanks for joining us. Let’s fund what we wish existed — together.</p>
                                    <p><a href='{requestOrigin}/settings' target='_blank'>Unsubscribe</a> from notifications.</p>
                                </body>
                            </html>";

                allEmailTasks.Add(_mailService.SendMailAsync(user.Email, subject, "", body));

                if (userRegistration.IsAnonymous)
                {
                    string resetPasswordUrl = $"{requestOrigin}/forgotpassword";
                    string userSettingsUrl = $"{requestOrigin}/settings";

                    var template = $@"
                                    <html>
                                        <body>
                                            <p><b>Hi {userRegistration.FirstName},</b></p>
                                            <p>Welcome to - the movement turning philanthropic dollars into <b>powerful, catalytic investments</b> that fuel real change.</p>
                                            <p>You’ve just joined what we believe will become the <b>largest community of catalytic capital champions</b> on the planet. Whether you're a donor, funder, or impact-curious investor - you're in the right place.</p>
                                            <p>Your username: <b>{userRegistration.UserName}</b></p>
                                            <p>To set your password: <a href='{resetPasswordUrl}' target='_blank'>Click here</a></p>
                                            <p>Here’s what you can do right now:</p>
                                            <p>🔎 <b>1. Discover Investments Aligned with Your Values</b></p>
                                            <p style='margin-bottom: 0px;'>Use your <b>DAF, foundation, or donation capital</b> to fund vetted companies, VC funds, and loan structures — not just nonprofits.</p>
                                            <p style='margin-top: 0px;'>➡️ <a href='{requestOrigin}/find'>Browse live investment opportunities</a></p>
                                            <p>🤝 <b>2. Connect with Like-Minded Peers</b></p>
                                            <p style='margin-bottom: 0px;'>Follow friends and colleagues, share opportunities, or keep your giving private — you’re in control.</p>
                                            <p style='margin-top: 0px;'>➡️ <a href='{requestOrigin}/community'>Explore the community</a></p>
                                            <p>🗣️ <b>3. Join or Start a Group</b></p>
                                            <p style='margin-bottom: 0px;'>Find (or create!) groups around shared causes and funding themes — amplify what matters to you.</p>
                                            <p style='margin-top: 0px;'>➡️ <a href='{requestOrigin}/community'>See active groups and start your own</a></p>
                                            <p>🚀 <b>4. Recommend Deals You Believe In</b></p>
                                            <p style='margin-bottom: 0px;'>Champion investments that should be seen — and funded — by others in the community.</p>
                                            <p>We’re here to help you put your capital to work — boldly, effectively, and in community.</p>
                                            <p>Thanks for joining us. Let’s fund what we wish existed — together.</p>
                                            <p><a href='{requestOrigin}/settings' target='_blank'>Unsubscribe</a> from notifications.</p>
                                        </body>
                                    </html>";

                    allEmailTasks.Add(_mailService.SendMailAsync(user.Email, subject, "", template));
                }
            }

            await Task.WhenAll(allEmailTasks);
        });

        return Ok(new { success = true, data = await _repository.UserAuthentication.CreateTokenAsync() });
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
        string captchaSecretKey = _keyVaultConfigService.GetCaptchaSecretKey();

        var requestContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("secret", captchaSecretKey),
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
        if (!await VerifyCaptcha(email.CaptchaToken!))
            return BadRequest("CAPTCHA verification failed.");

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
