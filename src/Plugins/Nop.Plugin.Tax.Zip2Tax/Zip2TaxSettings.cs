using Nop.Core.Configuration;

namespace Nop.Plugin.Tax.Zip2Tax
{
    /// <summary>
    /// Represents settings of the "Zip2Tax" tax plugin
    /// </summary>
    public class Zip2TaxSettings : ISettings
    {
        /// <summary>
        /// Gets or sets the Zip2Tax URL
        /// </summary>
        public string Url { get; set; }
        /// <summary>
        /// Gets or sets the Zip2Tax account username
        /// </summary>
        public string Username { get; set; }
        /// <summary>
        /// Gets or sets the Zip2Tax account username
        /// </summary>
        public string Password { get; set; }
    }
}