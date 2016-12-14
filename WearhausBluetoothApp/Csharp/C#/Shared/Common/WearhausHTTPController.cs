﻿using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Serialization.Json;
using Windows.Data.Json;
using Common;

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
        


        // to be moved or removed
        //public string Old_fv { get; set; }
        //public string Current_fv { get; set; }
        //public string Attempted_fv { get; set; }


        // Last Successful Reponse from an HTTP Request
        public string LastHttpResponse { get; private set; }

        
        public enum AccountState
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
        }




        public WearhausHttpController()
        {
            //HID = WearhausHttpController.ParseHID(deviceID);
            User_id = null;
            Acc_token = null;
            Hid_token = null;
            MyAccountState = AccountState.None;

            //Old_fv = null;
            //Current_fv = null;
            //Attempted_fv = null;
            LastHttpResponse = null;
        }

        // Need to pass in an ArcLink that this controller will represent
        public async void startServerRegistration(ArcLink arcLink)
        {
            if (arcLink.MyArcConnState != ArcLink.ArcConnState.Connected)
            {
                System.Diagnostics.Debug.WriteLine("Programmer Error: Cannot connect to server until an Arc is fully connected");
                return;
            }

            if (MyAccountState != AccountState.None || MyAccountState != AccountState.Error)
            {
                System.Diagnostics.Debug.WriteLine("Programmer Error: Should not attempt another startServerRegistration in current AccountState: " + MyAccountState);
                return;
            }

            System.Diagnostics.Debug.WriteLine("HTTPController startServerRegistration");
            MyAccountState = AccountState.Loading;
            onAccountStateChanged();
            
            try
            {
                Boolean successfulFVTable = await GetLatestFirmwareTable();
                if (!successfulFVTable)
                {
                    errorConnectingToServer();
                    return;
                }


                string guestResp = await HttpPost(PATH_ACCOUNT_VERIFY_GUEST, new Dictionary<string, string> { });


                JsonObject x = JsonObject.Parse(guestResp);
                int status = (int) x["status"].GetNumber();
                if (status != 0)
                {
                    System.Diagnostics.Debug.WriteLine("Non zero status returned from createGuest");
                    errorConnectingToServer();
                    return;
                }
                User_id = x["guest_user_id"].GetString();
                Acc_token = x["acc_token"].GetString();

                var hidVals = new Dictionary<string, string>{
                    {"acc_token", Acc_token},
                    {"hid", arcLink.HID},
                    {"fv_full_code", arcLink.Fv_full_code},
                    {"gcm_reg_id", ""}
                };

                string hidResp = await HttpPost(PATH_HEADPHONES_LOGIN, hidVals);


                JsonObject x2 = JsonObject.Parse(guestResp);

                int status2 = (int)x2["status"].GetNumber();
                if (status2 != 0)
                {
                    System.Diagnostics.Debug.WriteLine("Non zero status returned from createGuest");
                    errorConnectingToServer();
                    return;
                }

                Hid_token = x2["hid_token"].GetString();


                MyAccountState = AccountState.ValidGuest;
                onAccountStateChanged();

            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("Error parsing server response: " + e.HResult.ToString());
                errorConnectingToServer();
                return;
            }

        }

        private void errorConnectingToServer()
        {
            System.Diagnostics.Debug.WriteLine("errorConnectingToServer");
            User_id = null;
            Acc_token = null;
            Hid_token = null;
            MyAccountState = AccountState.Error;
            onAccountStateChanged();

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

                JsonObject x = JsonObject.Parse(resp);
                JsonObject f = x.GetNamedObject("firmware");
                JsonObject lastestByProductId = x.GetNamedObject("latest");
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

                    JsonArray temp_url_mirrors = firmwareJsonObj["url_mirrors"].GetArray();
                    Firmware.UrlMirror[] url_mirrors = new Firmware.UrlMirror[temp_url_mirrors.Count];
                    for (int i = 0; i < temp_url_mirrors.Count; i++)
                    {
                        String name = temp_url_mirrors[i].GetObject()["name"].GetString();
                        String iso_3166_1_alpha_2 = temp_url_mirrors[i].GetObject()["iso_3166_1_alpha_2"].GetString();
                        String url2 = temp_url_mirrors[i].GetObject()["url"].GetString();

                        url_mirrors[i] = new Firmware.UrlMirror(name, iso_3166_1_alpha_2, url2);
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
                        supported_product_ids[i] = (int)jsonArr_supported_product_ids[i].GetNumber();
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
            } catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("Error Getting FV Table: " + e.HResult.ToString());
            }
            return false;

        }

        public async Task<string> CreateNewUser(string email, string password)
        {
            var param = new Dictionary<string, string>{
                {"email", email},
                {"password", password}
            };

            string resp = await HttpPost(PATH_ACCOUNT_CREATE, param);
            Acc_token = ParseJsonResp("acc_token", resp);
            return resp;
        }

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

        public async Task<string> DfuReport(int dfu_status, String Old_fv, String Current_fv, String Attempted_fv)
        {
            var vals = new Dictionary<string, string>{
                {"token", Acc_token},
                {"old_fv", Old_fv},
                {"new_fv", Current_fv},
                {"attempted_fv", Attempted_fv},
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
            using (var client = new HttpClient()) {
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

        // returns status type, 0 = good, 1 = either error or special notif
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

                }*/
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("Exception in ParseJson: " + e.Message);
                return null;
            }
            return tempVal;
        }


    }
}
