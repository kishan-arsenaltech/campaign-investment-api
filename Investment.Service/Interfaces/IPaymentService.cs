using Invest.Core.Entities;
using Stripe;

namespace Invest.Service.Interfaces;

public interface IPaymentService
{
    Task<CommonResponse> ProcessCardPayment(CardPayment cardPaymentData, string klaviyoApiKey, string klaviyoListKey, bool isDevelopment);
    Task<List<PaymentMethodDetails>> CardPaymentMethods();
    Task<CommonResponse> ACHPaymentSecret(ACHPaymentSecret achPaymentSecretData);
    Task<CommonResponse> ProcessBankPayment(BankPayment bankPaymentData, string klaviyoApiKey, string klaviyoListKey, bool isDevelopment);
    Task<CommonResponse> ValidateDuplicateEmail(string email);
    Task WebhookCallForACHPaymentFailed(Charge charge, bool isProduction);
}