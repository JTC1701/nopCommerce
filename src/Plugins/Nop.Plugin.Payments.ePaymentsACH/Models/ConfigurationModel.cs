using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Mvc.ModelBinding;
using Nop.Web.Framework.Mvc.Models;

namespace Nop.Plugin.Payments.ePaymentsACH.Models
{
    public class ConfigurationModel : BaseNopModel
    {
        public int ActiveStoreScopeConfiguration { get; set; }

        [NopResourceDisplayName("Plugins.Payments.ePaymentsACH.Fields.MerchantId")]
        public string MerchantId { get; set; }
        public bool MerchantId_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.ePaymentsACH.Fields.ApiKeyId")]
        public string ApiKeyId { get; set; }
        public bool ApiKeyId_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.ePaymentsACH.Fields.SecretApiKey")]
        public string SecretApiKey { get; set; }
        public bool SecretApiKey_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.ePaymentsACH.Fields.WebhookURL")]
        public string WebhookURL { get; set; }
        public bool WebhookURL_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.ePaymentsACH.Fields.WebhookKeyId")]
        public string WebhookKeyId { get; set; }
        public bool WebhookKeyId_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.ePaymentsACH.Fields.WebhookSecretKey")]
        public string WebhookSecretKey { get; set; }
        public bool WebhookSecretKey_OverrideForStore { get; set; }
    }
}
