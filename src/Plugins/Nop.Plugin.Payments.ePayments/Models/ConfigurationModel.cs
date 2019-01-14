using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Mvc.ModelBinding;
using Nop.Web.Framework.Mvc.Models;

namespace Nop.Plugin.Payments.ePayments.Models
{
    public class ConfigurationModel : BaseNopModel
    {
        public int ActiveStoreScopeConfiguration { get; set; }

        [NopResourceDisplayName("Plugins.Payments.ePayments.Fields.MerchantId")]
        public string MerchantId { get; set; }
        public bool MerchantId_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.ePayments.Fields.ApiKeyId")]
        public string ApiKeyId { get; set; }
        public bool ApiKeyId_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.ePayments.Fields.SecretApiKey")]
        public string SecretApiKey { get; set; }
        public bool SecretApiKey_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.ePayments.Fields.ReturnURL")]
        public string ReturnURL { get; set; }
        public bool ReturnURL_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.ePayments.Fields.PassProductNamesAndTotals")]
        public bool PassProductNamesAndTotals { get; set; }
        public bool PassProductNamesAndTotals_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.ePayments.Fields.AdditionalFee")]
        public decimal AdditionalFee { get; set; }
        public bool AdditionalFee_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.ePayments.Fields.AdditionalFeePercentage")]
        public bool AdditionalFeePercentage { get; set; }
        public bool AdditionalFeePercentage_OverrideForStore { get; set; }
    }
}
