using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Mvc.ModelBinding;
using Nop.Web.Framework.Mvc.Models;

namespace Nop.Plugin.Payments.ePaymentsACH.Models
{
    public class PaymentInfoModel : BaseNopModel
    {
        [NopResourceDisplayName("Payment.ACH.AccountholderName")]
        public string AccountholderName { get; set; }

        [NopResourceDisplayName("Payment.ACH.RoutingNbr")]
        public string RoutingNbr { get; set; }

        [NopResourceDisplayName("Payment.ACH.AccountNbr")]
        public string AccountNbr { get; set; }

        public bool TermsAgreement { get; set; }
    }
}