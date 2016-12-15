using System;
using System.Collections.Generic;
using System.Text;

namespace Common
{
    class ArcUtil
    {

        public static string ParseFirmwareVersion(byte[] payload)
        {
            string firmwareStr = "";
            firmwareStr = BitConverter.ToString(payload).Replace("-", string.Empty);
            return firmwareStr;
        }

        public static string GetUniqueCodeFromFull(String fv_full)
        {
            if (fv_full != null && fv_full.Length == FV_Full_code_length)
            {
                return fv_full.Replace("000001000AFFFF", "").Replace("0000000000000000", "");
            }
            return null;


        }

        // format '000001000AFFFF12000000000000000000'
        public static readonly int FV_Full_code_length = 34;



        public static string ParseHID(string chatserviceinfoID)
        {
            System.Diagnostics.Debug.WriteLine("ParseHID: " + chatserviceinfoID);

            // now it is in form: "Bluetooth#Bluetooth18:5e:0f:3d:ec:a2-1c:f0:3e:00:50:51#RFCOMM:00000000:{00001107-d102-11e1-9b23-00025b00a5a5}"
            // not sure what the old form is, maybe it changed with the Windows 10 Anniversary update?
            // So split by #, then get [1], which is Bluetooth18:5e:0f:3d:ec:a2-1c:f0:3e:00:50:51
            // Then split by -, [1], then remove :, and we get 1cf03e005051, and remove the prefix (TODO check, does the server want the prefix removed right?)
            // We detect if it is this alternate form by checking for lack of '&' and '_' and for existence of '#'

            try
            {

                if (chatserviceinfoID.Contains("_") && chatserviceinfoID.Contains("&"))
                {

                    string[] words = chatserviceinfoID.Split('_');
                    var sub_words = words[words.Length - 2].Split('&');
                    return sub_words[sub_words.Length - 1];
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("ParseHID: new method");
                    // "Bluetooth#Bluetooth18:5e:0f:3d:ec:a2-1c:f0:3e:00:50:51#RFCOMM:00000000:{00001107-d102-11e1-9b23-00025b00a5a5}"
                    string[] words = chatserviceinfoID.Split('#');
                    var sub_words = words[1].Split('-')[1];
                    System.Diagnostics.Debug.WriteLine("  sub_words: " + sub_words);
                    // "1c:f0:3e:00:50:51"
                    return sub_words.Replace(":", "").ToUpperInvariant();
                }
            }
            catch (Exception e)
            {
                // TODO send message to server so we can see what format it was in, send as part of Dfu attempt
                System.Diagnostics.Debug.WriteLine("Error trying to parse Hid: " + e);
                return null;
            }
        }



    }
}
