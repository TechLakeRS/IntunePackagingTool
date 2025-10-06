namespace IntunePackagingTool.Models
{
    public class CatalogResult
    {
        public bool Success { get; set; }
        public string CatalogPath { get; set; } = "";
        public string Hash { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
        public string LogOutput { get; set; } = "";
    }
}