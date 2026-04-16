using Invest.Core.Entities;
using Stripe;

namespace Invest.Service.Interfaces;

public interface IPaymentService
{
    Task<CommonResponse> ProcessCardPayment(CardPayment cardPaymentData);
    Task<List<PaymentMethodDetails>> CardPaymentMethods();
    Task<CommonResponse> ACHPaymentSecret(ACHPaymentSecret achPaymentSecretData);
    Task<CommonResponse> ProcessBankPayment(BankPayment bankPaymentData);
    Task WebhookCallForACHPaymentFailed(Charge charge);
}