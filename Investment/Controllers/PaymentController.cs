using Invest.Core.Entities;
using Invest.Service.Interfaces;
using Investment.Extensions;
using Microsoft.AspNetCore.Mvc;
using Stripe;

namespace Investment.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PaymentController : ControllerBase
    {
        private readonly IPaymentService _paymentService;
        private readonly KeyVaultConfigService _keyVaultConfigService;
        private readonly IWebHostEnvironment _environment;

        public PaymentController(IPaymentService paymentService, KeyVaultConfigService keyVaultConfigService, IWebHostEnvironment environment)
        {
            _paymentService = paymentService;
            _keyVaultConfigService = keyVaultConfigService;
            _environment = environment;
        }

        [HttpPost("process-card-payment")]
        public async Task<IActionResult> ProcessCardPayment([FromBody] CardPayment cardPaymentData)
        {
            if (cardPaymentData == null)
            {
                return BadRequest(new { Success = false, Message = "Data type is invalid" });
            }
            if (!string.IsNullOrEmpty(cardPaymentData.PaymentMethodId))
            {
                if (string.IsNullOrWhiteSpace(cardPaymentData.PaymentMethodId))
                {
                    return BadRequest(new { Success = false, Message = "Payment method id is required" });
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(cardPaymentData.TokenId))
                {
                    return BadRequest(new { Success = false, Message = "Token Id is required" });
                }
                if (cardPaymentData.Amount <= 0)
                {
                    return BadRequest(new { Success = false, Message = "Amount must be greater than zero." });
                }
                if (cardPaymentData.RememberCardDetail != true && cardPaymentData.RememberCardDetail != false)
                {
                    return BadRequest(new { Success = false, Message = "Remember card detail must be either true or false." });
                }
            }

            string klaviyoApiKey = _keyVaultConfigService.GetKlaviyoApiKey();
            string klaviyoListKey = _keyVaultConfigService.GetKlaviyoListKey();
            bool isProduction = _environment.EnvironmentName == "Production";

            var response = await _paymentService.ProcessCardPayment(cardPaymentData, klaviyoApiKey, klaviyoListKey, isProduction);
            if (response.Success)
            {
                return Ok(response);
            }
            return BadRequest(response);
        }

        [HttpGet("card-payment-methods")]
        public async Task<IActionResult> CardPaymentMethods()
        {
            var paymentMethods = await _paymentService.CardPaymentMethods();
            if (paymentMethods == null || paymentMethods.Count == 0)
            {
                return Ok(new { Success = true });
            }
            return Ok(new { Success = true, Message = "Payment methods retrieved successfully.", Data = paymentMethods });
        }

        [HttpPost("ach-payment-secret")]
        public async Task<IActionResult> ACHPaymentSecret([FromBody] ACHPaymentSecret achPaymentSecretData)
        {
            if (achPaymentSecretData == null)
            {
                return BadRequest(new { Success = false, Message = "Data type is invalid" });
            }
            if (achPaymentSecretData.Amount <= 0)
            {
                return BadRequest(new { Success = false, Message = "Amount must be greater than zero." });
            }

            var response = await _paymentService.ACHPaymentSecret(achPaymentSecretData);
            if (response.Success)
            {
                return Ok(new { Success = true, Data = response.Message });
            }
            return BadRequest(response);
        }

        [HttpPost("process-bank-payment")]
        public async Task<IActionResult> ProcessBankPayment([FromBody] BankPayment bankPaymentData)
        {
            if (bankPaymentData == null)
            {
                return BadRequest(new { Success = false, Message = "Data type is invalid" });
            }
            if (string.IsNullOrEmpty(bankPaymentData.setup_intent))
            {
                return BadRequest(new { Success = false, Message = "setup_intent is required" });
            }
            if (string.IsNullOrEmpty(bankPaymentData.setup_intent_client_secret))
            {
                return BadRequest(new { Success = false, Message = "setup_intent_client_secret is required" });
            }
            if (string.IsNullOrEmpty(bankPaymentData.redirect_status))
            {
                return BadRequest(new { Success = false, Message = "redirect_status is required" });
            }

            string klaviyoApiKey = _keyVaultConfigService.GetKlaviyoApiKey();
            string klaviyoListKey = _keyVaultConfigService.GetKlaviyoListKey();
            bool isProduction = _environment.EnvironmentName == "Production";

            var response = await _paymentService.ProcessBankPayment(bankPaymentData, klaviyoApiKey, klaviyoListKey, isProduction);
            if (response.Success)
            {
                return Ok(response);
            }
            return BadRequest(response);
        }

        [HttpGet("validate-duplicate-email")]
        public async Task<IActionResult> ValidateDuplicateEmail(string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                return BadRequest(new { Success = false, Message = "Required email." });
            }
            var response = await _paymentService.ValidateDuplicateEmail(email);
            return Ok(response);
        }

        [HttpPost("stripe-webhook")]
        public async Task<IActionResult> StripeWebhook()
        {
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
            var signatureHeader = HttpContext.Request.Headers["Stripe-Signature"];
            string webhookSecret = _keyVaultConfigService.GetWebhookSecret();

            Event stripeEvent = EventUtility.ConstructEvent(json, signatureHeader, webhookSecret);

            if (stripeEvent.Type == "charge.failed")
            {
                var charge = stripeEvent.Data.Object as Stripe.Charge;
                if (charge != null)
                {
                    bool isProduction = _environment.EnvironmentName == "Production";

                    await _paymentService.WebhookCallForACHPaymentFailed(charge, isProduction);
                }
            }
            return Ok();
        }
    }
}
