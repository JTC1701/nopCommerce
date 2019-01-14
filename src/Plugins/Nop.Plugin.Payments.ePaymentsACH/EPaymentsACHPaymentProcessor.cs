using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Core.Domain.Logging;
using Nop.Core.Domain.Payments;
using Nop.Core.Plugins;
using Nop.Core.Caching;
using Nop.Plugin.Payments.ePaymentsACH.Models;
using Nop.Plugin.Payments.ePaymentsACH.Validators;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Tax;
using Nop.Services.Logging;
using Nop.Services.Customers;
using Ingenico.Connect.Sdk;
using Ingenico.Connect.Sdk.Domain.Definitions;
using Ingenico.Connect.Sdk.Domain.Payment.Definitions;
using Ingenico.Connect.Sdk.Domain.Errors.Definitions;
using Ingenico.Connect.Sdk.Domain.Payment;


namespace Nop.Plugin.Payments.ePaymentsACH
{
    public class EPaymentsACHPaymentProcessor : BasePlugin, IPaymentMethod
    {
        #region Fields

        private readonly CurrencySettings _currencySettings;
        private readonly ICheckoutAttributeParser _checkoutAttributeParser;
        private readonly ICurrencyService _currencyService;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILocalizationService _localizationService;
        private readonly IOrderTotalCalculationService _orderTotalCalculationService;
        private readonly ISettingService _settingService;
        private readonly ITaxService _taxService;
        private readonly IWebHelper _webHelper;
        private readonly EPaymentsACHPaymentSettings _ePaymentsACHPaymentSettings;
        private readonly IStaticCacheManager _cacheManager;
        private readonly ILogger _logger;
        private readonly ICustomerService _customerService;

        #endregion
        #region Ctor

        public EPaymentsACHPaymentProcessor(CurrencySettings currencySettings,
             ICheckoutAttributeParser checkoutAttributeParser,
             ICurrencyService currencyService,
             IGenericAttributeService genericAttributeService,
             IHttpContextAccessor httpContextAccessor,
             ILocalizationService localizationService,
             IOrderTotalCalculationService orderTotalCalculationService,
             ISettingService settingService,
             ITaxService taxService,
             IWebHelper webHelper,
             EPaymentsACHPaymentSettings ePaymentsACHPaymentSettings,
             IStaticCacheManager cacheManager,
             ILogger logger,
             ICustomerService customerService
            )
        {
            this._currencySettings = currencySettings;
            this._checkoutAttributeParser = checkoutAttributeParser;
            this._currencyService = currencyService;
            this._genericAttributeService = genericAttributeService;
            this._httpContextAccessor = httpContextAccessor;
            this._localizationService = localizationService;
            this._orderTotalCalculationService = orderTotalCalculationService;
            this._settingService = settingService;
            this._taxService = taxService;
            this._webHelper = webHelper;
            this._ePaymentsACHPaymentSettings = ePaymentsACHPaymentSettings;
            this._cacheManager = cacheManager;
            this._logger = logger;
            this._customerService = customerService;
        }

        #endregion
        #region Utilities

        /// <summary>
        /// Initialize Client
        /// </summary>
        /// <returns></returns>
        /// 
        private Client GetClient()
        {
            try
            {
                string apiKeyId = _ePaymentsACHPaymentSettings.ApiKeyId;
                string secretApiKey = _ePaymentsACHPaymentSettings.SecretApiKey;

                CommunicatorConfiguration configuration = Factory.CreateConfiguration(apiKeyId, secretApiKey);
                return Factory.CreateClient(configuration);
            }
            catch (Exception ex)
            {
                _logger.InsertLog(LogLevel.Error, ex.Message, ex.InnerException.Message);
                throw new NopException("An error occurred while initializing ACH Payment.");

            }

        }

        private CreatePaymentRequest CreateACHPaymentRequest(ProcessPaymentRequest request, PaymentInfoModel paymentInfo)
        {
            //get customer
            var customer = _customerService.GetCustomerById(request.CustomerId);
            if (customer == null)
                throw new NopException("Customer cannot be loaded");
            //get the primary store currency
            string currencyCode = _currencyService.GetCurrencyById(_currencySettings.PrimaryStoreCurrencyId)?.CurrencyCode;
            if (currencyCode == null)
                throw new NopException("Primary store currency cannot be loaded");
            //build ACH request
            AmountOfMoney amountOfMoney = new AmountOfMoney
            {
                Amount = Convert.ToInt64(request.OrderTotal * 100),
                CurrencyCode = currencyCode
            };

            Address billingAddress = new Address
            {
                CountryCode = customer.BillingAddress.Country.TwoLetterIsoCode,
                City = customer.BillingAddress.City,
                StateCode = customer.BillingAddress.StateProvince.Abbreviation,
                Street = customer.BillingAddress.Address1,
                AdditionalInfo = customer.BillingAddress.Address2,
                Zip = customer.BillingAddress.ZipPostalCode
            };

            CompanyInformation companyInformation = new CompanyInformation
            {
                Name = CommonHelper.EnsureMaximumLength(customer.BillingAddress.Company,40)
            };
            
            ContactDetails contactDetails = new ContactDetails
            {
                EmailAddress = customer.Email,
                PhoneNumber = customer.BillingAddress.PhoneNumber
            };

            PersonalName name = new PersonalName
            {
                FirstName = CommonHelper.EnsureMaximumLength(customer.BillingAddress.FirstName,15),
                Surname = customer.BillingAddress.LastName
            };
            PersonalInformation personalInformation = new PersonalInformation
            {
                Name = name
            };
            Customer ACHcustomer = new Customer
            {
                BillingAddress = billingAddress,
                MerchantCustomerId = customer.Id.ToString(),
                PersonalInformation = personalInformation,
                CompanyInformation = companyInformation,
                ContactDetails = contactDetails
            };
            OrderReferences references = new OrderReferences
            {
                MerchantReference = CommonHelper.EnsureMaximumLength(request.OrderGuid.ToString(), 30),
                //MerchantOrderId = 24242L

            };
            Ingenico.Connect.Sdk.Domain.Payment.Definitions.Order order = new Ingenico.Connect.Sdk.Domain.Payment.Definitions.Order
            {
                AmountOfMoney = amountOfMoney,
                Customer = ACHcustomer,
                References = references
            };

            BankAccountBban bankAccountBban = new BankAccountBban
            {
                AccountHolderName = paymentInfo.AccountholderName,
                AccountNumber = paymentInfo.AccountNbr,
                BankCode = paymentInfo.RoutingNbr
            };

            NonSepaDirectDebitPaymentProduct730SpecificInput dd730in = new NonSepaDirectDebitPaymentProduct730SpecificInput
            {
                BankAccountBban = bankAccountBban
            };

            NonSepaDirectDebitPaymentMethodSpecificInput ddIn = new NonSepaDirectDebitPaymentMethodSpecificInput
            {
                DirectDebitText = "Ingenico eStore",
                Tokenize = true,
                PaymentProductId = 730,
                PaymentProduct730SpecificInput = dd730in
            };
            FraudFields fraudFields = new FraudFields
            {
                CustomerIpAddress = customer.LastIpAddress
            };
            CreatePaymentRequest createPaymentRequest = new CreatePaymentRequest
            {
                DirectDebitPaymentMethodSpecificInput = ddIn,
                Order = order,
                FraudFields= fraudFields 
            };

            return createPaymentRequest;
        }

        private async Task<CreatePaymentResponse> CreatePaymentAsync(Client client, CreatePaymentRequest req)
        {
            string merchantID = _ePaymentsACHPaymentSettings.MerchantId;
            CreatePaymentResponse response = await client.Merchant(merchantID).Payments().Create(req);
            return response;
        }

        #endregion
        #region Methods

        /// <summary>
        /// Process a payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public ProcessPaymentResult ProcessPayment(ProcessPaymentRequest processPaymentRequest)
        {
            ProcessPaymentResult processPaymentResult = new ProcessPaymentResult();
            PaymentInfoModel paymentInfo = new PaymentInfoModel
            {
                AccountholderName = (string)processPaymentRequest.CustomValues["Accountholder Name"],
                RoutingNbr = (string)processPaymentRequest.CustomValues["Routing Nbr"],
                AccountNbr = (string)processPaymentRequest.CustomValues["Account Nbr"]
            };
            CreatePaymentRequest request = CreateACHPaymentRequest(processPaymentRequest, paymentInfo);

            using (Client client = GetClient())
            {

                
                Task<CreatePaymentResponse> CreatePayment = CreatePaymentAsync(client, request);
                try
                {
                    CreatePayment.Wait();
                    CreatePaymentResponse response = CreatePayment.Result;
                    string token = response.CreationOutput.Token;
                    string str = response.Payment.Status;
                    processPaymentResult.CaptureTransactionId = response.Payment.Id;
                    processPaymentResult.CaptureTransactionResult = response.Payment.Status;
                    if   (response.Payment.Status == "PENDING_PAYMENT"
                       || response.Payment.Status == "PENDING_APPROVAL"
                       || response.Payment.Status == "PENDING_COMPLETION")
                    {
                        processPaymentResult.NewPaymentStatus = PaymentStatus.Authorized;
                    }
                    else
                    {
                        processPaymentResult.NewPaymentStatus = PaymentStatus.Pending;
                    }
                }
                catch (DeclinedPaymentException e)
                {
                    
                    processPaymentResult.AuthorizationTransactionResult = "Declined" ;
                    processPaymentResult.AuthorizationTransactionCode = e.CreatePaymentResult.Payment.Status;
                    processPaymentResult.NewPaymentStatus = PaymentStatus.Voided;
                    processPaymentResult.AddError("Payment Declined");
                }
                catch (ApiException e)
                {
                    _logger.InsertLog(LogLevel.Error, "ACH API CreatePaymentRequest Error: " + e.ErrorId, e.Errors.ToString());
                    throw new NopException("An API exception occurred while creating ACH Payment.");
                }
                catch (Exception ex)
                {
                    _logger.InsertLog(LogLevel.Error, "ACH CreatePaymentRequest Error: " + ex.Message, ex.InnerException.Message);
                    throw new NopException("An error occurred while creating ACH request.");
                }
            }

            //    processPaymentResult.AuthorizationTransactionResult = "ACH Test Auth";
            //processPaymentResult.AuthorizationTransactionCode = "999999";
            //processPaymentResult.NewPaymentStatus = PaymentStatus.Authorized;

            return processPaymentResult;
        }

        /// <summary>
        /// Post process payment (used by payment gateways that require redirecting to a third-party URL)
        /// </summary>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        public void PostProcessPayment(PostProcessPaymentRequest postProcessPaymentRequest)
        {
        }

        /// <summary>
        /// Returns a value indicating whether payment method should be hidden during checkout
        /// </summary>
        /// <param name="cart">Shopping cart</param>
        /// <returns>true - hide; false - display.</returns>
        public bool HidePaymentMethod(IList<ShoppingCartItem> cart)
        {
            //you can put any logic here
            //for example, hide this payment method if all products in the cart are downloadable
            //or hide this payment method if current customer is from certain country
            return false;
        }

        /// <summary>
        /// Gets additional handling fee
        /// </summary>
        /// <param name="cart">Shopping cart</param>
        /// <returns>Additional handling fee</returns>
        public decimal GetAdditionalHandlingFee(IList<ShoppingCartItem> cart)
        {
            return this.CalculateAdditionalFee(_orderTotalCalculationService, cart,0, false);
        }
        /// <summary>
        /// Captures payment
        /// </summary>
        /// <param name="capturePaymentRequest">Capture payment request</param>
        /// <returns>Capture payment result</returns>
        public CapturePaymentResult Capture(Nop.Services.Payments.CapturePaymentRequest capturePaymentRequest)
        {
            return new CapturePaymentResult { Errors = new[] { "Capture method not supported" } };
        }

        /// <summary>
        /// Refunds a payment
        /// </summary>
        /// <param name="refundPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public RefundPaymentResult Refund(RefundPaymentRequest refundPaymentRequest)
        {
            return new RefundPaymentResult { Errors = new[] { "Refund method not supported" } };
        }

        /// <summary>
        /// Voids a payment
        /// </summary>
        /// <param name="voidPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public VoidPaymentResult Void(VoidPaymentRequest voidPaymentRequest)
        {
            return new VoidPaymentResult { Errors = new[] { "Void method not supported" } };
        }

        /// <summary>
        /// Process recurring payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public ProcessPaymentResult ProcessRecurringPayment(ProcessPaymentRequest processPaymentRequest)
        {
            return new ProcessPaymentResult { Errors = new[] { "Recurring payment not supported" } };
        }

        /// <summary>
        /// Cancels a recurring payment
        /// </summary>
        /// <param name="cancelPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public CancelRecurringPaymentResult CancelRecurringPayment(CancelRecurringPaymentRequest cancelPaymentRequest)
        {
            return new CancelRecurringPaymentResult { Errors = new[] { "Recurring payment not supported" } };
        }

        /// <summary>
        /// Gets a value indicating whether customers can complete a payment after order is placed but not completed (for redirection payment methods)
        /// </summary>
        /// <param name="order">Order</param>
        /// <returns>Result</returns>
        public bool CanRePostProcessPayment(Core.Domain.Orders.Order order)
        {
            if (order == null)
                throw new ArgumentNullException(nameof(order));

            //it's not a redirection payment method. So we always return false
            return false;
        }

        /// <summary>
        /// Validate payment form
        /// </summary>
        /// <param name="form">The parsed form values</param>
        /// <returns>List of validating errors</returns>
        public IList<string> ValidatePaymentForm(IFormCollection form)
        {
            var warnings = new List<string>();

            //validate
            var validator = new PaymentInfoValidator(_localizationService);
            var model = new PaymentInfoModel
            {

                AccountholderName = form["AccountholderName"],
                RoutingNbr = form["RoutingNbr"],
                AccountNbr = form["AccountNbr"]
                
            };
            var validationResult = validator.Validate(model);
            if (!validationResult.IsValid)
                warnings.AddRange(validationResult.Errors.Select(error => error.ErrorMessage));

            return warnings;
        }

        /// <summary>
        /// Get payment information
        /// </summary>
        /// <param name="form">The parsed form values</param>
        /// <returns>Payment info holder</returns>
        public ProcessPaymentRequest GetPaymentInfo(IFormCollection form)
        {

            ProcessPaymentRequest request = new ProcessPaymentRequest();
            request.CustomValues.Add("Accountholder Name", form["AccountholderName"].ToString());
            request.CustomValues.Add("Routing Nbr", form["RoutingNbr"].ToString());
            request.CustomValues.Add("Account Nbr", form["AccountNbr"].ToString());

            return request;
        }

        /// <summary>
        /// Gets a configuration page URL
        /// </summary>
        public override string GetConfigurationPageUrl()
        {
            return $"{_webHelper.GetStoreLocation()}Admin/PaymentEPaymentsACH/Configure";
        }

        /// <summary>
        /// Gets a view component for displaying plugin in public store ("payment info" checkout step)
        /// </summary>
        /// <param name="viewComponentName">View component name</param>
        public void GetPublicViewComponent(out string viewComponentName)
        {
            viewComponentName = "PaymentEPaymentsACH";
        }
        /// <summary>
        /// Install the plugin
        /// </summary>
        public override void Install()
        {
            //settings
            _settingService.SaveSetting(new EPaymentsACHPaymentSettings
            {

            });

            //locales

            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.ePaymentsACH.Fields.MerchantId", "ePayments Merchant ID");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.ePaymentsACH.Fields.MerchantId.Hint", "Specify your ePayments Merchant ID.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.ePaymentsACH.Fields.ApiKeyId", "ApiKeyId Identity Token");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.ePaymentsACH.Fields.ApiKeyId.Hint", "Specify ePayments ApiKeyId identity token");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.ePaymentsACH.Fields.SecretApiKey", "ePayments Secret API Key");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.ePaymentsACH.Fields.SecretApiKey.Hint", "ePayments Secret API Key.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.ePaymentsACH.Fields.WebhookURL", "ePayments Webhook URL");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.ePaymentsACH.Fields.WebhookURL.Hint", "This is the URL which ePayments will use to send payment status updates.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.ePaymentsACH.Fields.WebhookKeyId", "ePayments Webhook Key ID");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.ePaymentsACH.Fields.WebhookKeyId.Hint", "Webhook key which is set up on the ePayments Merchant Configuration page");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.ePaymentsACH.Fields.WebhookSecretKey", "ePayments Webhook Secret Key");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.ePaymentsACH.Fields.WebhookSecretKey.Hint", "Webhook Secrect Key which is set up on the ePayments Merchant Configuration page");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.ePaymentsACH.Instructions", "<p><b>To set up a sandbox account, please visit https://ePayments.developer-ingenico.com/ </b></p>");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.ePaymentsACH.PaymentMethodDescription", "ACH payment");
            this.AddOrUpdatePluginLocaleResource("Payment.ACH.AccountholderName", "Accountholder Name");
            this.AddOrUpdatePluginLocaleResource("Payment.ACH.RoutingNbr", "Routing Number");
            this.AddOrUpdatePluginLocaleResource("Payment.ACH.AccountNbr", "Account Number");
            base.Install();
        }

        /// <summary>
        /// Uninstall the plugin
        /// </summary>
        public override void Uninstall()
        {
            //settings
            _settingService.DeleteSetting<EPaymentsACHPaymentSettings>();

            //locales

            this.DeletePluginLocaleResource("Plugins.Payments.ePaymentsACH.Fields.MerchantId");
            this.DeletePluginLocaleResource("Plugins.Payments.ePaymentsACH.Fields.MerchantId.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.ePaymentsACH.Fields.ApiKeyId");
            this.DeletePluginLocaleResource("Plugins.Payments.ePaymentsACH.Fields.ApiKeyId.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.ePaymentsACH.Fields.SecretApiKey");
            this.DeletePluginLocaleResource("Plugins.Payments.ePaymentsACH.Fields.SecretApiKey.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.ePaymentsACH.Fields.WebhookURL");
            this.DeletePluginLocaleResource("Plugins.Payments.ePaymentsACH.Fields.WebhookURL.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.ePaymentsACH.Instructions");
            this.DeletePluginLocaleResource("Plugins.Payments.ePaymentsACH.PaymentMethodDescription");
            this.DeletePluginLocaleResource("Payment.ACH.AccountholderName");
            this.DeletePluginLocaleResource("Payment.ACH.RoutingNbr");
            this.DeletePluginLocaleResource("Payment.ACH.AccountNbr");
            this.DeletePluginLocaleResource("Plugins.Payments.ePaymentsACH.Fields.WebhookKeyId");
            this.DeletePluginLocaleResource("Plugins.Payments.ePaymentsACH.Fields.WebhookKeyId.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.ePaymentsACH.Fields.WebhookSecretKey");
            this.DeletePluginLocaleResource("Plugins.Payments.ePaymentsACH.Fields.WebhookSecretKey.Hint");

            base.Uninstall();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets a value indicating whether capture is supported
        /// </summary>
        public bool SupportCapture
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a value indicating whether partial refund is supported
        /// </summary>
        public bool SupportPartiallyRefund
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a value indicating whether refund is supported
        /// </summary>
        public bool SupportRefund
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a value indicating whether void is supported
        /// </summary>
        public bool SupportVoid
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a recurring payment type of payment method
        /// </summary>
        public RecurringPaymentType RecurringPaymentType
        {
            get { return RecurringPaymentType.NotSupported; }
        }

        /// <summary>
        /// Gets a payment method type
        /// </summary>
        public PaymentMethodType PaymentMethodType
        {
            get { return PaymentMethodType.Standard; }
        }

        /// <summary>
        /// Gets a value indicating whether we should display a payment information page for this plugin
        /// </summary>
        public bool SkipPaymentInfo
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a payment method description that will be displayed on checkout pages in the public store
        /// </summary>
        public string PaymentMethodDescription
        {
            //return description of this payment method to be display on "payment method" checkout step. good practice is to make it localizable
            //for example, for a redirection payment method, description may be like this: "You will be redirected to PayPal site to complete the payment"
            get { return _localizationService.GetResource("Plugins.Payments.ePaymentsACH.PaymentMethodDescription"); }
        }
        #endregion
    }
}
