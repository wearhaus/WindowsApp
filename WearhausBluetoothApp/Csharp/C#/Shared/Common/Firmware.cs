using System;
using System.Collections.Generic;
using System.Text;

namespace WearhausServer
{
    public class Firmware
    {

        // Cosntants for position and len of a "short code" for Firmware Version given a full firmware code
        // e.g. "000001000AFFFF54250000000000000000" has short code "5425"
        public const int SHORT_FV_CODE_INDEX = 14;
        public const int SHORT_FC_CODE_LEN = 4;

        // This table will be re-written upon initiating the Update but below is the temporarily initialized table
        public static Dictionary<string, Firmware> FirmwareTable = new Dictionary<string, Firmware>{
            {"5425",
            new Firmware("000001000AFFFF54250000000000000000", "1.0.0", "5425", @"Base Firmware Version",
                1, 1, 1, 1, 1, 1, null, "https://s3.amazonaws.com/wearhausfw/version425.dfu", new string[0] {}, new int[1] {0})
            },

            {"5615",
            new Firmware("000001000AFFFF56150000000000000000", "1.0.1", "5615", @"1.0.1",
                1, 1, 1, 1, 1, 1, null, "https://s3.amazonaws.com/wearhausfw/version615.dfu", new string[0] {}, new int[1] {0})
            },

            {"1100",
            new Firmware("000001000AFFFF11000000000000000000", "1.1.0", "1100", @"Firmware Version 1.1.0 adds the ability to use the Aux cable as 
            an audio source while the Arc is on and/or broadcasting, along with other changes.",
                8, 8, 1, 1, 1, 1, null, "https://s3.amazonaws.com/wearhausfw/version1100.dfu", new string[4] {"5402", "5425", "5615", "5923"}, new int[1] {0})
            }

        };

        // This cuts out different dfu methods and only stores {
        //    '0': '1200',
        //    '1': '1200',
        // },
        // from the field 'windows_dfu'
        public static Dictionary<string, string> LatestByProductId;


        public string fullCode { get; set; }
        public string humanName { get; set; }
        public string uniqueCode { get; set; }
        public string desc { get; set; }
        public int androidRecVC { get; set; }
        public int androidMinVC { get; set; }
        public int iosRecVC { get; set; }
        public int iosMinVC { get; set; }
        public int windowsRecVC { get; set; }
        public int windowsMinVC { get; set; }
        // url is deprecated, recommended to use url_mirrors
        public string url { get; set; }
        public UrlMirror[] url_mirrors { get; set; }
        public string[] validBases { get; set; }
        public int[] supportedProductIds { get; set; }

        public Firmware(string fullCode, string humanName, string uniqueCode, string desc, int androidRecVC,
            int androidMinVC, int iosRecVC, int iosMinVC, int windowsRecVC, int windowsMinVC,
            UrlMirror[] url_mirrors, String url, string[] validBases, int[] supportedProductIds)
        {
            this.fullCode = fullCode;
            this.humanName = humanName;
            this.uniqueCode = uniqueCode;
            this.desc = desc;
            this.androidRecVC = androidRecVC;
            this.androidMinVC = androidMinVC;
            this.iosRecVC = iosRecVC;
            this.iosMinVC = iosMinVC;
            this.windowsRecVC = windowsRecVC;
            this.windowsMinVC = windowsMinVC;
            this.url_mirrors = url_mirrors;
            this.url = url;
            this.validBases = validBases;
            this.supportedProductIds = supportedProductIds;
        }

        public class UrlMirror
        {
            public string name { get; set; }
            public string iso_3166_1_alpha_2 { get; set; }
            public string url { get; set; }

            public UrlMirror(String name, String iso_3166_1_alpha_2, String url)
            {
                this.name = name;
                this.iso_3166_1_alpha_2 = iso_3166_1_alpha_2;
                this.url = url;
            }
        }

    }
}
