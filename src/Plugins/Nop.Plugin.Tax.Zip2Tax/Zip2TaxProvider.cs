using System;
using System.IO;
using System.Net;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Core.Plugins;
using Nop.Core.Domain.Logging;
using Nop.Plugin.Tax.Zip2Tax;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Tax;
using System.Xml;
using System.Xml.Linq;
using Nop.Services.Logging;
using System.Collections.Generic;
using Nop.Services.Common;
using Nop.Core.Domain.Common;

namespace Nop.Plugin.Tax.Zip2Tax
{
    /// <summary>
    /// Zip2Tax tax provider
    /// </summary>
    public class Zip2TaxProvider : BasePlugin, ITaxProvider
    {
        private const string TAXRATE_KEY = "Nop.plugins.tax.zip2tax.taxratebyzip-{0}";
        #region Fields

        private readonly ISettingService _settingService;
        private readonly IStaticCacheManager _cacheManager;
        private readonly ITaxCategoryService _taxCategoryService;
        private readonly IWebHelper _webHelper;
        private readonly Zip2TaxSettings _zip2TaxSettings;
        private readonly ILogger _logger;
        private readonly IAddressAttributeService _addressAttributeService;
        private readonly ILocalizationService _localizationService;

        #endregion

        #region Ctor

        public Zip2TaxProvider(Zip2TaxSettings zip2TaxSettings,
            ISettingService settingService,
            IStaticCacheManager cacheManager,
            ITaxCategoryService taxCategoryService,
            IWebHelper webHelper,
            ILogger logger,
            IAddressAttributeService addressAttributeService,
            ILocalizationService localizationService)
        {

            this._settingService = settingService;
            this._cacheManager = cacheManager;
            this._taxCategoryService = taxCategoryService;
            this._webHelper = webHelper;
            this._zip2TaxSettings = zip2TaxSettings;
            this._logger = logger;
            this._addressAttributeService = addressAttributeService;
            this._localizationService = localizationService;
        }

        #endregion

        #region Utilities
        
        #endregion

        #region Methods

        /// <summary>
        /// Gets tax rate
        /// </summary>
        /// <param name="calculateTaxRequest">Tax calculation request</param>
        /// <returns>Tax</returns>
        public CalculateTaxResult GetTaxRate(CalculateTaxRequest calculateTaxRequest)
        {
            var result = new CalculateTaxResult();

            if (calculateTaxRequest.Address == null)
            {
                result.Errors.Add("Address is not set");
                return result;
            }
            // check for tax exempt attribute.  Return zero if tax exempt.
            if (!string.IsNullOrEmpty(calculateTaxRequest.Address.CustomAttributes))
            {
                AddressAttributeParser aap = new AddressAttributeParser(_addressAttributeService, _localizationService);
                int TaxExemptAttrId = -1;
                IList<AddressAttribute> aaList;
                //find Tax Exempt attribute Id
                aaList = aap.ParseAddressAttributes(calculateTaxRequest.Address.CustomAttributes);
                if (aaList.Count > 0)
                {
                    foreach (AddressAttribute aa in aaList)
                    {
                        if (aa.Name.ToLower() == "tax exempt")
                        {
                            TaxExemptAttrId = aa.Id; break;
                        }
                    }

                }

                if (TaxExemptAttrId != -1) //Tax Exempt found
                {
                    //IList<AddressAttributeValue> aav;
                    //aav = aap.ParseAddressAttributeValues(calculateTaxRequest.Address.CustomAttributes);/

                    IList<string> values;
                    values = aap.ParseValues(calculateTaxRequest.Address.CustomAttributes, TaxExemptAttrId);
                    if (values.Count > 0)
                    {
                        if (values[0] == "1") // 1 = checkbox "Yes" checked
                        {
                            result.TaxRate = 0;
                            return result;
                        }

                    }
                }

            }

            var cacheKey = string.Format(TAXRATE_KEY,
                !string.IsNullOrEmpty(calculateTaxRequest.Address.ZipPostalCode) ? calculateTaxRequest.Address.ZipPostalCode : string.Empty);

            if (_cacheManager.IsSet(cacheKey)) {
                result.TaxRate = _cacheManager.Get<decimal>(cacheKey);
                return result;
            }

            string url = _zip2TaxSettings.Url;
            string shippingTaxable;
            string strxml;
            //the tax rate calculation by fixed rate
            if (!String.IsNullOrEmpty(calculateTaxRequest.Address.ZipPostalCode))
            {
                try
                {
                    url += calculateTaxRequest.Address.ZipPostalCode; //append zip to url as last parm
                    //result.TaxRate = getZip2Tax(url);
                    ServicePointManager.Expect100Continue = true;
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
                    // ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };

                    using (WebClient client = new WebClient())
                    {
                        client.Headers.Add(HttpRequestHeader.Accept, "application/xml");
                        strxml = client.DownloadString(url);
                    }
                    XDocument taxInfo = XDocument.Parse(strxml);
                    XElement generalElement = taxInfo.Element("zip2tax.com");

                    // Parse xml
                    String errorCode = generalElement.Element("error_code").Value;
                    String errorInfo = generalElement.Element("error_message").Value;


                    if (errorCode.Equals("0"))
                    {
                        string taxrateStr = generalElement.Element("rate").Value;
                        result.TaxRate = Convert.ToDecimal(taxrateStr);
                        shippingTaxable = generalElement.Element("shippingtaxable").Value;
                        _cacheManager.Set(cacheKey, result.TaxRate, 60);
                        return result;
                    }
                    else
                    {
                        result.Errors.Add("Error returned from Zip2Tax: Error Code=" + errorCode + "; Error Message=" + errorInfo);
                        _logger.InsertLog(LogLevel.Error, "Error returned from Zip2Tax: Error Code = " + errorCode + "; Error Message=" + errorInfo + "; ZIP=" + calculateTaxRequest.Address.ZipPostalCode);
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    string err = "Zip2Tax Exception Raised: " + ex.Message;
                    result.Errors.Add(err);
                    _logger.InsertLog(LogLevel.Error, "Zip2Tax Exception Raised for ZIP: " + calculateTaxRequest.Address.ZipPostalCode + "; Message: " + ex.Message);
                    return result;
                }

            }
            else
            {
                result.Errors.Add("Zip code not valid");
                return result;
            }
        }

        /// <summary>
        /// Gets a configuration page URL
        /// </summary>
        public override string GetConfigurationPageUrl()
        {
            return $"{_webHelper.GetStoreLocation()}Admin/Zip2Tax/Configure";
        }

        /// <summary>
        /// Install plugin
        /// </summary>
        public override void Install()
        {

            //settings
            _settingService.SaveSetting(new Zip2TaxSettings());

            //locales
            this.AddOrUpdatePluginLocaleResource("Plugins.Tax.Zip2Tax.Fields.Url", "Url");
            this.AddOrUpdatePluginLocaleResource("Plugins.Tax.Zip2Tax.Fields.Url.Hint", "This is the specific GET from Zip2Tax. The application will append the zip code parm");
            this.AddOrUpdatePluginLocaleResource("Plugins.Tax.Zip2Tax.Fields.Username", "Username");
            this.AddOrUpdatePluginLocaleResource("Plugins.Tax.Zip2Tax.Fields.Password", "Password");

            base.Install();
        }

        /// <summary>
        /// Uninstall plugin
        /// </summary>
        public override void Uninstall()
        {
            //settings
            _settingService.DeleteSetting<Zip2TaxSettings>();

            //locales
            this.DeletePluginLocaleResource("Plugins.Tax.Zip2Tax.Fields.Url");
            this.DeletePluginLocaleResource("Plugins.Tax.Zip2Tax.Fields.Username");
            this.DeletePluginLocaleResource("Plugins.Tax.Zip2Tax.Fields.Password");

            base.Uninstall();
        }

        #endregion
    }
}