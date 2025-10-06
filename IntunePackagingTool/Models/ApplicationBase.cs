using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntunePackagingTool.Models
{
    /// <summary>
    /// Base class containing common properties for all application types
    /// </summary>
    public abstract class ApplicationBase
    {
        public string DisplayName { get; set; } = "";
        public string Version { get; set; } = "";
        public string Publisher { get; set; } = "";
        public string Description { get; set; } = "";
    }
}
