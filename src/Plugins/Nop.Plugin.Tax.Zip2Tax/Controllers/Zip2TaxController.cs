using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Services.Configuration;
using Nop.Services.Directory;
using Nop.Services.Security;
using Nop.Services.Stores;
using Nop.Services.Tax;
using Nop.Services.Localization;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc;
using Nop.Web.Framework.Mvc.Filters;
using Nop.Plugin.Tax.Zip2Tax.Models;

namespace Nop.Plugin.Tax.Zip2Tax.Controllers
{
    [AuthorizeAdmin]
    [Area(AreaNames.Admin)]
    public class Zip2TaxController : BasePluginController
    {
        #region Fields

        private readonly ICountryService _countryService;
        private readonly ILocalizationService _localizationService;
        private readonly IPermissionService _permissionService;
        private readonly ISettingService _settingService;
        private readonly IStateProvinceService _stateProvinceService;
        private readonly IStoreService _storeService;
        private readonly ITaxCategoryService _taxCategoryService;
        private readonly Zip2TaxSettings _zip2TaxSettings;

        #endregion

        #region Ctor

        public Zip2TaxController(
            ICountryService countryService,
            ILocalizationService localizationService,
            IPermissionService permissionService,
            ISettingService settingService,
            IStateProvinceService stateProvinceService,
            IStoreService storeService,
            ITaxCategoryService taxCategoryService,
            Zip2TaxSettings zip2TaxSettings)
        {
            this._countryService = countryService;
            this._localizationService = localizationService;
            this._permissionService = permissionService;
            this._settingService = settingService;
            this._stateProvinceService = stateProvinceService;
            this._storeService = storeService;
            this._taxCategoryService = taxCategoryService;
            this._zip2TaxSettings = zip2TaxSettings;
        }

        #endregion

        #region Methods

        public IActionResult Configure()
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageTaxSettings))
                return AccessDeniedView();

            var model = new ConfigurationModel
            {
                Url = _zip2TaxSettings.Url,
                Username = _zip2TaxSettings.Username,
                Password = _zip2TaxSettings.Password
            };

            return View("~/Plugins/Tax.Zip2Tax/Views/Configure.cshtml", model);
        }
        [HttpPost]
        [AuthorizeAdmin]
        [AdminAntiForgery]
        [Area(AreaNames.Admin)]
        public IActionResult Configure(ConfigurationModel model)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageTaxSettings))
                return AccessDeniedView();

            if (!ModelState.IsValid)
                return Configure();

            //save settings
            _zip2TaxSettings.Url = model.Url;
            _zip2TaxSettings.Username = model.Username;
            _zip2TaxSettings.Password = model.Password;

            /* We do not clear cache after each setting update.
             * This behavior can increase performance because cached settings will not be cleared 
             * and loaded from database after each update */
            _settingService.SaveSetting(_zip2TaxSettings);
            //now clear settings cache
            _settingService.ClearCache();

            SuccessNotification(_localizationService.GetResource("Admin.Plugins.Saved"));

            return Configure();
        }
        #endregion

    }
}