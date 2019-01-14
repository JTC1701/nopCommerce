using System;
using FluentValidation;
using Nop.Plugin.Payments.ePaymentsACH.Models;
using Nop.Services.Localization;
using Nop.Web.Framework.Validators;

namespace Nop.Plugin.Payments.ePaymentsACH.Validators
{
    public partial class PaymentInfoValidator : BaseNopValidator<PaymentInfoModel>
    {
        public PaymentInfoValidator(ILocalizationService localizationService)
        {
            
            RuleFor(x => x.AccountNbr).Matches(@"^[0-9]{1,30}$").WithMessage("Account Number Invalid");
            RuleFor(x => x.RoutingNbr).Matches(@"^[0-9]{1,15}$").WithMessage("Routing Number Invalid");
            RuleFor(x => x.AccountholderName).NotEmpty().WithMessage("Accountholder Name Required");
          //  RuleFor(x => x.TermsAgreement).Equal(true).WithMessage("You must Agree to the Terms");
        }
    }
}
