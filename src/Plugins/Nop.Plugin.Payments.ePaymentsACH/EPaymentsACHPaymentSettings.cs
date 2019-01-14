using Nop.Core.Configuration;
using Ingenico.Connect.Sdk.Webhooks;


namespace Nop.Plugin.Payments.ePaymentsACH
{
    /// <summary>
    /// Represents settings of the ePayments payment plugin
    /// </summary>
    public class EPaymentsACHPaymentSettings : ISettings
    {
        /// <summary>
        /// Gets or sets a value for the Merchant ID
        /// </summary>
        public string MerchantId { get; set; }

        /// <summary>
        /// Gets or sets ApiKeyId identity token
        /// </summary>
        public string ApiKeyId { get; set; }

        /// <summary>
        /// Gets or sets SecretApiKey token
        /// </summary>
        public string SecretApiKey { get; set; }

        /// <summary>
        /// Gets or sets WebhookURL address
        /// </summary>
        public string WebhookURL { get; set; }

        /// <summary>
        /// Gets or sets Webhook Key ID
        /// </summary>
        public string WebhookKeyId { get; set; }
        /// <summary>
        /// Gets or sets Webhook Secret Key
        /// </summary>
        public string WebhookSecretKey { get; set; }

    }
   
}
