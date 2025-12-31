// Ignore Spelling: klaviyo Api ach Webhook Accessors

using AutoMapper;
using Invest.Core.Dtos;
using Invest.Core.Entities;
using Investment.Core.Dtos;
using Investment.Core.Entities;
using Investment.Repo.Context;
using Investment.Service.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Stripe;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace Invest.Service.Interfaces
{

    public class PaymentService : IPaymentService
    {
        private readonly UserManager<User> _userManager;
        protected readonly IRepositoryManager _repositoryManager;
        private readonly IHttpContextAccessor _httpContextAccessors;
        private readonly CustomerService _customerService;
        private readonly PaymentIntentService _paymentIntentService;
        private readonly PaymentMethodService _paymentMethodService;
        private readonly RepositoryContext _context;
        private readonly SetupIntentService _setupIntentService;
        private readonly IMailService _mailService;
        private readonly HttpClient _httpClient;
        private readonly IMapper _mapper;
        private decimal originalDonationAmount = 0;
        private readonly string defaultPassword = "SEcurE!Pa$$w0rd_#2025";
        private readonly string request;

        public PaymentService(IRepositoryManager repositoryManager, IHttpContextAccessor httpContextAccessors, PaymentIntentService paymentIntentService, PaymentMethodService paymentMethodService, CustomerService customerService, RepositoryContext context, SetupIntentService setupIntentService, IMailService mailService, UserManager<User> userManager, HttpClient httpClient, IMapper mapper)
        {
            _repositoryManager = repositoryManager;
            _httpContextAccessors = httpContextAccessors;
            _customerService = customerService;
            _paymentIntentService = paymentIntentService;
            _paymentMethodService = paymentMethodService;
            _context = context;
            _setupIntentService = setupIntentService;
            _mailService = mailService;
            _userManager = userManager;
            _httpClient = httpClient;
            _mapper = mapper;
            request = _httpContextAccessors.HttpContext?.Request.Headers["Origin"].ToString()!;
        }

        #region ProcessCardPayment

        public async Task<CommonResponse> ProcessCardPayment(CardPayment cardPaymentData, string klaviyoApiKey, string klaviyoListKey, bool isDevelopment)
        {
            CommonResponse response = new();
            try
            {
                bool isAnonymous = cardPaymentData.IsAnonymous;
                var customerId = string.Empty;
                var paymentMethodId = string.Empty;
                var email = string.Empty;

                if (isAnonymous)
                {
                    var customerOptions = new CustomerCreateOptions
                    {
                        Name = $"{cardPaymentData?.FirstName} {cardPaymentData?.LastName}",
                        Email = cardPaymentData?.Email.ToLower()
                    };
                    var customer = await _customerService.CreateAsync(customerOptions);
                    customerId = customer.Id;

                    paymentMethodId = await CreateCardPaymentMethod(customerId, string.Empty, cardPaymentData?.TokenId!, cardPaymentData!.RememberCardDetail, isAnonymous);

                    response = await SaveCardPaymentIntent(cardPaymentData, paymentMethodId, customerId, string.Empty, isAnonymous, klaviyoApiKey, klaviyoListKey, isDevelopment);

                    email = cardPaymentData?.Email.ToLower();
                }
                else
                {
                    var userId = await GetUserId(cardPaymentData.UserName);
                    if (string.IsNullOrEmpty(userId))
                    {
                        response.Success = false;
                        response.Message = "User is not authenticated.";
                        return response;
                    }

                    var user = await _repositoryManager.UserAuthentication.GetUserById(userId);
                    if (user == null)
                    {
                        response.Success = false;
                        response.Message = "User not found.";
                        return response;
                    }

                    customerId = await GetOrCreateCustomerId(userId, user);

                    paymentMethodId = !string.IsNullOrEmpty(cardPaymentData.PaymentMethodId)
                                        ? cardPaymentData.PaymentMethodId
                                        : await CreateCardPaymentMethod(customerId, userId, cardPaymentData.TokenId, cardPaymentData.RememberCardDetail, isAnonymous);

                    response = await SaveCardPaymentIntent(cardPaymentData, paymentMethodId, customerId, userId, isAnonymous, klaviyoApiKey, klaviyoListKey, isDevelopment);

                    email = user?.Email.ToLower();
                }

                if (response.Success)
                {
                    response.Message = "Payment successful.";

                    var user = await _context.Users.FirstOrDefaultAsync(x => x.Email == email);

                    if (user != null)
                        response.Data = _mapper.Map<UserDetailsDto>(user);

                    string? investmentName = string.Empty;
                    if (cardPaymentData?.InvestmentId > 0)
                    {
                        investmentName = await _context.Campaigns.Where(x => x.Id == cardPaymentData.InvestmentId)
                                                                 .Select(x => x.Name)
                                                                 .SingleOrDefaultAsync();
                    }

                    if (user != null && (user.OptOutEmailNotifications == null || !(bool)user.OptOutEmailNotifications))
                        _ = SendDonationReceipt(investmentName, user.Email, user.FirstName!, user.LastName!, originalDonationAmount);
                }
            }
            catch (StripeException ex)
            {
                response.Success = false;
                response.Message = $" Payment gateway error: {ex.Message}";
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = $"An error occurred: {ex.Message}";
            }
            return response;
        }
        private async Task<string> CreateCardPaymentMethod(string customerId, string userId, string tokenId, bool rememberCardDetail, bool isAnonymous)
        {
            var paymentMethodOptions = new PaymentMethodCreateOptions
            {
                Type = "card",
                Card = new PaymentMethodCardOptions
                {
                    Token = tokenId
                }
            };

            var paymentMethod = await _paymentMethodService.CreateAsync(paymentMethodOptions);
            await _paymentMethodService.AttachAsync(paymentMethod.Id, new PaymentMethodAttachOptions
            {
                Customer = customerId,
            });

            if (!isAnonymous && rememberCardDetail == true)
            {
                var customer = _context.UserStripeCustomerMapping
                                .Where(u => u.UserId.ToString() == userId && u.CustomerId == customerId && u.CardDetailToken == string.Empty)
                                .FirstOrDefault();

                if (customer == null)
                {
                    await SaveCustomerData(userId, customerId, paymentMethod.Id, true);
                }
                else
                {
                    customer.CardDetailToken = paymentMethod.Id;
                    _context.UserStripeCustomerMapping.Update(customer);
                }
            }
            return paymentMethod.Id;
        }
        public async Task<CommonResponse> SaveCardPaymentIntent(CardPayment cardPaymentData, string paymentMethodId, string customerId, string userId, bool isAnonymous, string klaviyoApiKey, string klaviyoListKey, bool isDevelopment)
        {
            var userEmail = string.Empty;
            if (!string.IsNullOrEmpty(userId))
            {
                var user = await _repositoryManager.UserAuthentication.GetUserById(userId);
                userEmail = user.Email.ToLower();
            }

            CommonResponse response = new();
            var paymentIntentOptions = new PaymentIntentCreateOptions
            {
                Amount = cardPaymentData?.Amount * 100,
                Currency = "USD",
                CaptureMethod = "automatic",
                PaymentMethod = paymentMethodId,
                Customer = customerId,
                Confirm = true,
                AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
                {
                    Enabled = true,
                    AllowRedirects = "never"
                }
            };
            string requestDataJson = JsonConvert.SerializeObject(paymentIntentOptions);
            requestDataJson = requestDataJson.Replace(Convert.ToString(cardPaymentData?.Amount * 100)!, Convert.ToString(cardPaymentData?.Amount));

            var paymentIntent = await _paymentIntentService.CreateAsync(paymentIntentOptions);

            //await Task.Delay(5000);
            var paymentIntentResponse = _paymentIntentService.Get(paymentIntent.Id);
            if (paymentIntentResponse != null)
            {
                var lastpaymentIntentError = paymentIntentResponse.LastPaymentError;
                if (lastpaymentIntentError != null)
                {
                    response.Message = lastpaymentIntentError.Message;
                    response.Success = false;
                }
                else
                {
                    if (isAnonymous)
                    {
                        var userName = await CreateUser(cardPaymentData!.FirstName, cardPaymentData.LastName, cardPaymentData.Email.ToLower());
                        var newUser = _context.Users.Where(x => x.UserName == userName).FirstOrDefault();

                        userId = newUser!.Id;

                        await SaveCustomerData(userId, customerId, paymentMethodId, cardPaymentData.RememberCardDetail);
                        await UpdateUserBalance(newUser, Convert.ToDecimal(cardPaymentData?.Amount), userName, "Stripe Card", isAnonymous, klaviyoApiKey, klaviyoListKey, isDevelopment, cardPaymentData?.Reference);

                        _ = SendWelcomeEmail(newUser.Email.ToLower(), userName, newUser.FirstName!);
                    }
                    else
                    {
                        var user = await _repositoryManager.UserAuthentication.GetUserById(userId);
                        await UpdateUserBalance(user, Convert.ToDecimal(cardPaymentData?.Amount), cardPaymentData?.UserName!, "Stripe Card", isAnonymous, klaviyoApiKey, klaviyoListKey, isDevelopment, cardPaymentData?.Reference);
                    }
                    response.Success = true;
                }
                await SaveCardTransaction(userId, paymentIntentResponse, requestDataJson, cardPaymentData!);
            }
            return response;
        }
        private async Task SaveCardTransaction(string userId, PaymentIntent paymentIntentData, string requestDataJson, CardPayment cardPaymentData)
        {
            if (paymentIntentData.Status != "succeeded")
            {
                paymentIntentData.Status = "failed";
            }
            string responseDataJson = JsonConvert.SerializeObject(paymentIntentData);
            responseDataJson = responseDataJson.Replace(Convert.ToString(cardPaymentData.Amount * 100), Convert.ToString(cardPaymentData.Amount));

            var transactionMapping = new UserStripeTransactionMapping
            {
                UserId = userId == "" ? null : Guid.Parse(userId),
                TransactionId = paymentIntentData.Id,
                Status = paymentIntentData.Status,
                Amount = cardPaymentData?.Amount,
                Country = cardPaymentData?.BillingDetails?.Country,
                ZipCode = cardPaymentData?.BillingDetails?.ZipCode,
                RequestedData = requestDataJson,
                ResponseData = responseDataJson,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };
            _context.UserStripeTransactionMapping.Add(transactionMapping);
            await _context.SaveChangesAsync();
        }
        public async Task<List<PaymentMethodDetails>> CardPaymentMethods()
        {
            var paymentMethodDetailsList = new List<PaymentMethodDetails>();
            try
            {
                var userId = await GetUserId(string.Empty);
                var customerId = _context.UserStripeCustomerMapping
                                .Where(u => u.UserId.ToString() == userId && u.CardDetailToken != string.Empty)
                                .Select(u => u.CustomerId)
                                .FirstOrDefault();

                if (string.IsNullOrEmpty(customerId))
                {
                    return paymentMethodDetailsList;
                }
                else
                {
                    var stpireCustomer = await _customerService.GetAsync(customerId);

                    if (stpireCustomer.Deleted == true)
                    {
                        var customer = _context.UserStripeCustomerMapping
                                        .Where(u => u.UserId.ToString() == userId)
                                        .ToListAsync();

                        _context.UserStripeCustomerMapping.RemoveRange(await customer);
                        await _context.SaveChangesAsync();
                        return paymentMethodDetailsList;
                    }
                }

                var options = new PaymentMethodListOptions
                {
                    Customer = customerId,
                    Type = "card"
                };
                var paymentMethods = await _paymentMethodService.ListAsync(options);
                if (paymentMethods?.Data?.Count > 0)
                {
                    paymentMethodDetailsList = paymentMethods.Data.Select(pm => new PaymentMethodDetails
                    {
                        Id = pm.Id,
                        Type = pm.Type,
                        Brand = pm.Card.Brand,
                        Last4 = pm.Card.Last4,
                        ExpiryMonth = pm.Card.ExpMonth,
                        ExpiryYear = pm.Card.ExpYear
                    }).ToList();

                    if (paymentMethodDetailsList.Count > 0)
                    {
                        var cardDetailTokenList = _context.UserStripeCustomerMapping
                                                    .Where(u => u.UserId.ToString() == userId && u.CardDetailToken != string.Empty)
                                                    .Select(u => u.CardDetailToken)
                                                    .ToList();

                        paymentMethodDetailsList = paymentMethodDetailsList
                                                   .Where(p => cardDetailTokenList.Contains(p.Id))
                                                   .ToList();
                    }
                }

                return paymentMethodDetailsList;
            }
            catch (StripeException)
            {
                return paymentMethodDetailsList;
            }
            catch (Exception)
            {
                return paymentMethodDetailsList;
            }
        }

        #endregion ProcessCardPayment

        #region ProcessBankPayment

        public async Task<CommonResponse> ACHPaymentSecret(ACHPaymentSecret achPaymentSecretData)
        {
            CommonResponse response = new();
            try
            {
                bool isAnonymous = achPaymentSecretData.IsAnonymous;
                var customerId = string.Empty;

                if (isAnonymous)
                {
                    var customerOptions = new CustomerCreateOptions
                    {
                        Name = $"{achPaymentSecretData?.FirstName} {achPaymentSecretData?.LastName}",
                        Email = achPaymentSecretData?.Email.ToLower()
                    };
                    var customer = await _customerService.CreateAsync(customerOptions);
                    customerId = customer.Id;
                }
                else
                {
                    var userId = await GetUserId(achPaymentSecretData.UserName);
                    if (string.IsNullOrEmpty(userId))
                    {
                        response.Success = false;
                        response.Message = "User is not authenticated.";
                        return response;
                    }

                    var user = await _repositoryManager.UserAuthentication.GetUserById(userId);
                    if (user == null)
                    {
                        response.Success = false;
                        response.Message = "User not found.";
                        return response;
                    }

                    customerId = await GetOrCreateCustomerId(userId, user);
                }

                var setupIntentCreateOptions = new SetupIntentCreateOptions
                {
                    PaymentMethodTypes = new List<string> { "us_bank_account" },
                    Customer = customerId,
                    Metadata = new Dictionary<string, string>
                    {
                        { "intended_amount",  achPaymentSecretData!.Amount.ToString() }
                    }
                };
                var setupIntent = await _setupIntentService.CreateAsync(setupIntentCreateOptions);

                response.Success = true;
                response.Message = setupIntent.ClientSecret.ToString();
            }
            catch (StripeException ex)
            {
                response.Success = false;
                response.Message = $"Payment gateway error: {ex.Message}";
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = $"An error occurred: {ex.Message}";
            }
            return response;
        }
        public async Task<CommonResponse> ProcessBankPayment(BankPayment bankPaymentData, string klaviyoApiKey, string klaviyoListKey, bool isDevelopment)
        {
            CommonResponse response = new();
            try
            {
                bool isAnonymous = bankPaymentData.IsAnonymous;
                string userId = string.Empty;
                var email = string.Empty;

                if (!isAnonymous)
                {
                    userId = await GetUserId(bankPaymentData.UserName);
                    if (string.IsNullOrEmpty(userId))
                    {
                        response.Success = false;
                        response.Message = "User is not authenticated.";
                        return response;
                    }

                    var user = await _repositoryManager.UserAuthentication.GetUserById(userId);
                    if (user == null)
                    {
                        response.Success = false;
                        response.Message = "User not found.";
                        return response;
                    }
                    email = user?.Email.ToLower();
                }
                else
                {
                    email = bankPaymentData?.Email.ToLower();
                }

                var setupIntent = _setupIntentService.Get(bankPaymentData!.setup_intent);
                if (string.IsNullOrEmpty(setupIntent.CustomerId))
                {
                    response.Success = false;
                    response.Message = "CustomerId not found.";
                    return response;
                }
                if (string.IsNullOrEmpty(setupIntent.PaymentMethodId))
                {
                    response.Success = false;
                    response.Message = "PaymentMethodId not found.";
                    return response;
                }

                var customerId = setupIntent.CustomerId;
                var paymentMethodId = setupIntent.PaymentMethodId;
                var paymentAmount = string.Empty;

                if (setupIntent.Metadata.TryGetValue("intended_amount", out var intendedAmountString))
                {
                    paymentAmount = Convert.ToString(intendedAmountString);
                }

                response = await SaveBankPaymentIntent(bankPaymentData, userId, customerId, paymentMethodId, Convert.ToDecimal(paymentAmount), isAnonymous, klaviyoApiKey, klaviyoListKey, isDevelopment);

                if (response.Success)
                {
                    response.Message = "Payment successful.";

                    var user = await _context.Users.FirstOrDefaultAsync(x => x.Email == email);

                    if (user != null)
                        response.Data = _mapper.Map<UserDetailsDto>(user);

                    string? investmentName = string.Empty;
                    if (bankPaymentData?.InvestmentId > 0)
                    {
                        investmentName = await _context.Campaigns.Where(x => x.Id == bankPaymentData.InvestmentId)
                                                                 .Select(x => x.Name)
                                                                 .SingleOrDefaultAsync();
                    }

                    if (user != null && (user.OptOutEmailNotifications == null || !(bool)user.OptOutEmailNotifications))
                        _ = SendDonationReceipt(investmentName, user.Email, user.FirstName!, user.LastName!, originalDonationAmount);
                }
            }
            catch (StripeException ex)
            {
                response.Success = false;
                response.Message = $"Payment gateway error: {ex.Message}";
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = $"An error occurred: {ex.Message}";
            }
            return response;
        }
        public async Task<CommonResponse> SaveBankPaymentIntent(BankPayment bankPaymentData, string userId, string customerId, string paymentMethodId, decimal paymentAmount, bool isAnonymous, string klaviyoApiKey, string klaviyoListKey, bool isDevelopment)
        {
            CommonResponse response = new();
            var paymentMethod = await _paymentMethodService.GetAsync(paymentMethodId);

            if (!string.IsNullOrEmpty(paymentMethod.CustomerId))
            {
                if (paymentMethod.CustomerId != customerId)
                {
                    await _paymentMethodService.DetachAsync(paymentMethod.Id);
                }
            }
            else
            {
                await _paymentMethodService.AttachAsync(paymentMethod.Id, new PaymentMethodAttachOptions
                {
                    Customer = customerId,
                });
            }

            var userEmail = string.Empty;
            if (!string.IsNullOrEmpty(userId))
            {
                var user = await _repositoryManager.UserAuthentication.GetUserById(userId);
                userEmail = user.Email.ToLower();
            }

            var paymentIntentOptions = new PaymentIntentCreateOptions
            {
                Amount = (long?)paymentAmount * 100,
                Currency = "USD",
                CaptureMethod = "automatic",
                PaymentMethod = paymentMethodId,
                Customer = customerId,
                Confirm = true,
                AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
                {
                    Enabled = true,
                    AllowRedirects = "never"
                }
            };
            string requestDataJson = JsonConvert.SerializeObject(paymentIntentOptions);
            requestDataJson = requestDataJson.Replace(Convert.ToString(paymentAmount * 100), Convert.ToString(paymentAmount));

            var paymentIntent = _paymentIntentService.Create(paymentIntentOptions);

            await Task.Delay(30000);
            var paymentIntentResponse = _paymentIntentService.Get(paymentIntent.Id);
            if (paymentIntentResponse != null)
            {
                var lastpaymentIntentError = paymentIntentResponse.LastPaymentError;
                if (lastpaymentIntentError != null)
                {
                    response.Message = lastpaymentIntentError.Message;
                    response.Success = false;
                }
                else
                {
                    if (isAnonymous)
                    {
                        var userName = await CreateUser(bankPaymentData.FirstName, bankPaymentData.LastName, bankPaymentData.Email.ToLower());
                        var newUser = _context.Users.Where(x => x.UserName == userName).FirstOrDefault();

                        userId = newUser!.Id;

                        await SaveCustomerData(userId, customerId, paymentMethodId, false);
                        await UpdateUserBalance(newUser, Convert.ToDecimal(paymentAmount), userName, "Stripe Bank", isAnonymous, klaviyoApiKey, klaviyoListKey, isDevelopment, bankPaymentData.Reference);

                        _ = SendWelcomeEmail(newUser.Email.ToLower(), userName, newUser.FirstName!);
                    }
                    else
                    {
                        var user = await _repositoryManager.UserAuthentication.GetUserById(userId);
                        await UpdateUserBalance(user, Convert.ToDecimal(paymentAmount), bankPaymentData.UserName, "Stripe Bank", isAnonymous, klaviyoApiKey, klaviyoListKey, isDevelopment, bankPaymentData.Reference);
                    }
                    response.Success = true;
                }
            }
            await SaveBankTransaction(userId, paymentIntentResponse ?? paymentIntent, requestDataJson, paymentAmount);
            return response;
        }
        private async Task SaveBankTransaction(string userId, PaymentIntent? paymentIntentData, string requestDataJson, decimal paymentAmount)
        {
            if (paymentIntentData != null && paymentIntentData?.Status != "succeeded")
            {
                paymentIntentData!.Status = "failed";
            }
            string responseDataJson = JsonConvert.SerializeObject(paymentIntentData);
            responseDataJson = responseDataJson.Replace(Convert.ToString(paymentAmount * 100), Convert.ToString(paymentAmount));

            var transactionMapping = new UserStripeTransactionMapping
            {
                UserId = userId == "" ? null : Guid.Parse(userId),
                TransactionId = paymentIntentData?.Id,
                Status = paymentIntentData?.Status ?? "pending",
                Amount = paymentAmount,
                RequestedData = requestDataJson,
                ResponseData = responseDataJson,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };
            _context.UserStripeTransactionMapping.Add(transactionMapping);
            await _context.SaveChangesAsync();
        }
        public async Task WebhookCallForACHPaymentFailed(Charge charge, bool isProduction)
        {
            string paymentIntentId = charge.PaymentIntentId;

            var ustMapping = await _context.UserStripeTransactionMapping.FirstOrDefaultAsync(p => p.TransactionId == paymentIntentId);

            if (ustMapping != null)
            {
                var user = await _context.Users.SingleOrDefaultAsync(x => x.Id == ustMapping.UserId.ToString());

                ustMapping.Status = charge.Status;
                ustMapping.WebhookExecutionDate = DateTime.Now;
                ustMapping.WebhookStatus = string.IsNullOrEmpty(charge.FailureMessage) ? charge.Status : charge.FailureMessage;
                ustMapping.WebhookResponseData = JsonConvert.SerializeObject(charge);

                await _context.SaveChangesAsync();

                if (isProduction)
                {
                    decimal amount = Convert.ToDecimal(charge.Amount) * 0.01m;
                    await SendTransactionFailedEmail(user?.FirstName!, user?.LastName!, amount);
                }
            }
        }

        #endregion ProcessBankPayment

        #region SaveCustomerData

        private async Task SaveCustomerData(string userId, string customerId, string paymentMethodId, bool RememberCardDetail)
        {
            var customerMapping = new UserStripeCustomerMapping
            {
                UserId = Guid.Parse(userId),
                CustomerId = customerId,
            };
            if (RememberCardDetail)
            {
                customerMapping.CardDetailToken = paymentMethodId;
            }
            _context.UserStripeCustomerMapping.Add(customerMapping);
            await _context.SaveChangesAsync();
        }

        #endregion SaveCustomerData

        #region SendEmail

        private async Task SendWelcomeEmail(string emailTo, string userName, string firstName)
        {
            string logoUrl = $"{request}/logo-for-email.png";
            string logoHtml = $@"
                                <div style='text-align: center;'>
                                    <a href='https://www.google.com/' target='_blank'>
                                        <img src='{logoUrl}' alt='Logo' width='300' height='150' />
                                    </a>
                                </div>";

            string resetPasswordUrl = $"{request}/forgotpassword";
            string userSettingsUrl = $"{request}/settings";
            string subject = "Welcome to Investment Campaign - Let’s Move Capital That Matters 💥";

            var body = logoHtml + $@"
                                    <html>
                                        <body>
                                            <p><b>Hi {firstName},</b></p>
                                            <p>Welcome to <b>Investment Campaign</b> - the movement turning philanthropic dollars into <b>powerful, catalytic investments</b> that fuel real change.</p>
                                            <p>You’ve just joined what we believe will become the <b>largest community of catalytic capital champions</b> on the planet. Whether you're a donor, funder, or impact-curious investor - you're in the right place.</p>
                                            <p>Your Investment Campaign username: <b>{userName}</b></p>
                                            <p>To set your password: <a href='{resetPasswordUrl}' target='_blank'>Click here</a></p>
                                            <p>Here’s what you can do right now on Investment Campaign:</p>
                                            <p>🔎 <b>1. Discover Investments Aligned with Your Values</b></p>
                                            <p style='margin-bottom: 0px;'>Use your <b>DAF, foundation, or donation capital</b> to fund vetted companies, VC funds, and loan structures — not just nonprofits.</p>
                                            <p style='margin-top: 0px;'>➡️ <a href='{request}/find'>Browse live investment opportunities</a></p>
                                            <p>🤝 <b>2. Connect with Like-Minded Peers</b></p>
                                            <p style='margin-bottom: 0px;'>Follow friends and colleagues, share opportunities, or keep your giving private — you’re in control.</p>
                                            <p style='margin-top: 0px;'>➡️ <a href='{request}/community'>Explore the Investment Campaign community</a></p>
                                            <p>🗣️ <b>3. Join or Start a Group</b></p>
                                            <p style='margin-bottom: 0px;'>Find (or create!) groups around shared causes and funding themes — amplify what matters to you.</p>
                                            <p style='margin-top: 0px;'>➡️ <a href='{request}/community'>See active groups and start your own</a></p>
                                            <p>🚀 <b>4. Recommend Deals You Believe In</b></p>
                                            <p style='margin-bottom: 0px;'>Champion investments that should be seen — and funded — by others in the community.</p>
                                            <p style='margin-top: 0px;'>➡️ <a href='https://investment-campaign.org/lead-investor/'>Propose an opportunity</a></p>
                                            <p>We’re here to help you put your capital to work — boldly, effectively, and in community.</p>
                                            <p>Thanks for joining us. Let’s fund what we wish existed — together.</p>
                                            <p style='margin-bottom: 0px;'><b>The Investment Campaign Team</b></p>
                                            <p style='margin-top: 0px;'>🌍 <a href='https://investment-campaign.org/'>investment-campaign.org</a> | 💼 <a href='https://www.linkedin.com/company/investment-campaign-us/'>Follow us on LinkedIn</a></p>
                                            <p>Have questions? Email Shabbir at <a href='mailto:investment.campaign@mailinator.com'>investment.campaign@mailinator.com</a></p>
                                            <p><a href='{request}/settings' target='_blank'>Unsubscribe</a> from Investment Campaign notifications.</p>
                                        </body>
                                    </html>";

            await _mailService.SendMailAsync(emailTo, subject, "", body);
        }
        private async Task SendDonationReceipt(string? investmentName, string emailTo, string firstName, string lastName, decimal amount)
        {
            string formattedAmount = string.Format(System.Globalization.CultureInfo.GetCultureInfo("en-US"), "${0:N2}", amount);
            string formattedDate = DateTime.Now.ToString("MM/dd/yyyy");
            string subject = "Thank You for Your Donation - Let’s Turn It Into Impact (Receipt Attached)";

            string logoUrl = $"{request}/logo-for-email.png";
            string logoHtml = $@"
                                <div style='text-align: center;'>
                                    <a href='https://investment-campaign.org' target='_blank'>
                                        <img src='{logoUrl}' alt='Logo' width='300' height='150' />
                                    </a>
                                </div>";

            string investmentScenarios = investmentName != string.Empty ? investmentName! : "Investment Campaign";

            var template = logoHtml + $@"
                                    <html>
                                        <body>
                                            <p><b>Hi {firstName},</b></p>
                                            <p>Thank you for your generous, tax-deductible donation of <b>{formattedAmount}</b> to the {investmentScenarios} in support of impact investing.</p>
                                            <p>This isn’t just a donation — it’s a bold step toward moving capital where it’s needed most. Your gift helps unlock catalytic funding for founders building real solutions to the world’s biggest challenges.</p>
                                            <p><b>You’re not just giving. You’re investing in what should exist.</b></p>
                                            <p>Ready to activate your donation?</p>
                                            <p><a href='{request}/find'>Start exploring investment opportunities</a></p>
                                            <div style='margin-bottom: 20px; margin-top: 20px;'><hr></div>
                                            <p><div style='font-size: 17px;'><b>Your Donation Receipt</b></div></p>
                                            <ul style='list-style-type:disc;'>
                                                <li><b>Amount:</b> {formattedAmount}</li>
                                                <li><b>Date:</b> {formattedDate}</li>
                                                <li><b>EIN:</b> 86-2370923</li>
                                                <li><b>Investment Campaign</b></li>
                                            </ul>
                                            <p style='margin-bottom: 0px; margin-top: 0px;'>213 West 85th Street, New York, NY 10024</p>
                                            <p style='margin-top: 0px;'>San Francisco, CA 94147</p>
                                            <p>Please keep this email for your records.</p>
                                            <div style='margin-bottom: 20px; margin-top: 20px;'><hr></div>
                                            <p>Thank you again for being part of this growing movement to reimagine how capital can drive lasting change. We're honored to partner with you.</p>
                                            <p style='margin-bottom: 0px;'><b>Let’s keep building — together.</b></p>
                                            <p style='margin-bottom: 0px; margin-top: 0px;'>– Shabbir Bharmal</p>
                                            <p style='margin-top: 0px;'>Co-Founder, Investment Campaign</p>
                                            <p style='margin-bottom: 0px;'>🌍 <a href='https://investment-campaign.org/'>investment-campaign.org</a> | 💼 <a href='https://www.linkedin.com/company/investment-campaign-us/'>Follow us on LinkedIn</a></p>
                                            <p style='margin-top: 0px;'><a href='{request}/settings' target='_blank'>Unsubscribe</a> from Investment Campaign notifications.</p>
                                        </body>
                                    </html>";

            await _mailService.SendMailAsync(emailTo, subject, "", template);
        }
        private async Task SendTransactionFailedEmail(string firstName, string lastName, decimal amount)
        {
            string formattedAmount = $"${Convert.ToDecimal(amount):N2}";
            string logoUrl = $"{request}/logo-for-email.png";
            string logoHtml = $@"
                                <div style='text-align: center;'>
                                    <a href='https://investment-campaign.org' target='_blank'>
                                        <img src='{logoUrl}' alt='Logo' width='300' height='150' />
                                    </a>
                                </div>";

            string subject = "Failed ACH/ bank transfer notification";

            var body = logoHtml + $@"
                                    <html>
                                        <body>
                                            <p>Hello Team,</p>
                                            <p>We have a bank transfer (ACH) donation that failed from Stripe: {firstName} {lastName}, {formattedAmount}.</p>
                                            <p>Thanks!</p>
                                        </body>
                                    </html>";

            await _mailService.SendMailAsync("investment.campaign@mailinator.com", subject, "", body);
        }

        #endregion SendEmail

        #region GetOrCreateCustomer

        private async Task<string> GetOrCreateCustomerId(string userId, User user)
        {
            var customerId = _context.UserStripeCustomerMapping
                            .Where(u => u.UserId.ToString() == userId)
                            .OrderByDescending(u => u.Id)
                            .Select(u => u.CustomerId)
                            .FirstOrDefault();

            if (!string.IsNullOrEmpty(customerId))
            {
                try
                {
                    var customer = await _customerService.GetAsync(customerId);
                    if (!(customer.Deleted ?? false))
                    {
                        return customerId;
                    }
                }
                catch (Exception)
                {
                }
            }
            return await CreateCustomer(user);
        }
        private async Task<string> CreateCustomer(User user)
        {
            var customerOptions = new CustomerCreateOptions
            {
                Name = $"{user?.FirstName} {user?.LastName}",
                Email = user?.Email.ToLower(),
            };
            var customer = await _customerService.CreateAsync(customerOptions);

            var customerMapping = new UserStripeCustomerMapping
            {
                UserId = Guid.Parse(user!.Id),
                CustomerId = customer.Id,
                CardDetailToken = string.Empty
            };
            _context.UserStripeCustomerMapping.Add(customerMapping);
            await _context.SaveChangesAsync();
            return customer.Id;
        }

        #endregion GetOrCreateCustomer

        #region CreateUser

        private async Task<string> CreateUser(string firstName, string lastName, string email)
        {
            var userName = ((firstName).Trim() + (lastName).Trim()).ToString().ToLower();
            if (!string.IsNullOrEmpty(userName))
            {
                bool existsUserName = _context.Users.Any(x => x.UserName == userName);
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
            }

            UserRegistrationDto registrationDto = new UserRegistrationDto()
            {
                FirstName = firstName,
                LastName = lastName,
                UserName = userName,
                Password = defaultPassword,
                Email = email
            };
            await _repositoryManager.UserAuthentication.RegisterUserAsync(registrationDto, UserRoles.User);

            return userName;
        }

        #endregion CreateUser

        #region GetUserId

        private async Task<string> GetUserId(string userName)
        {
            var userId = string.Empty;
            if (!string.IsNullOrEmpty(userName))
            {
                var user = await _repositoryManager.UserAuthentication.GetUserByUserName(userName.ToLower());
                userId = user.Id;
            }
            else
            {
                var identity = _httpContextAccessors.HttpContext?.User.Identity as ClaimsIdentity;
                userId = identity?.Claims.FirstOrDefault(i => i.Type == "id")?.Value;
            }
            return userId!;
        }

        #endregion GetUserId

        #region UpdateUserBalance

        private async Task UpdateUserBalance(User user, decimal amount, string usernameFromResource, string paymentType, bool isAnonymous, string klaviyoApiKey, string klaviyoListKey, bool isDevelopment, string? reference = null)
        {
            if (!isDevelopment)
            {
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Klaviyo-API-Key {klaviyoApiKey}");
                _httpClient.DefaultRequestHeaders.Add("revision", "2024-05-15");

                if (isAnonymous)
                {
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
                                    isFreeUser = false,
                                    name = user.FirstName + " " + user.LastName
                                }
                            }
                        }
                    };

                    var json = System.Text.Json.JsonSerializer.Serialize(payload);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var klaviyoResponse = await _httpClient.PostAsync("https://a.klaviyo.com/api/profiles/", content);
                    var responseContent = await klaviyoResponse.Content.ReadAsStringAsync();

                    if (klaviyoResponse.IsSuccessStatusCode)
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
                        var listJson = System.Text.Json.JsonSerializer.Serialize(listPayload);
                        var listContent = new StringContent(listJson, Encoding.UTF8, "application/json");
                        string url = $"https://a.klaviyo.com/api/lists/{klaviyoListKey}/relationships/profiles/";

                        await _httpClient.PostAsync(url, listContent);
                    }
                }
                else
                {
                    if (user?.IsFreeUser == true && !string.IsNullOrEmpty(user.KlaviyoProfileId))
                    {
                        var updatePayload = new
                        {
                            data = new
                            {
                                type = "profile",
                                id = user.KlaviyoProfileId,
                                attributes = new
                                {
                                    properties = new
                                    {
                                        isFreeUser = false
                                    }
                                }
                            }
                        };
                        var updateJson = System.Text.Json.JsonSerializer.Serialize(updatePayload);
                        var updateContent = new StringContent(updateJson, Encoding.UTF8, "application/json");

                        await _httpClient.PatchAsync("https://a.klaviyo.com/api/profiles/" + user.KlaviyoProfileId + "/", updateContent);
                    }
                }
            }

            originalDonationAmount = amount;
            if (paymentType == "Stripe Card")
            {
                decimal totalCardStripeFee = (amount * 0.022m) + 0.30m; //Stripe Card Fee
                decimal totalInvestmentCampaignFee = (amount * 0.05m); //InvestmentCampaign Fee

                amount -= totalCardStripeFee + totalInvestmentCampaignFee;
            }
            else if (paymentType == "Stripe Bank")
            {
                decimal totalBankStripeFee = (amount * 0.008m); //Stripe Bank Fee
                decimal totalInvestmentCampaignFee = (amount * 0.05m); //InvestmentCampaign Fee

                if (totalBankStripeFee > 5.0m)
                    totalBankStripeFee = 5.0m;

                amount -= totalBankStripeFee + totalInvestmentCampaignFee;
            }

            var accountBalanceChangeLog = new AccountBalanceChangeLog
            {
                UserId = user!.Id,
                PaymentType = paymentType,
                OldValue = user.AccountBalance,
                UserName = user.UserName,
                NewValue = user.AccountBalance + amount,
                Reference = !string.IsNullOrWhiteSpace(reference) ? reference : null
            };
            _context.AccountBalanceChangeLogs.Add(accountBalanceChangeLog);
            await _context.SaveChangesAsync();

            user.AccountBalance += amount;
            if (!string.IsNullOrEmpty(usernameFromResource) && user.IsActive == false)
            {
                user.IsActive = true;
            }
            if (user.IsFreeUser == true)
            {
                user.IsFreeUser = false;
            }
            await _repositoryManager.UserAuthentication.UpdateUser(user);
            await _repositoryManager.SaveAsync();
        }

        #endregion UpdateUserBalance

        #region ValidateDuplicateEmail

        public async Task<CommonResponse> ValidateDuplicateEmail(string email)
        {
            CommonResponse response = new();
            if (!string.IsNullOrEmpty(email))
            {
                var existingEmail = await _context.Users.Where(u => u.Email.ToLower() == email.ToLower()).AnyAsync();

                if (existingEmail)
                {
                    response.Success = false;
                    response.Message = "Duplicate email exists";
                }
                else
                {
                    response.Success = true;
                    response.Message = "Duplicate email not exists";
                }
            }
            return response;
        }

        #endregion ValidateDuplicateEmail
    }
}
