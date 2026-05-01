// Ignore Spelling: klaviyo Api ach Webhook Accessors

using AutoMapper;
using Invest.Core.Dtos;
using Invest.Core.Entities;
using Invest.Core.Settings;
using Investment.Core.Constants;
using Investment.Core.Dtos;
using Investment.Core.Entities;
using Investment.Repo.Context;
using Investment.Service.Interfaces;
using Investment.Service.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Stripe;
using System.Security.Claims;

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
        private readonly EmailQueue _emailQueue;
        private readonly IMapper _mapper;
        private readonly AppSecrets _appSecrets;
        private decimal originalDonationAmount = 0;

        public PaymentService(IRepositoryManager repositoryManager, IHttpContextAccessor httpContextAccessors, PaymentIntentService paymentIntentService, PaymentMethodService paymentMethodService, CustomerService customerService, RepositoryContext context, SetupIntentService setupIntentService, EmailQueue emailQueue, UserManager<User> userManager, IMapper mapper, AppSecrets appSecrets)
        {
            _repositoryManager = repositoryManager;
            _httpContextAccessors = httpContextAccessors;
            _customerService = customerService;
            _paymentIntentService = paymentIntentService;
            _paymentMethodService = paymentMethodService;
            _context = context;
            _setupIntentService = setupIntentService;
            _emailQueue = emailQueue;
            _userManager = userManager;
            _mapper = mapper;
            _appSecrets = appSecrets;
        }

        #region ProcessCardPayment

        public async Task<CommonResponse> ProcessCardPayment(CardPayment cardPaymentData)
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

                    response = await SaveCardPaymentIntent(cardPaymentData, paymentMethodId, customerId, string.Empty, isAnonymous);

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

                    response = await SaveCardPaymentIntent(cardPaymentData, paymentMethodId, customerId, userId, isAnonymous);

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
                    {
                        string formattedDate = DateTime.Now.ToString("MM/dd/yyyy");
                        string formattedAmount = string.Format(System.Globalization.CultureInfo.GetCultureInfo("en-US"), "${0:N2}", originalDonationAmount);

                        var variables = new Dictionary<string, string>
                        {
                            { "firstName", user.FirstName! },
                            { "amount", formattedAmount },
                            { "investmentName", investmentName ?? "Investment" },
                            { "date", formattedDate },
                            { "exploreInvestmentsUrl", $"{_appSecrets.RequestOrigin}/investments" },
                            { "unsubscribeUrl", $"{_appSecrets.RequestOrigin}/settings" }
                        };

                        _emailQueue.QueueEmail(async (sp) =>
                        {
                            var emailService = sp.GetRequiredService<IEmailTemplateService>();

                            await emailService.SendTemplateEmailAsync(
                                EmailTemplateCategory.DonationReceipt,
                                user.Email,
                                variables
                            );
                        });
                    }
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
        public async Task<CommonResponse> SaveCardPaymentIntent(CardPayment cardPaymentData, string paymentMethodId, string customerId, string userId, bool isAnonymous)
        {
            decimal amount = cardPaymentData.CoverFees ? cardPaymentData.InvestmentAmountWithFees : cardPaymentData.Amount;
            originalDonationAmount = amount;

            CommonResponse response = new();

            if (amount >= 5000)
            {
                var paymentMethodUpdateOptions = new PaymentMethodUpdateOptions
                {
                    BillingDetails = new PaymentMethodBillingDetailsOptions
                    {
                        Name = $"{cardPaymentData.FirstName} {cardPaymentData.LastName}",
                        Email = cardPaymentData.Email.ToLower(),
                        Address = new AddressOptions
                        {
                            Line1 = cardPaymentData.Address?.Street,
                            City = cardPaymentData.Address?.City,
                            State = cardPaymentData.Address?.State,
                            Country = cardPaymentData.Address?.Country,
                            PostalCode = cardPaymentData.Address?.ZipCode,
                        }
                    }
                };
                await _paymentMethodService.UpdateAsync(paymentMethodId, paymentMethodUpdateOptions);
            }

            var paymentIntentOptions = new PaymentIntentCreateOptions
            {
                Amount = (long)(amount * 100),
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

            var paymentIntent = await _paymentIntentService.CreateAsync(paymentIntentOptions);

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
                        await UpdateUserBalance(newUser, cardPaymentData.Amount, cardPaymentData.CoverFees, userName, "Stripe Card", cardPaymentData?.Reference, cardPaymentData?.Address?.ZipCode);

                        var variables = new Dictionary<string, string>
                        {
                            { "firstName", newUser.FirstName! },
                            { "userName", userName },
                            { "resetPasswordUrl", $"{_appSecrets.RequestOrigin}/forgotpassword" },
                            { "siteUrl", _appSecrets.RequestOrigin }
                        };

                        _emailQueue.QueueEmail(async (sp) =>
                        {
                            var emailService = sp.GetRequiredService<IEmailTemplateService>();

                            await emailService.SendTemplateEmailAsync(
                                EmailTemplateCategory.WelcomeAnonymousUser,
                                newUser.Email,
                                variables
                            );
                        });
                    }
                    else
                    {
                        var user = await _repositoryManager.UserAuthentication.GetUserById(userId);
                        await UpdateUserBalance(user, cardPaymentData.Amount, cardPaymentData.CoverFees, cardPaymentData?.UserName!, "Stripe Card", cardPaymentData?.Reference, cardPaymentData?.Address?.ZipCode);
                    }
                    response.Success = true;
                }
                await SaveCardTransaction(userId, paymentIntentResponse, requestDataJson, cardPaymentData!);
            }
            return response;
        }
        private async Task SaveCardTransaction(string userId, PaymentIntent paymentIntentData, string requestDataJson, CardPayment cardPaymentData)
        {
            decimal amount = cardPaymentData.CoverFees ? cardPaymentData.InvestmentAmountWithFees : cardPaymentData.Amount;

            if (paymentIntentData.Status != "succeeded")
                paymentIntentData.Status = "failed";

            string responseDataJson = JsonConvert.SerializeObject(paymentIntentData);

            var transactionMapping = new UserStripeTransactionMapping
            {
                UserId = userId == "" ? null : Guid.Parse(userId),
                TransactionId = paymentIntentData.Id,
                Status = paymentIntentData.Status,
                Amount = amount,
                Country = cardPaymentData?.Address?.Country,
                ZipCode = cardPaymentData?.Address?.ZipCode,
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
                        { "intended_amount",  achPaymentSecretData!.Amount.ToString() },
                        { "amount_with_fees", achPaymentSecretData.InvestmentAmountWithFees.ToString() },
                        { "cover_fees", achPaymentSecretData.CoverFees.ToString() }
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
        public async Task<CommonResponse> ProcessBankPayment(BankPayment bankPaymentData)
        {
            CommonResponse response = new();
            try
            {
                bool isAnonymous = bankPaymentData.IsAnonymous;
                string? userId = string.Empty;
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
                decimal paymentAmount = 0m;
                decimal investmentAmountWithFees = 0m;
                bool isCoverFees = false;

                if (setupIntent.Metadata.TryGetValue("intended_amount", out var intendedAmountString))
                    paymentAmount = Convert.ToDecimal(intendedAmountString);

                if (setupIntent.Metadata.TryGetValue("amount_with_fees", out var amountWithFees))
                    investmentAmountWithFees = Convert.ToDecimal(amountWithFees);

                if (setupIntent.Metadata.TryGetValue("cover_fees", out var coverFees))
                    isCoverFees = Convert.ToBoolean(coverFees);

                decimal amount = paymentAmount;
                paymentAmount = isCoverFees ? investmentAmountWithFees : paymentAmount;
                originalDonationAmount = paymentAmount;

                response = await SaveBankPaymentIntent(bankPaymentData, userId, isCoverFees, customerId, paymentMethodId, paymentAmount, amount, isAnonymous);

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
                    {
                        string formattedDate = DateTime.Now.ToString("MM/dd/yyyy");
                        string formattedAmount = string.Format(System.Globalization.CultureInfo.GetCultureInfo("en-US"), "${0:N2}", originalDonationAmount);

                        var variables = new Dictionary<string, string>
                        {
                            { "firstName", user.FirstName! },
                            { "amount", formattedAmount },
                            { "investmentName", investmentName ?? "Investment" },
                            { "date", formattedDate },
                            { "exploreInvestmentsUrl", $"{_appSecrets.RequestOrigin}/investments" },
                            { "unsubscribeUrl", $"{_appSecrets.RequestOrigin}/settings" }
                        };

                        _emailQueue.QueueEmail(async (sp) =>
                        {
                            var emailService = sp.GetRequiredService<IEmailTemplateService>();

                            await emailService.SendTemplateEmailAsync(
                                EmailTemplateCategory.DonationReceipt,
                                user.Email,
                                variables
                            );
                        });
                    }
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
        public async Task<CommonResponse> SaveBankPaymentIntent(BankPayment bankPaymentData, string userId, bool coverFees, string customerId, string paymentMethodId, decimal paymentAmount, decimal amount, bool isAnonymous)
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

            if (paymentAmount >= 5000)
            {
                var paymentMethodUpdateOptions = new PaymentMethodUpdateOptions
                {
                    BillingDetails = new PaymentMethodBillingDetailsOptions
                    {
                        Name = $"{bankPaymentData.FirstName} {bankPaymentData.LastName}",
                        Email = bankPaymentData.Email.ToLower(),
                        Address = new AddressOptions
                        {
                            Line1 = bankPaymentData.Address?.Street,
                            City = bankPaymentData.Address?.City,
                            State = bankPaymentData.Address?.State,
                            PostalCode = bankPaymentData.Address?.ZipCode,
                        }
                    }
                };
                await _paymentMethodService.UpdateAsync(paymentMethodId, paymentMethodUpdateOptions);
            }

            var paymentIntentOptions = new PaymentIntentCreateOptions
            {
                Amount = (long)(paymentAmount * 100),
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
                        await UpdateUserBalance(newUser, amount, coverFees, userName, "Stripe Bank", bankPaymentData.Reference, bankPaymentData?.Address?.ZipCode);

                        var variables = new Dictionary<string, string>
                        {
                            { "firstName", newUser.FirstName! },
                            { "userName", userName },
                            { "resetPasswordUrl", $"{_appSecrets.RequestOrigin}/forgotpassword" },
                            { "siteUrl", _appSecrets.RequestOrigin }
                        };

                        _emailQueue.QueueEmail(async (sp) =>
                        {
                            var emailService = sp.GetRequiredService<IEmailTemplateService>();

                            await emailService.SendTemplateEmailAsync(
                                EmailTemplateCategory.WelcomeAnonymousUser,
                                newUser.Email,
                                variables
                            );
                        });
                    }
                    else
                    {
                        var user = await _repositoryManager.UserAuthentication.GetUserById(userId);
                        await UpdateUserBalance(user, amount, coverFees, bankPaymentData.UserName, "Stripe Bank", bankPaymentData.Reference, bankPaymentData?.Address?.ZipCode);
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
                paymentIntentData!.Status = "failed";

            string responseDataJson = JsonConvert.SerializeObject(paymentIntentData);

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
        public async Task WebhookCallForACHPaymentFailed(Charge charge)
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

                if (_appSecrets.IsProduction)
                {
                    decimal amount = Convert.ToDecimal(charge.Amount) * 0.01m;
                    string formattedAmount = $"${Convert.ToDecimal(amount):N2}";

                    var variables = new Dictionary<string, string>
                    {
                        { "firstName", user?.FirstName! },
                        { "lastName", user?.LastName! },
                        { "amount", formattedAmount }
                    };

                    _emailQueue.QueueEmail(async (sp) =>
                    {
                        var emailService = sp.GetRequiredService<IEmailTemplateService>();

                        await emailService.SendTemplateEmailAsync(
                            EmailTemplateCategory.ACHFailureNotification,
                            _appSecrets.AdminEmail,
                            variables
                        );
                    });
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
            var userName = $"{firstName}{lastName}".Replace(" ", "").Trim().ToLower();
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
                Password = _appSecrets.DefaultPassword,
                Email = email
            };
            await _repositoryManager.UserAuthentication.RegisterUserAsync(registrationDto, UserRoles.User);

            return userName;
        }

        #endregion CreateUser

        #region GetUserId

        private async Task<string?> GetUserId(string? userName)
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
            return userId;
        }

        #endregion GetUserId

        #region UpdateUserBalance

        private async Task UpdateUserBalance(User user, decimal amount, bool coverFees, string usernameFromResource, string paymentType, string? reference = null, string? zipCode = null)
        {
            decimal totalFee = amount * 0.05m;
            decimal stripeFee = 0m;

            switch (paymentType)
            {
                case "Stripe Card":
                    stripeFee = (amount * 0.022m) + 0.30m;
                    break;

                case "Stripe Bank":
                    stripeFee = Math.Min(amount * 0.008m, 5.0m);
                    break;

                default:
                    stripeFee = 0m;
                    break;
            }

            decimal totalFees = stripeFee + totalFee;

            amount = coverFees ? amount : amount - totalFees;

            var accountBalanceChangeLog = new AccountBalanceChangeLog
            {
                UserId = user!.Id,
                PaymentType = paymentType,
                OldValue = user.AccountBalance,
                UserName = user.UserName,
                NewValue = user.AccountBalance + amount,
                Fees = totalFees,
                GrossAmount = originalDonationAmount,
                NetAmount = amount,
                Reference = !string.IsNullOrWhiteSpace(reference) ? reference : null,
                ZipCode = !string.IsNullOrWhiteSpace(zipCode) ? zipCode : null,
            };
            _context.AccountBalanceChangeLogs.Add(accountBalanceChangeLog);
            await _context.SaveChangesAsync();

            user.AccountBalance += amount;

            if (string.IsNullOrWhiteSpace(user.ZipCode))
                user.ZipCode = !string.IsNullOrWhiteSpace(zipCode) ? zipCode : null;
            if (!string.IsNullOrEmpty(usernameFromResource) && user.IsActive == false)
                user.IsActive = true;
            if (user.IsFreeUser == true)
                user.IsFreeUser = false;

            await _repositoryManager.UserAuthentication.UpdateUser(user);
            await _repositoryManager.SaveAsync();
        }

        #endregion UpdateUserBalance
    }
}
