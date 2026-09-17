using Microsoft.AspNetCore.Http;

namespace SNP_SubSystem.Models
{
    public class SettingsViewModel
    {
        public string SystemTitle { get; set; }
        
        public IFormFile SystemLogoFile { get; set; }
        public string SystemLogoUrl { get; set; }

        public IFormFile LoginBackgroundFile { get; set; }
        public string LoginBackgroundUrl { get; set; }

        public IFormFile HomeBackgroundFile { get; set; }
        public string HomeBackgroundUrl { get; set; }
    }
}
