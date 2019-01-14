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
using Nop.Plugin.Payments.ePayments.Models;
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

namespace Nop.Plugin.Payments.ePayments.Controllers
{
    public class PaymentEPaymentsController : BasePaymentController
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
        private readonly EPaymentsPaymentSettings _ePaymentsPaymentSettings;
        private readonly ShoppingCartSettings _shoppingCartSettings;
        private readonly IStaticCacheManager _cacheManager;

        #endregion

        #region Ctor

        public PaymentEPaymentsController(IWorkContext workContext,
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
            EPaymentsPaymentSettings ePaymentsPaymentSettings,
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
            this._ePaymentsPaymentSettings = ePaymentsPaymentSettings;
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
            var ePaymentsPaymentSettings = _settingService.LoadSetting<EPaymentsPaymentSettings>(storeScope);

            var model = new ConfigurationModel
            {
                MerchantId = ePaymentsPaymentSettings.MerchantId,
                ApiKeyId = ePaymentsPaymentSettings.ApiKeyId,
                SecretApiKey = ePaymentsPaymentSettings.SecretApiKey,
                ReturnURL = ePaymentsPaymentSettings.ReturnURL,
                PassProductNamesAndTotals = ePaymentsPaymentSettings.PassProductNamesAndTotals,
                AdditionalFee = ePaymentsPaymentSettings.AdditionalFee,
                AdditionalFeePercentage = ePaymentsPaymentSettings.AdditionalFeePercentage,
                ActiveStoreScopeConfiguration = storeScope
            };
            if (storeScope > 0)
            {
                model.MerchantId_OverrideForStore = _settingService.SettingExists(ePaymentsPaymentSettings, x => x.MerchantId, storeScope);
                model.ApiKeyId_OverrideForStore = _settingService.SettingExists(ePaymentsPaymentSettings, x => x.ApiKeyId, storeScope);
                model.SecretApiKey_OverrideForStore = _settingService.SettingExists(ePaymentsPaymentSettings, x => x.SecretApiKey, storeScope);
                model.ReturnURL_OverrideForStore = _settingService.SettingExists(ePaymentsPaymentSettings, x => x.ReturnURL, storeScope);
                model.PassProductNamesAndTotals_OverrideForStore = _settingService.SettingExists(ePaymentsPaymentSettings, x => x.PassProductNamesAndTotals, storeScope);
                model.AdditionalFee_OverrideForStore = _settingService.SettingExists(ePaymentsPaymentSettings, x => x.AdditionalFee, storeScope);
                model.AdditionalFeePercentage_OverrideForStore = _settingService.SettingExists(ePaymentsPaymentSettings, x => x.AdditionalFeePercentage, storeScope);
            }

            return View("~/Plugins/Payments.ePayments/Views/Configure.cshtml", model);
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
            var ePaymentsPaymentSettings = _settingService.LoadSetting<EPaymentsPaymentSettings>(storeScope);

            //save settings
            ePaymentsPaymentSettings.MerchantId = model.MerchantId;
            ePaymentsPaymentSettings.ApiKeyId = model.ApiKeyId;
            ePaymentsPaymentSettings.SecretApiKey = model.SecretApiKey;
            ePaymentsPaymentSettings.ReturnURL = model.ReturnURL;
            ePaymentsPaymentSettings.PassProductNamesAndTotals = model.PassProductNamesAndTotals;
            ePaymentsPaymentSettings.AdditionalFee = model.AdditionalFee;
            ePaymentsPaymentSettings.AdditionalFeePercentage = model.AdditionalFeePercentage;

            /* We do not clear cache after each setting update.
             * This behavior can increase performance because cached settings will not be cleared 
             * and loaded from database after each update */
            _settingService.SaveSettingOverridablePerStore(ePaymentsPaymentSettings, x => x.MerchantId, model.MerchantId_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(ePaymentsPaymentSettings, x => x.ApiKeyId, model.ApiKeyId_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(ePaymentsPaymentSettings, x => x.SecretApiKey, model.SecretApiKey_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(ePaymentsPaymentSettings, x => x.ReturnURL, model.ReturnURL_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(ePaymentsPaymentSettings, x => x.PassProductNamesAndTotals, model.PassProductNamesAndTotals_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(ePaymentsPaymentSettings, x => x.AdditionalFee, model.AdditionalFee_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(ePaymentsPaymentSettings, x => x.AdditionalFeePercentage, model.AdditionalFeePercentage_OverrideForStore, storeScope, false);

            //now clear settings cache
            _settingService.ClearCache();

            SuccessNotification(_localizationService.GetResource("Admin.Plugins.Saved"));

            return Configure();
        }

        public IActionResult PaymentHandler(string guid, string returnMAC, string hostedCheckoutId)
        {

            var orderNumberGuid = Guid.Empty;
            try
            {
                orderNumberGuid = new Guid(guid);

                var order = _orderService.GetOrderByGuid(orderNumberGuid);
                if (order != null)
                {
                    string OriginReturnMac = _cacheManager.Get<string>(hostedCheckoutId);
                    if (returnMAC == OriginReturnMac)
                    {
                        Task<GetHostedCheckoutResponse> task = GetResponse(hostedCheckoutId);
                        task.Wait();
                        GetHostedCheckoutResponse response = task.Result;
                        if (response.Status == "PAYMENT_CREATED")
                        {
                            if (response.CreatedPaymentOutput.PaymentStatusCategory == "SUCCESSFUL")
                            {
                                if ((response.CreatedPaymentOutput.Payment.Status == "CAPTURE_REQUESTED") 
                                    || (response.CreatedPaymentOutput.Payment.Status == "CAPTURED")
                                    || (response.CreatedPaymentOutput.Payment.Status == "PAID"))
                                {
                                    
                                    SetOrderAsPaid(order, response.CreatedPaymentOutput.Payment.Id,
                                        response.CreatedPaymentOutput.Payment.Status);
                                    
                                }
                            }
                        }
                        return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });
                    }
                    else
                    {
                        _logger.InsertLog(LogLevel.Warning, "EPayments PaymentHandler: MAC mismatch" , "OrderID: " + order.Id.ToString());
                    }
                }
                else
                {
                    return RedirectToRoute("HomePage");
                }
            }
            catch (Exception ex)
            {
                _logger.InsertLog(LogLevel.Error, ex.Message, ex.InnerException.Message);
            }


            return RedirectToAction("Index", "Home", new { area = "" }); ;

            //return RedirectToRoute("HomePage");
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
            string apiKeyId = _ePaymentsPaymentSettings.ApiKeyId;
            string secretApiKey = _ePaymentsPaymentSettings.SecretApiKey;

            CommunicatorConfiguration configuration = Factory.CreateConfiguration(apiKeyId, secretApiKey);
            return Factory.CreateClient(configuration);
        }
        private async Task<GetHostedCheckoutResponse> GetResponse(string hostedCheckoutId)
        {
            using (Client client = GetClient())
            {
                string merchantID = _ePaymentsPaymentSettings.MerchantId;
                GetHostedCheckoutResponse response = await client.Merchant(merchantID).Hostedcheckouts().Get(hostedCheckoutId);
                return response;
            }
        }
        #endregion
    }
}