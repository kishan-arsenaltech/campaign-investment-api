using Invest.Authorization.Enums;
using Invest.Core.Entities;
using Invest.Core.Settings;
using Invest.Service.Interfaces;
using Investment.Authorization.Attributes;
using Investment.Authorization.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stripe;

namespace Investment.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Module(Modules.Payment)]
    public class PaymentController : ControllerBase
    {
        private readonly IPaymentService _paymentService;
        private readonly AppSecrets _appSecrets;

        public PaymentController(IPaymentService paymentService, AppSecrets appSecrets)
        {
            _paymentService = paymentService;
            _appSecrets = appSecrets;
        }

        [HttpPost("process-card-payment")]
        [ModuleAuthorize(PermissionType.Manage)]
        public async Task<IActionResult> ProcessCardPayment([FromBody] CardPayment cardPaymentData)
        {
            if (cardPaymentData == null)
                return BadRequest(new { Success = false, Message = "Data type is invalid" });

            if (!string.IsNullOrEmpty(cardPaymentData.PaymentMethodId))
            {
                if (string.IsNullOrWhiteSpace(cardPaymentData.PaymentMethodId))
                    return BadRequest(new { Success = false, Message = "Payment method id is required" });
            }
            else
            {
                if (string.IsNullOrWhiteSpace(cardPaymentData.TokenId))
                    return BadRequest(new { Success = false, Message = "Token Id is required" });

                if (cardPaymentData.Amount <= 0)
                    return BadRequest(new { Success = false, Message = "Amount must be greater than zero." });

                if (cardPaymentData.RememberCardDetail != true && cardPaymentData.RememberCardDetail != false)
                    return BadRequest(new { Success = false, Message = "Remember card detail must be either true or false." });
            }

            var response = await _paymentService.ProcessCardPayment(cardPaymentData);

            if (response.Success)
                return Ok(response);

            return BadRequest(response);
        }

        [HttpGet("card-payment-methods")]
        [ModuleAuthorize(PermissionType.View)]
        public async Task<IActionResult> CardPaymentMethods()
        {
            var paymentMethods = await _paymentService.CardPaymentMethods();

            if (paymentMethods == null || paymentMethods.Count == 0)
                return Ok(new { Success = true });

            return Ok(new { Success = true, Message = "Payment methods retrieved successfully.", Data = paymentMethods });
        }

        [HttpPost("ach-payment-secret")]
        [ModuleAuthorize(PermissionType.Manage)]
        public async Task<IActionResult> ACHPaymentSecret([FromBody] ACHPaymentSecret achPaymentSecretData)
        {
            if (achPaymentSecretData == null)
                return BadRequest(new { Success = false, Message = "Data type is invalid" });

            if (achPaymentSecretData.Amount <= 0)
                return BadRequest(new { Success = false, Message = "Amount must be greater than zero." });

            var response = await _paymentService.ACHPaymentSecret(achPaymentSecretData);

            if (response.Success)
                return Ok(new { Success = true, Data = response.Message });

            return BadRequest(response);
        }

        [HttpPost("process-bank-payment")]
        [ModuleAuthorize(PermissionType.Manage)]
        public async Task<IActionResult> ProcessBankPayment([FromBody] BankPayment bankPaymentData)
        {
            if (bankPaymentData == null)
                return BadRequest(new { Success = false, Message = "Data type is invalid" });
            if (string.IsNullOrEmpty(bankPaymentData.setup_intent))
                return BadRequest(new { Success = false, Message = "setup_intent is required" });
            if (string.IsNullOrEmpty(bankPaymentData.setup_intent_client_secret))
                return BadRequest(new { Success = false, Message = "setup_intent_client_secret is required" });
            if (string.IsNullOrEmpty(bankPaymentData.redirect_status))
                return BadRequest(new { Success = false, Message = "redirect_status is required" });

            var response = await _paymentService.ProcessBankPayment(bankPaymentData);

            if (response.Success)
                return Ok(response);

            return BadRequest(response);
        }

        [HttpPost("stripe-webhook")]
        [AllowAnonymous]
        public async Task<IActionResult> StripeWebhook()
        {
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
            var signatureHeader = HttpContext.Request.Headers["Stripe-Signature"];
            string webhookSecret = _appSecrets.WebhookSecret;

            Stripe.Event stripeEvent = EventUtility.ConstructEvent(json, signatureHeader, webhookSecret);

            if (stripeEvent.Type == "charge.failed")
            {
                var charge = stripeEvent.Data.Object as Stripe.Charge;
                if (charge != null)
                    await _paymentService.WebhookCallForACHPaymentFailed(charge);
            }
            return Ok();
        }
    }
}
