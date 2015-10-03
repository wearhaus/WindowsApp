using System;
using System.Collections.Generic;
using System.Text;

namespace WearhausServer
{
    public class FirmwareObj
    {

        public string dateReleased { get; set; }
        public string fullCode { get; set; }
        public string humanName { get; set; }
        public string desc { get; set; }
        public int androidRecVC { get; set; }
        public int androidMinVC { get; set; }
        public int iosRecVC { get; set; }
        public int iosMinVC { get; set; }
        public int windowsRecVC { get; set; }
        public int windowsMinVC { get; set; }
        public string url { get; set; }
        public string[] validBases { get; set; }

        public FirmwareObj(string dateReleased, string fullCode, string humanName, string desc, int androidRecVC, int androidMinVC, int iosRecVC, int iosMinVC, int windowsRecVC, int windowsMinVC, string url, string[] validBases)
        {
            this.dateReleased = dateReleased;
            this.fullCode = fullCode;
            this.humanName = humanName;
            this.desc = desc;
            this.androidRecVC = androidRecVC;
            this.androidMinVC = androidMinVC;
            this.iosRecVC = iosRecVC;
            this.iosMinVC = iosMinVC;
            this.windowsRecVC = windowsRecVC;
            this.windowsMinVC = windowsMinVC;
            this.url = url;
            this.validBases = validBases;
        }
    }
}
