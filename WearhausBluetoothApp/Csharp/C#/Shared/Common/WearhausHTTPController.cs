using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Serialization.Json;
using Windows.Data.Json;


namespace WearhausHttp
{
    public class WearhausHttpController
    {


#if DEBUG
        private const string WEARHAUS_URI = "http://wearhausapistaging.herokuapp.com/v1.2/";
#else
        private const string WEARHAUS_URI = "http://wearhausapi.herokuapp.com/v1.2/";
#endif
        private const string PATH_ACCOUNT_CREATE = "account/create";
        private const string PATH_ACCOUNT_VERIFY_GUEST = "account/verify_guest";
        private const string PATH_ACCOUNT_VERIFY_CREDENTIALS = "account/verify_credentials";
        private const string PATH_ACCOUNT_UPDATE_HID = "account/update_hid";
        private const string PATH_ACCOUNT_VERIFY_EMAIL = "account/verify_email";
        private const string PATH_ACCOUNT_UPDATE_PROFILE = "account/update_profile";
        private const string PATH_ACCOUNT_FORGOT_PASSWORD = "account/forgot_password";
        private const string PATH_ACCOUNT_FORGOT_PASSWORD_LOGIN = "account/forgot_password_login";

        private const string PATH_USERS_SHOW = "users/forgot_password_login";
        private const string PATH_USERS_PRIVATE_PROFILE = "users/forgot_password_login";

        private const string PATH_FRIENDS_REQUEST = "friends/forgot_password_login";
        private const string PATH_FRIENDS_REMOVE = "friends/forgot_password_login";
        private const string PATH_FRIENDS_IDS = "friends/forgot_password_login";

        private string HID;
        private string User_id;
        private string Token;

        private DataContractJsonSerializer JsonSerializer;

        // Last Successful Reponse from an HTTP Request
        public string LastHttpResponse { get; private set; }

        public WearhausHttpController(string deviceID)
        {
            HID = WearhausHttpController.ParseHID(deviceID);
            User_id = null;
            Token = null;

            JsonSerializer = new DataContractJsonSerializer(typeof(Dictionary<string, string>));

            LastHttpResponse = null;
        }

        public async Task<string> CreateNewUser(string email, string password)
        {
            var param = new Dictionary<string, string>{
                {"email", email},
                {"password", password}
            };
            
            string resp = await HttpPost(PATH_ACCOUNT_CREATE, param);
            return resp; 
        }

        public async Task<string> CreateGuest()
        {
            var vals = new Dictionary<string, string>{
                {"hid", HID}
            };

            string resp = await HttpPost(PATH_ACCOUNT_VERIFY_GUEST, vals);
            var x = JsonValue.Parse(resp);
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
                    return "Exception in HttpPost response:\n" + e.Message;
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
                    return "Exception in HttpGet response:\n" + e.Message;
                }
            }
        }


        public static string ParseHID(string chatserviceinfoID)
        {
            string[] words = chatserviceinfoID.Split('_')[1].Split('&');
            return words[words.Length - 1];
        }

    }
}
