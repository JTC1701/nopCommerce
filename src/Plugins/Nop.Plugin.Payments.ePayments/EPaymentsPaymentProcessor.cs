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
using Nop.Core.Plugins;
using Nop.Core.Caching;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Tax;
using Nop.Services.Logging;
using Ingenico.Connect.Sdk;
using Ingenico.Connect.Sdk.Domain.Definitions;
using Ingenico.Connect.Sdk.Domain.Hostedcheckout;
using Ingenico.Connect.Sdk.Domain.Hostedcheckout.Definitions;
using Ingenico.Connect.Sdk.Domain.Payment.Definitions;
using Ingenico.Connect.Sdk.Domain.Errors.Definitions;
using Ingenico.Connect.Sdk.Domain.Payment;


namespace Nop.Plugin.Payments.ePayments
{
    /// <summary>
    /// Ingenico ePayments payment processor
    /// </summary>
    public class EPaymentsPaymentProcessor : BasePlugin, IPaymentMethod
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
        private readonly EPaymentsPaymentSettings _ePaymentsPaymentSettings;
        private readonly IStaticCacheManager _cacheManager;
        private readonly ILogger _logger;

        #endregion

        #region Ctor

        public EPaymentsPaymentProcessor(CurrencySettings currencySettings,
            ICheckoutAttributeParser checkoutAttributeParser,
            ICurrencyService currencyService,
            IGenericAttributeService genericAttributeService,
            IHttpContextAccessor httpContextAccessor,
            ILocalizationService localizationService,
            IOrderTotalCalculationService orderTotalCalculationService,
            ISettingService settingService,
            ITaxService taxService,
            IWebHelper webHelper,
            EPaymentsPaymentSettings ePaymentsPaymentSettings,
            IStaticCacheManager cacheManager,
            ILogger logger
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
            this._ePaymentsPaymentSettings = ePaymentsPaymentSettings;
            this._cacheManager = cacheManager;
            this._logger = logger;
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
                string apiKeyId = _ePaymentsPaymentSettings.ApiKeyId;
                string secretApiKey = _ePaymentsPaymentSettings.SecretApiKey;

                CommunicatorConfiguration configuration = Factory.CreateConfiguration(apiKeyId, secretApiKey);
                return Factory.CreateClient(configuration);
            }
            catch (Exception ex)
            {
                _logger.InsertLog(LogLevel.Error, ex.Message, ex.InnerException.Message);
                throw new NopException("An error occurred while initializing Payment.");

            }

        }
        private async Task<CreateHostedCheckoutResponse> CreateHostedCheckoutAsync(Client client, CreateHostedCheckoutRequest req)
        {
            string merchantID = _ePaymentsPaymentSettings.MerchantId;
            CreateHostedCheckoutResponse response = await client.Merchant(merchantID).Hostedcheckouts().Create(req);
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
            return new ProcessPaymentResult();
        }

        /// <summary>
        /// Post process payment (used by payment gateways that require redirecting to a third-party URL)
        /// </summary>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        public void PostProcessPayment(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            using (Client client = GetClient())
            {
                HostedCheckoutSpecificInput hostedCheckoutSpecificInput = new HostedCheckoutSpecificInput();
                hostedCheckoutSpecificInput.Locale = "en_GB";
                hostedCheckoutSpecificInput.Variant = "100";
                hostedCheckoutSpecificInput.ReturnUrl = _ePaymentsPaymentSettings.ReturnURL;
                hostedCheckoutSpecificInput.ReturnUrl += "?guid=" + postProcessPaymentRequest.Order.OrderGuid;

                PaymentProductFiltersHostedCheckout paymentProductFiltersHostedCheckout = new PaymentProductFiltersHostedCheckout();
                PaymentProductFilter paymentProductFilter = new PaymentProductFilter();
                IList<Int32?> PayProducts = new List<Int32?>
                {
                    // All Payment Product IDs are listed at https://epayments-api.developer-ingenico.com/s2sapi/v1/en_US/java/paymentproducts.html
                    1, // Visa
                    2, // American Express
                    3, // MasterCard
                    128 // Discover
                    // 730 // ACH
                };

                paymentProductFilter.Products = PayProducts;
                paymentProductFiltersHostedCheckout.RestrictTo = paymentProductFilter;
                hostedCheckoutSpecificInput.PaymentProductFilters = paymentProductFiltersHostedCheckout;

                string currencyCode = _currencyService.GetCurrencyById(_currencySettings.PrimaryStoreCurrencyId)?.CurrencyCode;

                AmountOfMoney amountOfMoney = new AmountOfMoney();
                amountOfMoney.Amount = Convert.ToInt64(postProcessPaymentRequest.Order.OrderTotal * 100);
                amountOfMoney.CurrencyCode = currencyCode;
                Address billingAddress = new Address();
                billingAddress.CountryCode = postProcessPaymentRequest.Order.BillingAddress.Country.TwoLetterIsoCode;

                billingAddress.City = postProcessPaymentRequest.Order.BillingAddress.City;
                billingAddress.StateCode = postProcessPaymentRequest.Order.BillingAddress.StateProvince.Abbreviation;
                billingAddress.Street = postProcessPaymentRequest.Order.BillingAddress.Address1;
                billingAddress.AdditionalInfo = postProcessPaymentRequest.Order.BillingAddress.Address2;
                billingAddress.Zip = postProcessPaymentRequest.Order.BillingAddress.ZipPostalCode;

                Customer customer = new Customer();
                customer.BillingAddress = billingAddress;
                customer.MerchantCustomerId = postProcessPaymentRequest.Order.CustomerId.ToString();


                PersonalName name = new PersonalName();
                name.FirstName = postProcessPaymentRequest.Order.Customer.BillingAddress.FirstName;
                name.Surname = postProcessPaymentRequest.Order.Customer.BillingAddress.LastName;

                PersonalInformation personalInformation = new PersonalInformation { Name = name };
                customer.PersonalInformation = personalInformation;

                CompanyInformation companyInformation = new CompanyInformation
                {
                    Name = CommonHelper.EnsureMaximumLength(postProcessPaymentRequest.Order.Customer.BillingAddress.Company,40)
                };
               
                customer.CompanyInformation = companyInformation;

                ContactDetails contactDetails = new ContactDetails
                {
                    EmailAddress = postProcessPaymentRequest.Order.Customer.Email,
                    PhoneNumber = postProcessPaymentRequest.Order.Customer.BillingAddress.PhoneNumber
                };
                customer.ContactDetails = contactDetails;


                OrderReferences references = new OrderReferences();
                // references.InvoiceData = invoiceData;
                references.MerchantOrderId = postProcessPaymentRequest.Order.Id;
                references.Descriptor = "Ingenico eStore";  // This will appear on the customer's bank statement
                //references.MerchantReference = postProcessPaymentRequest.Order.OrderGuid.ToString();

                IList<LineItem> items = new List<LineItem>();

                foreach (var lineitem in postProcessPaymentRequest.Order.OrderItems)
                {
                    AmountOfMoney amt = new AmountOfMoney();
                    amt.Amount = Convert.ToInt64(lineitem.PriceExclTax * 100);
                    amt.CurrencyCode = currencyCode;

                    LineItemInvoiceData inv = new LineItemInvoiceData();
                    inv.Description = lineitem.Product.Sku;
                    inv.NrOfItems = lineitem.Quantity.ToString();
                    inv.PricePerItem = Convert.ToInt64(lineitem.UnitPriceExclTax * 100);

                    LineItem item = new LineItem();
                    item.AmountOfMoney = amt;
                    item.InvoiceData = inv;
                    items.Add(item);
                }
                // add shipping as a line item
                if (postProcessPaymentRequest.Order.OrderShippingExclTax > 0)
                {
                    AmountOfMoney amt = new AmountOfMoney();
                    amt.Amount = Convert.ToInt64(postProcessPaymentRequest.Order.OrderShippingExclTax * 100);
                    amt.CurrencyCode = currencyCode;

                    LineItemInvoiceData inv = new LineItemInvoiceData();
                    inv.Description = "Shipping & Handling";
                    inv.NrOfItems = "1";
                    inv.PricePerItem = amt.Amount;

                    LineItem item = new LineItem();
                    item.AmountOfMoney = amt;
                    item.InvoiceData = inv;
                    items.Add(item);
                }
                // add Tax amount as a line item
                if (postProcessPaymentRequest.Order.OrderTax > 0)
                {
                    AmountOfMoney amt = new AmountOfMoney();
                    amt.Amount = Convert.ToInt64(postProcessPaymentRequest.Order.OrderTax * 100);
                    amt.CurrencyCode = currencyCode;

                    LineItemInvoiceData inv = new LineItemInvoiceData();
                    inv.Description = "Tax";
                    inv.NrOfItems = "1";
                    inv.PricePerItem = amt.Amount;

                    LineItem item = new LineItem();
                    item.AmountOfMoney = amt;
                    item.InvoiceData = inv;
                    items.Add(item);
                }

                ShoppingCart shoppingCart = new ShoppingCart();
                shoppingCart.Items = items;


                Ingenico.Connect.Sdk.Domain.Payment.Definitions.Order order = new Ingenico.Connect.Sdk.Domain.Payment.Definitions.Order();
                order.AmountOfMoney = amountOfMoney;
                order.Customer = customer;
                order.References = references;
                order.ShoppingCart = shoppingCart;

                FraudFields fraudFields = new FraudFields
                {
                    CustomerIpAddress = postProcessPaymentRequest.Order.Customer.LastIpAddress
                };

                CreateHostedCheckoutRequest body = new CreateHostedCheckoutRequest();
                body.HostedCheckoutSpecificInput = hostedCheckoutSpecificInput;
                body.Order = order;
                body.FraudFields = fraudFields;


                Task<CreateHostedCheckoutResponse> CreateHostedCheckout = CreateHostedCheckoutAsync(client, body);
                try
                {
                    CreateHostedCheckout.Wait();
                    CreateHostedCheckoutResponse response = CreateHostedCheckout.Result;
                    string returnMAC = response.RETURNMAC;
                    string cacheKey = response.HostedCheckoutId;
                    _cacheManager.Set(cacheKey, returnMAC, 60);
                    string url = "https://payment." + response.PartialRedirectUrl;
                    _httpContextAccessor.HttpContext.Response.Redirect(url);
                }
                catch (Exception ex)
                {
                    _logger.InsertLog(LogLevel.Error, "CreateHostedCheckout Error: " + ex.Message, ex.InnerException.Message);
                    throw new NopException("An error occurred while creating hosted checkout.");
                }
            }


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
            return this.CalculateAdditionalFee(_orderTotalCalculationService, cart,
                _ePaymentsPaymentSettings.AdditionalFee, _ePaymentsPaymentSettings.AdditionalFeePercentage);
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

            //let's ensure that at least 5 seconds passed after order is placed
            //P.S. there's no any particular reason for that. we just do it
            if ((DateTime.UtcNow - order.CreatedOnUtc).TotalSeconds < 5)
                return false;

            return true;
        }

        /// <summary>
        /// Validate payment form
        /// </summary>
        /// <param name="form">The parsed form values</param>
        /// <returns>List of validating errors</returns>
        public IList<string> ValidatePaymentForm(IFormCollection form)
        {
            return new List<string>();
        }

        /// <summary>
        /// Get payment information
        /// </summary>
        /// <param name="form">The parsed form values</param>
        /// <returns>Payment info holder</returns>
        public ProcessPaymentRequest GetPaymentInfo(IFormCollection form)
        {
            return new ProcessPaymentRequest();
        }

        /// <summary>
        /// Gets a configuration page URL
        /// </summary>
        public override string GetConfigurationPageUrl()
        {
            return $"{_webHelper.GetStoreLocation()}Admin/PaymentEPayments/Configure";
        }

        /// <summary>
        /// Gets a view component for displaying plugin in public store ("payment info" checkout step)
        /// </summary>
        /// <param name="viewComponentName">View component name</param>
        public void GetPublicViewComponent(out string viewComponentName)
        {
            viewComponentName = "PaymentEPayments";
        }
       
        /// <summary>
        /// Install the plugin
        /// </summary>
        public override void Install()
        {
            //settings
            _settingService.SaveSetting(new EPaymentsPaymentSettings
            {
               
            });

            //locales
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.ePayments.Fields.AdditionalFee", "Additional fee");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.ePayments.Fields.AdditionalFee.Hint", "Enter additional fee to charge your customers.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.ePayments.Fields.AdditionalFeePercentage", "Additional fee. Use percentage");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.ePayments.Fields.AdditionalFeePercentage.Hint", "Determines whether to apply a percentage additional fee to the order total. If not enabled, a fixed value is used.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.ePayments.Fields.MerchantId", "ePayments Merchant ID");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.ePayments.Fields.MerchantId.Hint", "Specify your ePayments Merchant ID.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.ePayments.Fields.PassProductNamesAndTotals", "Pass product names and order totals to checkout page");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.ePayments.Fields.PassProductNamesAndTotals.Hint", "Check if product names and order totals should be passed to the checkout page.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.ePayments.Fields.ApiKeyId", "ApiKeyId Identity Token");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.ePayments.Fields.ApiKeyId.Hint", "Specify ePayments ApiKeyId identity token");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.ePayments.Fields.RedirectionTip", "You will be redirected to the Ingenico ePayments checkout page to complete the payment. Upon successful completion, you will be directed back to this site.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.ePayments.Fields.SecretApiKey", "ePayments Secret API Key");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.ePayments.Fields.SecretApiKey.Hint", "ePayments Secret API Key.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.ePayments.Fields.ReturnURL", "ePayments Return URL");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.ePayments.Fields.ReturnURL.Hint", "The URL to be redirected upon completion of the payment.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.ePayments.Instructions", "<p><b>To set up a sandbox account, please visit https://epayments.developer-ingenico.com/ </b></p>");
            this.AddOrUpdatePluginLocaleResource("Plugins.Payments.ePayments.PaymentMethodDescription", "You will be redirected to the Ingenico ePayments checkout page to complete the payment.");
            base.Install();
        }

        /// <summary>
        /// Uninstall the plugin
        /// </summary>
        public override void Uninstall()
        {
            //settings
            _settingService.DeleteSetting<EPaymentsPaymentSettings>();

            //locales
            this.DeletePluginLocaleResource("Plugins.Payments.ePayments.Fields.AdditionalFee");
            this.DeletePluginLocaleResource("Plugins.Payments.ePayments.Fields.AdditionalFee.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.ePayments.Fields.AdditionalFeePercentage");
            this.DeletePluginLocaleResource("Plugins.Payments.ePayments.Fields.AdditionalFeePercentage.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.ePayments.Fields.MerchantId");
            this.DeletePluginLocaleResource("Plugins.Payments.ePayments.Fields.MerchantId.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.ePayments.Fields.PassProductNamesAndTotals");
            this.DeletePluginLocaleResource("Plugins.Payments.ePayments.Fields.PassProductNamesAndTotals.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.ePayments.Fields.ApiKeyId");
            this.DeletePluginLocaleResource("Plugins.Payments.ePayments.Fields.ApiKeyId.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.ePayments.Fields.RedirectionTip");
            this.DeletePluginLocaleResource("Plugins.Payments.ePayments.Fields.SecretApiKey");
            this.DeletePluginLocaleResource("Plugins.Payments.ePayments.Fields.SecretApiKey.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.ePayments.Fields.ReturnURL");
            this.DeletePluginLocaleResource("Plugins.Payments.ePayments.Fields.ReturnURL.Hint");
            this.DeletePluginLocaleResource("Plugins.Payments.ePayments.Instructions");
            this.DeletePluginLocaleResource("Plugins.Payments.ePayments.PaymentMethodDescription");

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
            get { return PaymentMethodType.Redirection; }
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
            get { return _localizationService.GetResource("Plugins.Payments.ePayments.PaymentMethodDescription"); }
        }

        #endregion

    }
}
