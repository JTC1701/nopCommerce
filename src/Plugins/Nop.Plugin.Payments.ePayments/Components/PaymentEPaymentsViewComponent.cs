using Microsoft.AspNetCore.Mvc;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Payments.ePayments.Components
{
    [ViewComponent(Name = "PaymentEPayments")]
    public class PaymentEPaymentsViewComponent : NopViewComponent
    {
        public IViewComponentResult Invoke()
        {
            return View("~/Plugins/Payments.ePayments/Views/PaymentInfo.cshtml");
        }
    }
}
