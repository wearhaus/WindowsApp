using System;
using System.Collections.Generic;
using System.Text;

namespace WearhausServer
{
    public class Firmware
    {

        public static Dictionary<string, Firmware> FirmwareTable = new Dictionary<string, Firmware>{
            {"000001000AFFFF56150000000000000000", 
            new Firmware("", "000001000AFFFF56150000000000000000", "1.0.0", @"Base Firmware Version", 
                1, 1, 1, 1, 1, 1, "https://s3.amazonaws.com/wearhausfw/version615.dfu", new string[1] {"Any"}) 
            },

            {"000001000AFFFF11000000000000000000",
            new Firmware("", "000001000AFFFF11000000000000000000", "1.1.0", @"Firmware Version 1.1.0 adds the ability to use the Aux cable as 
            an audio source while the Arc is on and/or broadcasting, along with other changes.",
                8, 8, 1, 1, 1, 1, "", new string[2] {"1.0.0", "Any"}) 
            },

            {"????",
            new Firmware("", "????", "1.2.0", @"Firmware Version 1.2.0 enables the bluetooth Microphone and be able to handle phone
            calls and other uses of the mic during normal headphone operation, along with other changes.",
                8, 8, 1, 1, 1, 1, "", new string[3] {"1.1.0", "1.0.0", "Any"}) 
            }
        };

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

        public Firmware(string dateReleased, string fullCode, string humanName, string desc, int androidRecVC, int androidMinVC, int iosRecVC, int iosMinVC, int windowsRecVC, int windowsMinVC, string url, string[] validBases)
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
