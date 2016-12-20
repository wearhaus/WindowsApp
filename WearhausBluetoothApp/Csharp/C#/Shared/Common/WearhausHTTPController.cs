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


#if DEBUG
        private const string WEARHAUS_URI = "http://wearhausapistaging.herokuapp.com/v1.3/";
#else
        private const string WEARHAUS_URI = "http://wearhausapi.herokuapp.com/v1.3/";
#endif
        private const string PATH_ACCOUNT_CREATE = "account/create";
        private const string PATH_ACCOUNT_VERIFY_GUEST = "account/create_guest";
        private const string PATH_ACCOUNT_VERIFY_CREDENTIALS = "account/,verify_credentials";
        private const string PATH_ACCOUNT_UPDATE_HID = "account/update_hid";
        private const string PATH_ACCOUNT_VERIFY_EMAIL = "account/verify_email";
        private const string PATH_ACCOUNT_UPDATE_PROFILE = "account/update_profile";
        private const string PATH_ACCOUNT_FORGOT_PASSWORD = "account/forgot_password";
        private const string PATH_ACCOUNT_FORGOT_PASSWORD_LOGIN = "account/forgot_password_login";

        private const string PATH_HEADPHONES_DFU_REPORT = "headphones/dfu_report";
        private const string PATH_HEADPHONES_LOGIN = "headphones/login";
        private const string PATH_FIRMWARE_TABLE = "headphones/firmware_table";

        private const string PATH_USERS_SHOW = "users/forgot_password_login";
        private const string PATH_USERS_PRIVATE_PROFILE = "users/forgot_password_login";

        private const string PATH_FRIENDS_REQUEST = "friends/forgot_password_login";
        private const string PATH_FRIENDS_REMOVE = "friends/forgot_password_login";
        private const string PATH_FRIENDS_IDS = "friends/forgot_password_login";


        // STH this should not be where HID is stored; this should only store server related userId and token
        // hid, fv, etc. should only be with the arclink object

        //private string HID;
        //private string Fv_full_code;

        private string User_id;
        private string Acc_token;
        private string Hid_token;

        // TODO These shouldn't be here, move later when we can
        public string Old_fv { get; set; }
        public string Current_fv { get; set; }
        public string Attempted_fv { get; set; }



        // to be moved or removed
        //public string Old_fv { get; set; }
        //public string Current_fv { get; set; }
        //public string Attempted_fv { get; set; }


        // Last Successful Reponse from an HTTP Request
        public string LastHttpResponse { get; private set; }


        // TODO, later, when we can imrpove UI in this branch, include these again.
        /*public enum AccountState
        {
            None,
            Loading,
            // Both guest account and hid have been registered with server
            ValidGuest,
            Error,
        };

        public AccountState MyAccountState { get; private set; }

        // subscribe/unsubscribe with myArcLink.ArcConnStateChanged += myListenerMethod; 
        // static void myListenerMethod(object sender, EventArgs e) {}
        // myListenerMethod should read MyArcConnState and ErrorHuman and updateUI or logic
        public event EventHandler AccountStateChanged;

        protected virtual void onAccountStateChanged()
        {
            System.Diagnostics.Debug.WriteLine("AccountState has changed: " + MyAccountState);
            AccountStateChanged?.Invoke(this, null);
        }*/




        public WearhausHttpController()
        {
            //HID = WearhausHttpController.ParseHID(deviceID);
            User_id = null;
            Acc_token = null;
            Hid_token = null;
            //MyAccountState = AccountState.None;

            //Old_fv = null;
            //Current_fv = null;
            //Attempted_fv = null;
            LastHttpResponse = null;
        }

        // Need to pass in an ArcLink that this controller will represent
        // returns true if success, false otherwise
        public async Task<Boolean> startServerRegistration(string hid, String fv_full_code)
        {
            //if (arcLink.MyArcConnState != ArcLink.ArcConnState.Connected)
            //{
            //    System.Diagnostics.Debug.WriteLine("Programmer Error: Cannot connect to server until an Arc is fully connected");
            //    return;
            //}

            //if (MyAccountState != AccountState.None && MyAccountState != AccountState.Error)
            //{
            //    System.Diagnostics.Debug.WriteLine("Programmer Error: Should not attempt another startServerRegistration in current AccountState: " + MyAccountState);
            //    return;
            //}

            //System.Diagnostics.Debug.WriteLine("HTTPController startServerRegistration");
            //MyAccountState = AccountState.Loading;
            //onAccountStateChanged();


            try
            {
                Boolean successfulFVTable = await GetLatestFirmwareTable();
                if (!successfulFVTable)
                {
                    errorConnectingToServer();
                    return false;
                }


                string guestResp = await HttpPost(PATH_ACCOUNT_VERIFY_GUEST, new Dictionary<string, string> { });
                System.Diagnostics.Debug.WriteLine("guestResp  " + guestResp);

                JsonObject x = JsonObject.Parse(guestResp);
                int status = Convert.ToInt32(x["status"].GetString());
                if (status != 0)
                {
                    System.Diagnostics.Debug.WriteLine("Non zero status returned from createGuest");
                    errorConnectingToServer();
                    return false;
                }
                User_id = x["guest_user_id"].GetString();
                Acc_token = x["acc_token"].GetString();

                var hidVals = new Dictionary<string, string>{
                    {"acc_token", Acc_token},
                    {"hid", hid},
                    {"fv_full_code", fv_full_code},
                    {"gcm_reg_id", ""}
                };

                string hidResp = await HttpPost(PATH_HEADPHONES_LOGIN, hidVals);
                System.Diagnostics.Debug.WriteLine("hidResp  " + hidResp);


                JsonObject x2 = JsonObject.Parse(hidResp);
                int status2 = Convert.ToInt32(x2["status"].GetString());
                if (status2 != 0)
                {
                    System.Diagnostics.Debug.WriteLine("Non zero status returned from login headphones");
                    errorConnectingToServer();
                    return false;
                }

                Hid_token = x2["hid_token"].GetString();


                //MyAccountState = AccountState.ValidGuest;
                //onAccountStateChanged();
                return true;

            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("Error parsing server response: " + e.ToString());
                errorConnectingToServer();
                return false;
            }

        }

        private void errorConnectingToServer()
        {
            System.Diagnostics.Debug.WriteLine("errorConnectingToServer");
            User_id = null;
            Acc_token = null;
            Hid_token = null;
            //MyAccountState = AccountState.Error;
            //onAccountStateChanged();

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

        // populates static tables in Firmware class
        // returns success or failure boolean
        public async Task<Boolean> GetLatestFirmwareTable()
        {
            try
            {
                var param = new Dictionary<string, string>();
                string resp = await HttpPost(PATH_FIRMWARE_TABLE, param);
                System.Diagnostics.Debug.WriteLine("resp" + resp);

                JsonObject x = JsonObject.Parse(resp);
                JsonObject f = x.GetNamedObject("firmware");
                JsonObject lastestByProductId = x.GetNamedObject("latest_by_product_id");
                //string latestVer = x["latest"].GetString();

                // Update Firmware Table
                foreach (string key in f.Keys)
                {
                    JsonObject firmwareJsonObj = f.GetNamedObject(key);


                    int android_min_vc = int.Parse(firmwareJsonObj["android_min_vc"].GetString());
                    int android_rec_vc = int.Parse(firmwareJsonObj["android_rec_vc"].GetString());
                    string desc = firmwareJsonObj["desc"].GetString();
                    string full_code = firmwareJsonObj["full_code"].GetString();
                    string human_name = firmwareJsonObj["human_name"].GetString();
                    string unique_code = firmwareJsonObj["unique_code"].GetString();
                    string url = firmwareJsonObj["url"].GetString();

                    Firmware.UrlMirror[] url_mirrors = null;
                    if (firmwareJsonObj.ContainsKey("url_mirrors"))
                    {
                        JsonArray temp_url_mirrors = firmwareJsonObj["url_mirrors"].GetArray();
                        url_mirrors = new Firmware.UrlMirror[temp_url_mirrors.Count];

                        for (int i = 0; i < temp_url_mirrors.Count; i++)
                        {
                            String name = temp_url_mirrors[i].GetObject()["name"].GetString();
                            String iso_3166_1_alpha_2 = temp_url_mirrors[i].GetObject()["iso_3166_1_alpha_2"].GetString();
                            String url2 = temp_url_mirrors[i].GetObject()["url"].GetString();

                            url_mirrors[i] = new Firmware.UrlMirror(name, iso_3166_1_alpha_2, url2);
                        }
                    }


                    var jsonArr_valid_bases = firmwareJsonObj["valid_bases"].GetArray();
                    string[] valid_bases = new string[jsonArr_valid_bases.Count];
                    for (int i = 0; i < jsonArr_valid_bases.Count; i++)
                    {
                        valid_bases[i] = jsonArr_valid_bases[i].GetString();
                    }

                    var jsonArr_supported_product_ids = firmwareJsonObj["supported_product_ids"].GetArray();

                    int[] supported_product_ids = new int[jsonArr_supported_product_ids.Count];
                    for (int i = 0; i < jsonArr_supported_product_ids.Count; i++)
                    {

                        supported_product_ids[i] = Convert.ToInt32(jsonArr_supported_product_ids[i].GetString());


                    }

                    Firmware firmwareObj = new Firmware(full_code, human_name, unique_code, desc, android_rec_vc, android_min_vc,
                        1, 1, 1, 1, url_mirrors, url, valid_bases, supported_product_ids);
                    if (Firmware.FirmwareTable.ContainsKey(key))
                    {
                        Firmware.FirmwareTable[key] = firmwareObj;
                    }
                    else
                    {
                        Firmware.FirmwareTable.Add(key, firmwareObj);
                    }
                }



                Firmware.LatestByProductId = new Dictionary<string, string> { };

                if (lastestByProductId.ContainsKey("windows_dfu"))
                {

                    JsonObject windowsDfu = lastestByProductId.GetNamedObject("windows_dfu");
                    if (windowsDfu != null)
                    {
                        foreach (string key in windowsDfu.Keys)
                        {
                            Firmware.LatestByProductId[key] = windowsDfu[key].GetString();
                        }
                    }
                    // So if null, then this DFU app is deprecated and we should display that you should update
                    // this Windows app, or check wearhaus.com
                    // For any new version of WindowsDFU, we can just change this string to "windows_dfu1", then "windows_dfu2", etc.
                    // at the same time we release a new version
                }
                return true;
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("Error Getting FV Table: " + e.ToString());
            }
            return false;

        }

        /*public async Task<string> CreateNewUser(string email, string password)
        {
            var param = new Dictionary<string, string>{
                {"email", email},
                {"password", password}
            };

            string resp = await HttpPost(PATH_ACCOUNT_CREATE, param);
            Acc_token = ParseJsonResp("acc_token", resp);
            return resp;
        }*/

        /*public async Task<Boolean> CreateGuest()
        {
            var vals = new Dictionary<string, string>{
                //{"hid", HID} // ignored
            };

            string resp = await HttpPost(PATH_ACCOUNT_VERIFY_GUEST, vals);

            try
            {
                User_id = ParseJsonResp("guest_user_id", resp);
                Acc_token = ParseJsonResp("acc_token", resp);
            } catch (Exception e)
            {
                User_id = 
                return false;
            }
            return true;
        }*/


        /*public async Task<string> VerifyCredentials(string email, string password)
        {
            var vals = new Dictionary<string, string>{
                {"email", email},
                {"password", password},
                {"hid", HID}
            };

            string resp = await HttpPost(PATH_ACCOUNT_VERIFY_CREDENTIALS, vals);
            return resp;
        }*/

        public async Task<string> DfuReport(int dfu_status)
        {
            var vals = new Dictionary<string, string>{
                {"token", Acc_token},
                {"old_fv_full_code", Old_fv},
                {"new_fv_full_code", Current_fv},
                {"attempted_fv_full_code", Attempted_fv},
                {"dfu_status", dfu_status.ToString()},
#if WINDOWS_PHONE_APP
                {"device", "windows_phone"}
#else
                {"device", "windows_desktop"}
#endif
            };
            string resp = await HttpPost(PATH_HEADPHONES_DFU_REPORT, vals);

            string text = "SENT DFU REPORT:\n";
            foreach (KeyValuePair<string, string> kvp in vals)
            {
                //textBox3.Text += ("Key = {0}, Value = {1}", kvp.Key, kvp.Value);
                text += string.Format("\n\tKey = {0}, Value = {1}", kvp.Key, kvp.Value);
            }
            System.Diagnostics.Debug.WriteLine(text);
            System.Diagnostics.Debug.WriteLine("RESPONSE TO DFU REPORT:\n" + resp);
            return resp;

        }

        private async Task<string> HttpPost(string destination, Dictionary<string, string> values)
        {
            using (var client = new HttpClient())
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine("HTTPController connecting to url=" + destination);

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
                    System.Diagnostics.Debug.WriteLine("HTTPController connecting to url=" + destination);

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

        // H: to be deprecated; checks for bad statuses can be handled differently
        // returns status type, 0 = good, 1 = either error or special notif
        /*
        public static int ParseJsonResp(string jsonResp)
        {
            JsonObject x = JsonObject.Parse(jsonResp);
            //string tempVal = null;
            string status = null;
            try
            {
                //tempVal = x[key].GetString();
                status = x["status"].GetString();

                /*switch (Convert.ToInt32(status))
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
        */





        // TODO, this does not belong in this class, move later
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




        // format '000001000AFFFF12000000000000000000'
        public static readonly int FV_Full_code_length = 34;


        public static string GetUniqueCodeFromFull(String fv_full)
        {
            if (fv_full != null && fv_full.Length == FV_Full_code_length)
            {
                return fv_full.Replace("000001000AFFFF", "").Replace("0000000000000000", "");
            }
            return null;
        }


        public static string ParseFirmwareVersion(byte[] payload)
        {
            string firmwareStr = "";
            firmwareStr = BitConverter.ToString(payload).Replace("-", string.Empty);
            return firmwareStr;
        }

    }
}
