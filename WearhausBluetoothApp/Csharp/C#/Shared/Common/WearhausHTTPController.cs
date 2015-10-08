using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Serialization.Json;
using Windows.Data.Json;


namespace WearhausServer
{
    public class WearhausHttpController
    {

        public static Dictionary<string, FirmwareObj> FirmwareTable = new Dictionary<string, FirmwareObj>{
            {"000001000AFFFF56150000000000000000", 
            new FirmwareObj("", "000001000AFFFF56150000000000000000", "1.0.0", @"Base Firmware Version", 
                1, 1, 1, 1, 1, 1, "https://s3.amazonaws.com/wearhausfw/version615.dfu", new string[1] {"Any"}) 
            },

            {"000001000AFFFF11000000000000000000",
            new FirmwareObj("", "000001000AFFFF11000000000000000000", "1.1.0", @"Firmware Version 1.1.0 adds the ability to use the Aux cable as 
            an audio source while the Arc is on and/or broadcasting, along with other changes.",
                8, 8, 1, 1, 1, 1, "", new string[2] {"1.0.0", "Any"}) 
            },

            {"????",
            new FirmwareObj("", "????", "1.2.0", @"Firmware Version 1.2.0 enables the bluetooth Microphone and be able to handle phone
            calls and other uses of the mic during normal headphone operation, along with other changes.",
                8, 8, 1, 1, 1, 1, "", new string[3] {"1.1.0", "1.0.0", "Any"}) 
            }
        };


#if DEBUG
        private const string WEARHAUS_URI = "http://wearhausapistaging.herokuapp.com/v1.2/";
#else
        private const string WEARHAUS_URI = "http://wearhausapi.herokuapp.com/v1.2/";
#endif
        private const string PATH_ACCOUNT_CREATE = "account/create";
        private const string PATH_ACCOUNT_VERIFY_GUEST = "account/verify_guest";
        private const string PATH_ACCOUNT_VERIFY_CREDENTIALS = "account/,verify_credentials";
        private const string PATH_ACCOUNT_UPDATE_HID = "account/update_hid";
        private const string PATH_ACCOUNT_VERIFY_EMAIL = "account/verify_email";
        private const string PATH_ACCOUNT_UPDATE_PROFILE = "account/update_profile";
        private const string PATH_ACCOUNT_FORGOT_PASSWORD = "account/forgot_password";
        private const string PATH_ACCOUNT_FORGOT_PASSWORD_LOGIN = "account/forgot_password_login";

        private const string PATH_HEADPHONES_DFU_REPORT = "headphones/dfu_report";

        private const string PATH_USERS_SHOW = "users/forgot_password_login";
        private const string PATH_USERS_PRIVATE_PROFILE = "users/forgot_password_login";

        private const string PATH_FRIENDS_REQUEST = "friends/forgot_password_login";
        private const string PATH_FRIENDS_REMOVE = "friends/forgot_password_login";
        private const string PATH_FRIENDS_IDS = "friends/forgot_password_login";

        private string HID;
        private string User_id;
        private string Token;

        public string Old_fv { get; set; }
        public string Current_fv { get; set; }
        public string Attempted_fv { get; set; }
        // Last Successful Reponse from an HTTP Request
        public string LastHttpResponse { get; private set; }

        public WearhausHttpController(string deviceID)
        {
            HID = WearhausHttpController.ParseHID(deviceID);
            User_id = null;
            Token = null;

            Old_fv = null;
            Current_fv = null;
            Attempted_fv = null;
            LastHttpResponse = null;
        }

        public async Task<byte[]> DownloadDfuFile(string url)
        {
            byte[] fileArr;
            using (var client = new HttpClient())
            {
                try
                {
                    HttpResponseMessage responseFile = await client.GetAsync(url);
                    fileArr = await responseFile.Content.ReadAsByteArrayAsync();
                }
                catch (Exception e)
                {
                    System.Diagnostics.Debug.WriteLine("Exception in HttpGet response:" + e.Message);
                    fileArr = null;
                }
            }
            return fileArr;
        }

        public async Task<string> CreateNewUser(string email, string password)
        {
            var param = new Dictionary<string, string>{
                {"email", email},
                {"password", password}
            };
            
            string resp = await HttpPost(PATH_ACCOUNT_CREATE, param);
            Token = ParseJsonResp("token", resp);
            return resp; 
        }

        public async Task<string> CreateGuest()
        {
            var vals = new Dictionary<string, string>{
                {"hid", HID}
            };

            string resp = await HttpPost(PATH_ACCOUNT_VERIFY_GUEST, vals);
            User_id = ParseJsonResp("guest_user_id", resp);
            Token = ParseJsonResp("token", resp);
            return resp;
        }

        public async Task<string> VerifyCredentials(string email, string password)
        {
            var vals = new Dictionary<string, string>{
                {"email", email},
                {"password", password},
                {"hid", HID}
            };

            string resp = await HttpPost(PATH_ACCOUNT_VERIFY_CREDENTIALS, vals);
            return resp;
        }

        public async Task<string> DfuReport(string dfu_status, bool dfu_success)
        {
            var vals = new Dictionary<string, string>{
                {"token", Token},
                {"old_fv", Old_fv},
                {"new_fv", Current_fv},
                {"attempted_fv", Attempted_fv},
                {"dfu_status", dfu_status},
#if WINDOWS_PHONE_APP
                {"device", "windows_phone"}
#else
                {"device", "windows_desktop"}
#endif
            };
            string resp = await HttpPost(PATH_HEADPHONES_DFU_REPORT, vals);
            return resp;

        }
        
        private async Task<string> HttpPost(string destination, Dictionary<string, string> values)
        {
            using (var client = new HttpClient()){
                try
                {
                    var content = new FormUrlEncodedContent(values);
                    var response = await client.PostAsync(WEARHAUS_URI + destination, content);
                    var responseString = await response.Content.ReadAsStringAsync();
                    LastHttpResponse = responseString;
                    return responseString;
                }
                catch (Exception e)
                {
                    return "Exception in HttpPost response:" + e.Message;
                }
            }
        }

        private async Task<string> HttpGet(string destination)
        {
            using (var client = new HttpClient())
            {
                try
                {
                    var responseString = await client.GetStringAsync(WEARHAUS_URI + destination);
                    LastHttpResponse = responseString;
                    return responseString;
                }
                catch (Exception e)
                {
                    return "Exception in HttpGet response:" + e.Message;
                }
            }
        }

        public static string ParseJsonResp(string key, string jsonResp)
        {
            JsonObject x = JsonObject.Parse(jsonResp);
            string tempVal = null;
            string status = null;
            try
            {
                tempVal = x[key].GetString();
                status = x["status"].GetString();
                switch (Convert.ToInt32(status))
                {
                    case 0:
                        break;

                    case 1:
                        return "Error: bad or missing token";

                    case 2:
                        return "Error: bad or missing user_id";

                    case 3:
                        return "Error: bad or missing HID";

                    case 4:
                        return "Error: bad or missing email";

                    case 5:
                        return "Error: bad or missing session_id";

                    case 6:
                        return "Error: authentication failed";

                    case 7:
                        return "Error: email taken";

                    case 8:
                        return "Error: fb_id taken";

                    case 9:
                        return "Error: bad song_id";

                    case 10:
                        return "Error: facebook token failed at being authenticated";

                    case 11:
                        return "Error: given user_id is a guest! Either requested action is forbidden for guests, or no profile to return";

                    case 12:
                        return "Error: bad param";

                    case 13:
                        return "Error: username already taken";

                    case 100: 
                        return "Error: need one or both fb_id and password to be set at all times";

                    case 102: 
                        return "new song object created. please upload image to S3 and call next server endpoint";

                    case 104: 
                        return "Error: bad song metadata. Couldn't create song_id";

                    case 111: 
                        return "Error: fb_id not tied to any account. Use create account";

                    case 131: 
                        return "Error: station not on server";

                    case 132: 
                        return "Error: station in wrong state; is currently idle";

                    case 133: 
                        return "Error: station in wrong state; is currently listening";

                    case 134: 
                        return "station/session exists, but may be stale (4+ hours since last update). Don't render metadata on app";

                    case 140: 
                        return "Error: station is private; either master_user can't be found, or not friends with master_user";

                    case 150: 
                        return "Error: friend request already sent";

                    case 151: 
                        return "Error: already friends";

                    case 180: 
                        return "Error: bad profile_pic_url. Needs to be image on S3 or facebook url";

                    case 181: 
                        return "Error: Can't set password. An email needs to be set first";

                    case 184: 
                        return "Error: No password has been set; use facebook to login to account";

                    default:
                        return "Error: unknown error";

                }
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("Exception in ParseJson: " + e.Message);
                return null;
            }
            return tempVal;
        }


        public static string ParseHID(string chatserviceinfoID)
        {
            string[] words = chatserviceinfoID.Split('_')[1].Split('&');
            return words[words.Length - 1];
        }

        public static string ParseFirmwareVersion(byte[] payload)
        {
            string firmwareStr = "";
            firmwareStr = BitConverter.ToString(payload).Replace("-", string.Empty);
            return firmwareStr;
        }

    }
}
