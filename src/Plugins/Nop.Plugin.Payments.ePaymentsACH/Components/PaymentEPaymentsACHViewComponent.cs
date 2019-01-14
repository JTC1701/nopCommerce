using System.Net;
using Microsoft.AspNetCore.Mvc;
using Nop.Web.Framework.Components;
using Nop.Plugin.Payments.ePaymentsACH.Models;

namespace Nop.Plugin.Payments.ePaymentsACH.Components
{
    [ViewComponent(Name = "PaymentEPaymentsACH")]
    public class PaymentEPaymentsACHViewComponent : NopViewComponent
    {
        public IViewComponentResult Invoke()
        {
            var model = new PaymentInfoModel()
            {
                
            };

            //set postback values (we cannot access "Form" with "GET" requests)
            if (this.Request.Method != WebRequestMethods.Http.Get)
            {
                var form = this.Request.Form;
                model.AccountholderName = form["AccountholderName"];
                model.RoutingNbr = form["RoutingNbr"];
                model.AccountNbr = form["AccountNbr"];
               
            }
            return View("~/Plugins/Payments.ePaymentsACH/Views/PaymentInfo.cshtml",model);
        }
    }
}
