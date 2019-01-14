using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Mvc.ModelBinding;
using Nop.Web.Framework.Mvc.Models;

namespace Nop.Plugin.Tax.Zip2Tax.Models
{
    public class ConfigurationModel : BaseNopModel
    {
        [NopResourceDisplayName("Plugins.Tax.Zip2Tax.Fields.Url")]
        public string Url { get; set; }
        [NopResourceDisplayName("Plugins.Tax.Zip2Tax.Fields.Username")]
        public string Username { get; set; }
        [NopResourceDisplayName("Plugins.Tax.Zip2Tax.Fields.Password")]
        public string Password { get; set; }
        
    }
}