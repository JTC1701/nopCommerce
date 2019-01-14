using Nop.Core.Configuration;

namespace Nop.Plugin.Payments.ePayments
{
    /// <summary>
    /// Represents settings of the ePayments payment plugin
    /// </summary>
    public class EPaymentsPaymentSettings : ISettings
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
        /// Gets or sets the Return URL after payment is processed
        /// </summary>
        public string ReturnURL { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to pass info about purchased items
        /// </summary>
        public bool PassProductNamesAndTotals { get; set; }

        /// <summary>
        /// Gets or sets an additional fee
        /// </summary>
        public decimal AdditionalFee { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to "additional fee" is specified as percentage. true - percentage, false - fixed value.
        /// </summary>
        public bool AdditionalFeePercentage { get; set; }
    }
}

