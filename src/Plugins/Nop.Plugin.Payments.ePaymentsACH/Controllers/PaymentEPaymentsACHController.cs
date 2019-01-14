using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Caching;
using Nop.Core.Domain.Logging;
using Nop.Plugin.Payments.ePaymentsACH.Models;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Security;
using Nop.Services.Stores;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;
using Ingenico.Connect.Sdk;
using Ingenico.Connect.Sdk.Domain.Definitions;
using Ingenico.Connect.Sdk.Domain.Hostedcheckout;
using Ingenico.Connect.Sdk.Domain.Hostedcheckout.Definitions;
using Ingenico.Connect.Sdk.Domain.Payment.Definitions;
using Ingenico.Connect.Sdk.Domain.Errors.Definitions;
using Ingenico.Connect.Sdk.Domain.Payment;
using Ingenico.Connect.Sdk.Webhooks;

namespace Nop.Plugin.Payments.ePaymentsACH.Controllers
{
    public class ACHWebhookKeys : ISecretKeyStore
    {
        private readonly EPaymentsACHPaymentSettings _ePaymentsACHPaymentSettings;
        public ACHWebhookKeys(EPaymentsACHPaymentSettings ePaymentsACHPaymentSettings)
        {
            this._ePaymentsACHPaymentSettings = ePaymentsACHPaymentSettings;
        }
        public string GetSecretKey(string keyId)
        { return _ePaymentsACHPaymentSettings.WebhookSecretKey; }
    }
    //public class WebhookEventStructure
    //{
    //    string apiVersion;
    //    string id;
    //    string created;
    //    string merchantId;
    //    string type;
    //    PaymentResponse paymentResponse;
    //}
    public class PaymentEPaymentsAchController : BasePaymentController
    {
        
        #region Fields

        private readonly IWorkContext _workContext;
        private readonly IStoreService _storeService;
        private readonly ISettingService _settingService;
        private readonly IPaymentService _paymentService;
        private readonly IOrderService _orderService;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IPermissionService _permissionService;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly ILocalizationService _localizationService;
        private readonly IStoreContext _storeContext;
        private readonly ILogger _logger;
        private readonly IWebHelper _webHelper;
        private readonly PaymentSettings _paymentSettings;
        private readonly EPaymentsACHPaymentSettings _ePaymentsACHPaymentSettings;
        private readonly ShoppingCartSettings _shoppingCartSettings;
        private readonly IStaticCacheManager _cacheManager;
        private ISecretKeyStore _secretKeyStore;
        

        #endregion

        #region Ctor

        public PaymentEPaymentsAchController(IWorkContext workContext,
            IStoreService storeService,
            ISettingService settingService,
            IPaymentService paymentService,
            IOrderService orderService,
            IOrderProcessingService orderProcessingService,
            IPermissionService permissionService,
            IGenericAttributeService genericAttributeService,
            ILocalizationService localizationService,
            IStoreContext storeContext,
            ILogger logger,
            IWebHelper webHelper,
            PaymentSettings paymentSettings,
            EPaymentsACHPaymentSettings ePaymentsACHPaymentSettings,
            ShoppingCartSettings shoppingCartSettings,
            IStaticCacheManager cacheManager)
        {
            this._workContext = workContext;
            this._storeService = storeService;
            this._settingService = settingService;
            this._paymentService = paymentService;
            this._orderService = orderService;
            this._orderProcessingService = orderProcessingService;
            this._permissionService = permissionService;
            this._genericAttributeService = genericAttributeService;
            this._localizationService = localizationService;
            this._storeContext = storeContext;
            this._logger = logger;
            this._webHelper = webHelper;
            this._paymentSettings = paymentSettings;
            this._ePaymentsACHPaymentSettings = ePaymentsACHPaymentSettings;
            this._shoppingCartSettings = shoppingCartSettings;
            this._cacheManager = cacheManager;
        }

        #endregion

        #region Methods

        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public IActionResult Configure()
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            //load settings for a chosen store scope
            var storeScope = this.GetActiveStoreScopeConfiguration(_storeService, _workContext);
            var ePaymentsACHPaymentSettings = _settingService.LoadSetting<EPaymentsACHPaymentSettings>(storeScope);

            var model = new ConfigurationModel
            {
                MerchantId = ePaymentsACHPaymentSettings.MerchantId,
                ApiKeyId = ePaymentsACHPaymentSettings.ApiKeyId,
                SecretApiKey = ePaymentsACHPaymentSettings.SecretApiKey,
                WebhookURL = ePaymentsACHPaymentSettings.WebhookURL,
                WebhookKeyId = ePaymentsACHPaymentSettings.WebhookKeyId,
                WebhookSecretKey = ePaymentsACHPaymentSettings.WebhookSecretKey,
                ActiveStoreScopeConfiguration = storeScope
            };
            if (storeScope > 0)
            {
                model.MerchantId_OverrideForStore = _settingService.SettingExists(ePaymentsACHPaymentSettings, x => x.MerchantId, storeScope);
                model.ApiKeyId_OverrideForStore = _settingService.SettingExists(ePaymentsACHPaymentSettings, x => x.ApiKeyId, storeScope);
                model.SecretApiKey_OverrideForStore = _settingService.SettingExists(ePaymentsACHPaymentSettings, x => x.SecretApiKey, storeScope);
                model.WebhookURL_OverrideForStore = _settingService.SettingExists(ePaymentsACHPaymentSettings, x => x.WebhookURL, storeScope);
                model.WebhookKeyId_OverrideForStore = _settingService.SettingExists(ePaymentsACHPaymentSettings, x => x.WebhookKeyId, storeScope);
                model.WebhookSecretKey_OverrideForStore = _settingService.SettingExists(ePaymentsACHPaymentSettings, x => x.WebhookSecretKey, storeScope);
            }

            return View("~/Plugins/Payments.ePaymentsACH/Views/Configure.cshtml", model);
        }

        [HttpPost]
        [AuthorizeAdmin]
        [AdminAntiForgery]
        [Area(AreaNames.Admin)]
        public IActionResult Configure(ConfigurationModel model)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            if (!ModelState.IsValid)
                return Configure();

            //load settings for a chosen store scope
            var storeScope = this.GetActiveStoreScopeConfiguration(_storeService, _workContext);
            var ePaymentsACHPaymentSettings = _settingService.LoadSetting<EPaymentsACHPaymentSettings>(storeScope);

            //save settings
            ePaymentsACHPaymentSettings.MerchantId = model.MerchantId;
            ePaymentsACHPaymentSettings.ApiKeyId = model.ApiKeyId;
            ePaymentsACHPaymentSettings.SecretApiKey = model.SecretApiKey;
            ePaymentsACHPaymentSettings.WebhookURL = model.WebhookURL;
            ePaymentsACHPaymentSettings.WebhookKeyId = model.WebhookKeyId;
            ePaymentsACHPaymentSettings.WebhookSecretKey = model.WebhookSecretKey;

            /* We do not clear cache after each setting update.
             * This behavior can increase performance because cached settings will not be cleared 
             * and loaded from database after each update */
            _settingService.SaveSettingOverridablePerStore(ePaymentsACHPaymentSettings, x => x.MerchantId, model.MerchantId_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(ePaymentsACHPaymentSettings, x => x.ApiKeyId, model.ApiKeyId_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(ePaymentsACHPaymentSettings, x => x.SecretApiKey, model.SecretApiKey_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(ePaymentsACHPaymentSettings, x => x.WebhookURL, model.WebhookURL_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(ePaymentsACHPaymentSettings, x => x.WebhookKeyId, model.WebhookKeyId_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(ePaymentsACHPaymentSettings, x => x.WebhookSecretKey, model.WebhookSecretKey_OverrideForStore, storeScope, false);

            //now clear settings cache
            _settingService.ClearCache();

            SuccessNotification(_localizationService.GetResource("Admin.Plugins.Saved"));

            return Configure();
        }
        [HttpGet]
        public IActionResult PaymentUpdate()
        {

            try
            {
                HttpRequest req = Request;

                string strHeaders = "";
                var xh = req.Headers.GetEnumerator();

                foreach (var i in req.Headers)
                {
                    strHeaders += i.Key;
                    strHeaders += " *** ";
                }
                
                _logger.InsertLog(LogLevel.Information, "Webhook GET", strHeaders);
                string msg = req.Headers["X-GCS-Webhooks-Endpoint-Verification"].ToString();

                if (String.IsNullOrEmpty(msg))
                    return Ok("Test Received");
                else
                    return Ok(msg);
            }
            catch(Exception ex)
            {
                _logger.InsertLog(LogLevel.Error, "Webhook GET", ex.Message);
                return NotFound();
                    
            }
        }
        [HttpPost]
        public IActionResult PaymentUpdate(string body)
        {

            string SignatureGood = _ePaymentsACHPaymentSettings.WebhookSecretKey;
            ACHWebhookKeys sk = new ACHWebhookKeys(_ePaymentsACHPaymentSettings);
            
            var helper = Webhooks.CreateHelper(sk);

            List<RequestHeader> requestHeaders = new List<RequestHeader>{
                new RequestHeader("X-GCS-Signature", SignatureGood),
                new RequestHeader("X-GCS-KeyId", _ePaymentsACHPaymentSettings.WebhookKeyId)
            };

            var anEvent = helper.Unmarshal(body, requestHeaders);
            return NoContent();
            //var orderNumberGuid = Guid.Empty;
            //try
            //{
            //    orderNumberGuid = new Guid(guid);
            //    var order = _orderService.GetOrderByGuid(orderNumberGuid);
            //    if (order != null)
            //    {
            //        string OriginReturnMac = _cacheManager.Get<string>(hostedCheckoutId);
            //        if (returnMAC == OriginReturnMac)
            //        {
            //            Task<GetHostedCheckoutResponse> task = GetResponse(hostedCheckoutId);
            //            task.Wait();
            //            GetHostedCheckoutResponse response = task.Result;
            //            if (response.Status == "PAYMENT_CREATED")
            //            {
            //                if (response.CreatedPaymentOutput.PaymentStatusCategory == "SUCCESSFUL")
            //                {
            //                    if ((response.CreatedPaymentOutput.Payment.Status == "CAPTURE_REQUESTED")
            //                        || (response.CreatedPaymentOutput.Payment.Status == "CAPTURED")
            //                        || (response.CreatedPaymentOutput.Payment.Status == "PAID"))
            //                    {

            //                        SetOrderAsPaid(order, response.CreatedPaymentOutput.Payment.Id,
            //                            response.CreatedPaymentOutput.Payment.Status);

            //                    }
            //                }
            //            }
            //            return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });
            //        }
            //        else
            //        {
            //            _logger.InsertLog(LogLevel.Warning, "EPayments PaymentHandler: MAC mismatch", "OrderID: " + order.Id.ToString());
            //        }
            //    }
            //    else
            //    {
            //        return RedirectToRoute("HomePage");
            //    }
            //}
            //catch (Exception ex)
            //{
            //    _logger.InsertLog(LogLevel.Error, ex.Message, ex.InnerException.Message);
            //}



        }

        public IActionResult CancelOrder()
        {
            var order = _orderService.SearchOrders(storeId: _storeContext.CurrentStore.Id,
                customerId: _workContext.CurrentCustomer.Id, pageSize: 1).FirstOrDefault();
            if (order != null)
                return RedirectToRoute("OrderDetails", new { orderId = order.Id });

            return RedirectToRoute("HomePage");
        }
        private void SetOrderAsPaid(Nop.Core.Domain.Orders.Order order, string tranId, string statDescription)
        {
            //mark order as paid
            if (_orderProcessingService.CanMarkOrderAsPaid(order))
            {
                order.CaptureTransactionId = tranId;
                order.CaptureTransactionResult = statDescription;
                _orderService.UpdateOrder(order);

                _orderProcessingService.MarkOrderAsPaid(order);
            }
        }

        private Client GetClient()
        {
            string apiKeyId = _ePaymentsACHPaymentSettings.ApiKeyId;
            string secretApiKey = _ePaymentsACHPaymentSettings.SecretApiKey;

            CommunicatorConfiguration configuration = Factory.CreateConfiguration(apiKeyId, secretApiKey);
            return Factory.CreateClient(configuration);
        }

        
    
    #endregion
}
}
