using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IntunePackagingTool.Models
{
    public class GroupDevice 
    {
            public string Id { get; set; }
            public string DeviceName { get; set; }
            public string AzureDeviceId { get; set; }  // The Azure AD device ID
            public string UserPrincipalName { get; set; }
            public string OperatingSystem { get; set; }
            public string OSVersion { get; set; }

            public bool IsSelected { get; set; }

        public bool IsCompliant { get; set; }
            public bool AccountEnabled { get; set; }
            public DateTime LastSyncDateTime { get; set; }
            public DateTime CreatedDateTime { get; set; }

          
      
    }
}