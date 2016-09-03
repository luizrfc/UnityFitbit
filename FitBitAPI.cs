﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Assets.Scripts.Utility;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.Fitbit
{
    /// <summary>
    /// FitbitAPI Handles all the calls to fitbit for retrieving the users data.
    /// Each tracker will have it's own API for getting data
    /// </summary>
    public class FitBitAPI : MonoBehaviour
    {
        /// <summary>
        /// Fill in your ConsumerSecret and ClientID for Fitbit
        /// </summary>
        private const string _consumerSecret = "YOUR_KEY_HERE";
        private const string _clientId = "YOUR_CLIENT_ID_HERE";

        //If you're making an app for Android, fill in your custom scheme here from Fitbit
        //if you don't know how to do the callback through a native browser on a mobile device 
        //http://technicalartistry.blogspot.ca/2016/01/fitbit-unity-oauth-2-and-native.html 
        //can probably help :)
        private const string CustomAndroidScheme = "YOUR_CALLBACK_URL";

        private const string _tokenUrl = "https://api.fitbit.com/oauth2/token";
        private const string _baseGetUrl = "https://api.fitbit.com/1/user/-/";
        private const string _profileUrl = _baseGetUrl + "profile.json/";
        private const string _activityUrl = _baseGetUrl + "activities/" ;

        private string _distanceUrl = _activityUrl +"distance/date/" + _currentDateTime + "/1d.json";

        private string _stepsUrl = _activityUrl +"steps/date/"+ _currentDateTime + "/1d.json";
        private string _yesterdayStepsUrl = _activityUrl + "steps/date/" + _yesterdayDateTime + "/1d.json";

        private string _caloriesUrl = _activityUrl +"calories/date/"+ _currentDateTime + "/1d.json";

        private string _sleepUrl = _baseGetUrl + "sleep/minutesAsleep/date/" + _currentDateTime + "/" + _currentDateTime + ".json";
        private string _yesterdaySleepUrl = _baseGetUrl + "sleep/minutesAsleep/date/" + _yesterdayDateTime + "/" + _yesterdayDateTime + ".json";

        private static string _currentDateTime = GetCurrentDate();
        private static string _yesterdayDateTime = GetYesterdayDate();

        private string _returnCode;
        private WWW _wwwRequest;
        private bool _bGotTheData = false;
        private bool _bFirstFire = true;

        private OAuth2AccessToken _oAuth2 = new OAuth2AccessToken();
        public FitbitData _fitbitData;
        public User User;

        public GameObject FitbitPanel;
        public GameObject ButtonPrefab;
        public GameObject Manager;
        public GameObject MessageBox;
        public Image LoadingImage;
        //Debug String for Android
        private string _statusMessage;

        private string CallBackUrl
        {
            get
            {
                //determine which platform we're running on and use the appropriate url
                if(Application.platform == RuntimePlatform.WindowsEditor)
                    return   WWW.EscapeURL("http://test.debuginc.com/envdump.php/"); 
                else if(Application.platform == RuntimePlatform.Android)
                {
                    return WWW.EscapeURL(CustomAndroidScheme); 
                }
                else
                {
                    return WWW.EscapeURL(CustomAndroidScheme);
                }
            }
        }

        public void Start()
        {
            DontDestroyOnLoad(this);
            //_fitbitData = User.Instance.FitbitData;
        }
        
        private void OnGUI()
        {
            if (!_bGotTheData && !string.IsNullOrEmpty(_statusMessage) && _bFirstFire)
            {
                MessageBox.GetComponent<MessageBoxBehavior>().SetMessageBoxText(_statusMessage);
                MessageBox.GetComponent<MessageBoxBehavior>().OpenMessageBox();
                _bFirstFire = false;
            }
        }

        public void LoginToFitbit()
        {
            //we'll check to see if we have the RefreshToken in PlayerPrefs or not. 
            //if we do, then we'll use the RefreshToken to get the data
            //if not then we will just do the regular ask user to login to get data
            //then save the tokens correctly.
            Manager.GetComponent<Manager>().SetLoadingMessage("Starting Fitbit Login");
            Manager.GetComponent<Manager>().OpenLoadingPanel();

            if (PlayerPrefs.HasKey("FitbitRefreshToken"))
            {
                UseRefreshToken();
            }
            else
            {
                UserAcceptOrDeny();
            }
            
        }
        public void UserAcceptOrDeny()
        {
            //we don't have a refresh token so we gotta go through the whole auth process.
            var url =
                "https://www.fitbit.com/oauth2/authorize?response_type=code&client_id=" + _clientId + "&redirect_uri=" +
                CallBackUrl +
                "&scope=activity%20nutrition%20heartrate%20location%20profile%20sleep%20weight%20social";
            Application.OpenURL(url);
            // print(url);
#if UNITY_EDITOR
            Manager.GetComponent<Manager>().OpenFitbitMenu();
#endif
        }

        private void UseReturnCode()
        {
            _statusMessage += "return code isn't empty";
            //not empty means we put a code in
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(_clientId + ":" + _consumerSecret);
            var encoded = Convert.ToBase64String(plainTextBytes);

            var form = new WWWForm();
            form.AddField("client_id", _clientId);
            form.AddField("grant_type", "authorization_code");
            form.AddField("redirect_uri", WWW.UnEscapeURL(CallBackUrl));
            form.AddField("code", _returnCode);

            var headers = form.headers;
            headers["Authorization"] = "Basic " + encoded;

            _wwwRequest = new WWW(_tokenUrl, form.data, headers);
            StartCoroutine(WaitForAccess(_wwwRequest));

            //DIRTY DIRTY HACK
            while (!_wwwRequest.isDone)
            {
            }

            Debug.Log("Token: " + _wwwRequest.text);
            Debug.Log("parsing token");

            var parsed = new JSONObject(_wwwRequest.text);
            ParseAccessToken(parsed);
            _statusMessage += "\nParsed Token: " + _oAuth2.Token;

            //now that we have the Auth Token, Lets use it and get data.
            GetAllData();
            //User.Instance.UpdateCharacterData();
            _bGotTheData = true;
        }

        public void UseRefreshToken()
        {
            Debug.Log("Using Refresh Token");
            var plainTextBytes = Encoding.UTF8.GetBytes(_clientId + ":" + _consumerSecret);
            var encoded = Convert.ToBase64String(plainTextBytes);

            var form = new WWWForm();
            form.AddField("grant_type", "refresh_token");
            form.AddField("refresh_token", PlayerPrefs.GetString("FitbitRefreshToken"));

            var headers = form.headers;
            headers["Authorization"] = "Basic " + encoded;

            _wwwRequest = new WWW(_tokenUrl, form.data, headers);
            StartCoroutine(WaitForAccess(_wwwRequest));
            //DIRTY DIRTY HACK
            while (!_wwwRequest.isDone)
            {
            }

            Debug.Log("RefreshToken wwwText: " + _wwwRequest.text);
            //check to see if it's errored or not
            //we have an error and thus should just redo the Auth.
            if(!String.IsNullOrEmpty(_wwwRequest.error))
            {
                PlayerPrefs.DeleteKey("FitbitRefreshToken");
                UserAcceptOrDeny();
                UseReturnCode();
                GetAllData();
            }
            else
            {
                Debug.Log("Using the Auth Token (UseRefreshToken)");
                //no errors so parse the accessToken and update everything :)
                var parsed = new JSONObject(_wwwRequest.text);
                ParseAccessToken(parsed);
                GetAllData();
            }
        }

        public void SetReturnCodeFromAndroid(string code)
        {
            if(string.IsNullOrEmpty(code))
                return;
            //we passed the full URL so we'll have to extract the 
            //We will add 6 to the string lenght to account for "?code="
            _returnCode = code.Substring(CustomAndroidScheme.Length + 6);
            _statusMessage += "Return Code is: " + _returnCode;
            
            UseReturnCode();
        }

        public void SetReturnCode(string code)
        {
            if(string.IsNullOrEmpty(code))
                return;

            _returnCode = code;
            UseReturnCode();
        }

        

        public int GetStepsFromYesterday()
        {
            //time for Getting Dataz
            var headers = new Dictionary<string, string>();
            headers["Authorization"] = "Bearer " + _oAuth2.Token;

            Debug.Log("Steps URL is: " + _yesterdayStepsUrl);
            _wwwRequest = new WWW(_yesterdayStepsUrl, null, headers);
            Debug.Log("Doing yesterday Steps GET Request");
            StartCoroutine(WaitForAccess(_wwwRequest));

            //DIRTY DIRTY HACK
            while (!_wwwRequest.isDone)
            {
            }

            //parse IN method instead of going to another
            //convert the json to xml cause json blows hard.
            XmlDocument json = JsonConvert.DeserializeXmlNode(_wwwRequest.text);

            XDocument doc = XDocument.Parse(json.InnerXml);
            var root = doc.Descendants("value").FirstOrDefault();

            Debug.Log("Steps from YESTERDAY Fitbit: " + root.Value);
            return ToInt(root.Value);
        }

        public int GetSleepFromYesterday()
        {
            //time for Getting Dataz
            var headers = new Dictionary<string, string>();
            headers["Authorization"] = "Bearer " + _oAuth2.Token;

            Debug.Log("Steps URL is: " + _yesterdaySleepUrl);
            _wwwRequest = new WWW(_yesterdaySleepUrl, null, headers);
            Debug.Log("Doing yesterday Sleep GET Request");
            StartCoroutine(WaitForAccess(_wwwRequest));

            //DIRTY DIRTY HACK
            while (!_wwwRequest.isDone)
            {
            }

            //parse IN method instead of going to another
            //convert the json to xml cause json blows hard.
            XmlDocument json = JsonConvert.DeserializeXmlNode(_wwwRequest.text);

            XDocument doc = XDocument.Parse(json.InnerXml);
            var root = doc.Descendants("value").FirstOrDefault();

            Debug.Log("Sleep from YESTERDAY Fitbit: " + root.Value);
            return ToInt(root.Value);
        }

        public void GetAllData()
        {
            GetProfileData();
            GetCharacterRelevantData();
            BuildProfile();

            //make sure the loading screen is open and change message
            Manager.GetComponent<Manager>().SetLoadingMessage("Processing Fitbit Data");
            Manager.GetComponent<Manager>().OpenLoadingPanel();
            _fitbitData.LastSyncTime = DateTime.Now.ToUniversalTime();
            Debug.Log("LastSyncTime: "+ DateTime.Now.ToUniversalTime().ToString("g"));
            //For now, we'll do the GS call for character stuff here till I find a better way to handle it.
            User.Instance.GetLastGsData();
            //Manager.GetComponent<Manager>().OpenCharacterMenu();
            //WebView.Hide();
            //MessageBox.GetComponent<MessageBoxBehavior>().CloseMessageBox();
        }

        private void GetCharacterRelevantData()
        {
            GetSteps();
            GetDistance();
            GetCalories();
            GetSleep();
        }

        #region GetData
        private void GetProfileData()
        {

            //time for Getting Dataz
            var headers = new Dictionary<string, string>();
            headers["Authorization"] = "Bearer " + _oAuth2.Token;

            _wwwRequest = new WWW(_profileUrl, null, headers);
            Debug.Log("Doing GET Request");
            StartCoroutine(WaitForAccess(_wwwRequest));

            //DIRTY DIRTY HACK
            while (!_wwwRequest.isDone)
            {
            }

            ParseProfileData(_wwwRequest.text);

        }

        private void GetCalories()
        {
            //time for Getting Dataz
            var headers = new Dictionary<string, string>();
            headers["Authorization"] = "Bearer " + _oAuth2.Token;

            Debug.Log("Calories URL is: " + _caloriesUrl);
            _wwwRequest = new WWW(_caloriesUrl, null, headers);
            Debug.Log("Doing Calories GET Request");
            StartCoroutine(WaitForAccess(_wwwRequest));

            //DIRTY DIRTY HACK
            while (!_wwwRequest.isDone)
            {
            }
            ParseCaloriesData(_wwwRequest.text);
        }

        private void GetDistance()
        {
            //time for Getting Dataz
            var headers = new Dictionary<string, string>();
            headers["Authorization"] = "Bearer " + _oAuth2.Token;

            Debug.Log("Distance URL is: " + _distanceUrl);
            _wwwRequest = new WWW(_distanceUrl, null, headers);
            Debug.Log("Doing Distance GET Request");
            StartCoroutine(WaitForAccess(_wwwRequest));

            //DIRTY DIRTY HACK
            while (!_wwwRequest.isDone)
            {
            }

            ParseDistanceData(_wwwRequest.text);
        }

        private void GetSteps()
        {
            //time for Getting Dataz
            var headers = new Dictionary<string, string>();
            headers["Authorization"] = "Bearer " + _oAuth2.Token;

            Debug.Log("Steps URL is: " + _stepsUrl);
            _wwwRequest = new WWW(_stepsUrl, null, headers);
            Debug.Log("Doing Steps GET Request");
            StartCoroutine(WaitForAccess(_wwwRequest));

            //DIRTY DIRTY HACK
            while (!_wwwRequest.isDone)
            {
            }

            ParseStepsData(_wwwRequest.text);
        }

        private void GetSleep()
        {
            //time for Getting Dataz
            var headers = new Dictionary<string, string>();
            headers["Authorization"] = "Bearer " + _oAuth2.Token;

            Debug.Log("Sleep URL is: " + _sleepUrl);
            _wwwRequest = new WWW(_sleepUrl, null, headers);
            Debug.Log("Doing Sleep GET Request");
            StartCoroutine(WaitForAccess(_wwwRequest));

            //DIRTY DIRTY HACK
            while (!_wwwRequest.isDone)
            {
            }

            ParseSleepData(_wwwRequest.text);
        }

        private void BuildProfile()
        {
            var imageWWW = new WWW(_fitbitData.ProfileData["avatar"]);
            //DIRTY DIRTY HACK
            while (!imageWWW.isDone)
            {
            }

            //turn on the panel just incase we haven't already and to avoid null exceptions
            //TODO actually replace this with a loading screen
            FitbitPanel.transform.parent.gameObject.SetActive(true);
            FitbitPanel.gameObject.SetActive(true);

            if(imageWWW.error == null)
            {
                //Header area. Pic and Name
                var pic = FitbitPanel.transform.Find("ProfilePicture");
                var thumbnail = new Texture2D(100, 100);
                thumbnail = imageWWW.texture;
                pic.GetComponent<Image>().sprite = Sprite.Create(thumbnail, new Rect(0, 0, 100, 100), new Vector2(0.5f, 0.5f));
                
            }
            var name = FitbitPanel.transform.Find("Name");
            Debug.Log(_fitbitData.RawProfileData["fullName"]);
            name.GetComponent<Text>().text = _fitbitData.RawProfileData["fullName"];


            //we should check to see if there is "data" (aka buttons) already or if this is 
            //the first time we are doing this (to avoid mass amount of the same buttons)
            //Details Area (inside the contentpanel inside the scrollviewer)
            var contentPanel = GameObject.Find("FitbitProfileContentPanel").gameObject;
            if (contentPanel.transform.childCount != 0)
            {
                foreach (Transform child in contentPanel.transform)
                {
                    Destroy(child.gameObject);
                }
            }
            if(_fitbitData.ProfileData.Count != 0)
            {
                foreach (KeyValuePair<string, string> kvp in _fitbitData.ProfileData)
                {
                    if(kvp.Key == "avatar")
                        continue;
                    
                    var btn = Instantiate(ButtonPrefab);
                    btn.transform.SetParent(contentPanel.transform,false);
                    //put a space between the camelCase
                    var tempKey = Regex.Replace(kvp.Key, "(\\B[A-Z])", " $1");
                    //then capitalize the first letter
                    UppercaseFirst(tempKey);
                    btn.transform.FindChild("Text").GetComponent<Text>().text = tempKey + ": " + kvp.Value;
                    btn.name = tempKey + "Btn";
                }
                //Steps Button
                var step = Instantiate(ButtonPrefab);
                step.transform.SetParent(contentPanel.transform, false);
                step.transform.FindChild("Text").GetComponent<Text>().text = "Steps: " + _fitbitData.CurrentSteps;
                step.name = "StepsBtn";
               
                //Dist Button
                var dist = Instantiate(ButtonPrefab);
                dist.transform.SetParent(contentPanel.transform, false);

                string distUnit = "";
                if (_fitbitData.RawProfileData["distanceUnit"] == "METRIC" || _fitbitData.RawProfileData["distanceUnit"] == "UK")
                {
                    distUnit = "KM";
                }
                else if (_fitbitData.RawProfileData["distanceUnit"] == "US")
                {
                    distUnit = "Mi";
                }

                dist.transform.FindChild("Text").GetComponent<Text>().text = "Distance: " + _fitbitData.CurrentDistance + " " + distUnit;
                dist.name = "DistanceBtn";

                //Calories Button
                var cals = Instantiate(ButtonPrefab);
                cals.transform.SetParent(contentPanel.transform, false);
                cals.transform.FindChild("Text").GetComponent<Text>().text = "Calories: " + _fitbitData.CurrentCalories;
                cals.name = "CaloriesBtn";

                //Sleep Button
                var sleep = Instantiate(ButtonPrefab);
                sleep.transform.SetParent(contentPanel.transform, false);
                sleep.transform.FindChild("Text").GetComponent<Text>().text = "Minutes Asleep: " + _fitbitData.CurrentSleep;
                sleep.name = "SleepBtn";
            }
            
            _bGotTheData = true;
            //turn the panel back off again till something else turns it back on
            FitbitPanel.transform.parent.gameObject.SetActive(false);
        }
        #endregion

        #region Parsing
        private void ParseAccessToken(JSONObject parsed)
        {
            var dict = parsed.ToDictionary();
            foreach (KeyValuePair<string, string> kvp in dict)
            {
                if (kvp.Key == "access_token")
                {
                    _oAuth2.Token = kvp.Value;
                    PlayerPrefs.SetString("FitbitAccessToken", kvp.Value);
                }
                else if (kvp.Key == "expires_in")
                {
                    var num = 0;
                    Int32.TryParse(kvp.Value, out num);
                    _oAuth2.ExpiresIn = num;

                }
                else if (kvp.Key == "refresh_token")
                {
                    _oAuth2.RefreshToken = kvp.Value;
                    Debug.Log("REFRESH TOKEN: " + kvp.Value);
                    PlayerPrefs.SetString("FitbitRefreshToken", kvp.Value);
                    Debug.Log("Token We Just Store: " + PlayerPrefs.GetString("FitbitRefreshToken"));
                }
                else if (kvp.Key == "token_type")
                {
                    _oAuth2.TokenType = kvp.Value;
                    PlayerPrefs.SetString("FitbitTokenType", kvp.Value);
                }
            }
        }

        private void ParseProfileData(string data)
        {
            Debug.Log("inserting json data into fitbitData.RawProfileData");
            //Debug.LogWarning(data);
            XmlDocument xmldoc = JsonConvert.DeserializeXmlNode(data);

            var doc = XDocument.Parse(xmldoc.InnerXml);


            doc.Descendants("topBadges").Remove();
            foreach (XElement xElement in doc.Descendants())
            {
                //Debug.Log(xElement.Name.LocalName + ": Value:" + xElement.Value);V
                if (!_fitbitData.RawProfileData.ContainsKey(xElement.Name.LocalName))
                    _fitbitData.RawProfileData.Add(xElement.Name.LocalName, xElement.Value);
                else
                {
                    //Debug.LogWarning("Key already found in RawProfileData: " + xElement.Name.LocalName);
                    //if the key is already in the dict, we will just update the value for consistency.
                    _fitbitData.RawProfileData[xElement.Name.LocalName] = xElement.Value;
                }

                if (_fitbitData.ProfileData.ContainsKey(xElement.Name.LocalName))
                {
                    _fitbitData.ProfileData[xElement.Name.LocalName] = xElement.Value;
                }
            }

            /*var json = JObject.Parse(data);
            
            foreach (JProperty property in json["user"].Children())
            {
                if(property.Name == "topBadges")
                    continue;

                Debug.Log("Key: "+ property.Name + "   Value" + property.Value);
                if (_fitbitData.ProfileData.ContainsKey(property.Name))
                {
                    _fitbitData.ProfileData[property.Name] = property.Value.ToString();
                }
                if(!_fitbitData.RawProfileData.ContainsKey(property.Name))
                    _fitbitData.RawProfileData.Add(property.Name,property.Value.ToString());
            }*/

            /*var doc = XDocument.Parse(data);
            doc.Descendants("topBadges").Remove();
            //we need to keep a copy of the original data incase sometime down the road we wanna do something with it
            //but we also need to make a working copy, of just the essentials that we are using now.
            foreach (XElement xElement in doc.Descendants())
            {
                //Debug.Log(xElement.Name.LocalName + ": Value:" + xElement.Value);V
                _fitbitData.RawProfileData.Add(xElement.Name.LocalName,xElement.Value);
                if (_fitbitData.ProfileData.ContainsKey(xElement.Name.LocalName))
                {
                    _fitbitData.ProfileData[xElement.Name.LocalName] = xElement.Value;
                }
            }*/

        }

        private void ParseStepsData(string data)
        {
            //convert the json to xml cause json blows hard.
            XmlDocument json = JsonConvert.DeserializeXmlNode(data);

            XDocument doc = XDocument.Parse(json.InnerXml);
            var root = doc.Descendants("value").FirstOrDefault();
            _fitbitData.CurrentSteps = ToInt(root.Value);
            
            Debug.Log("Steps from Fitbit: " + _fitbitData.CurrentSteps);
        }

        private void ParseDistanceData(string data)
        {
            XmlDocument json = JsonConvert.DeserializeXmlNode(data);

            XDocument doc = XDocument.Parse(json.InnerXml);
            var root = doc.Descendants("value").FirstOrDefault().Value;
            //trim the value
            if(root.Length > 4)
                root = root.Substring(0, 4);

            _fitbitData.CurrentDistance = ToDouble(root);

            Debug.Log("Distance from Fitbit is:" + _fitbitData.CurrentDistance);
        }

        private void ParseCaloriesData(string data)
        {
            XmlDocument json = JsonConvert.DeserializeXmlNode(data);

            var doc = XDocument.Parse(json.InnerXml);
            var calories = doc.Descendants("value").FirstOrDefault().Value;

            _fitbitData.CurrentCalories = ToInt(calories);
        }

        private void ParseSleepData(string data)
        {
            Debug.Log(data);
            XmlDocument json = JsonConvert.DeserializeXmlNode(data);

            var doc = XDocument.Parse(json.InnerXml);
            var sleepTimeTotal = doc.Descendants("value").FirstOrDefault().Value;
            Debug.Log("Minutes asleep for: " + sleepTimeTotal);

            _fitbitData.CurrentSleep = ToInt(sleepTimeTotal);
        }


        #endregion

        static string UppercaseFirst(string s)
        {
            // Check for empty string.
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }
            // Return char and concat substring.
            return char.ToUpper(s[0]) + s.Substring(1);
        }

        IEnumerator WaitForAccess(WWW www)
        {
            _statusMessage += "waiting for access\n";
            yield return www;
            _statusMessage += "Past the Yield \n";
            // check for errors
            if (www.error == null)
            {
                _statusMessage += "no error \n";
                _statusMessage += "wwwText: " + www.text;
                //Debug.Log("WWW Ok!: " + www.text);
                // _accessToken = www.responseHeaders["access_token"];
            }
            if (www.error != null)
            {
                _statusMessage += "\n Error" + www.error;
                Debug.Log(www.error);
                //Manager.GetComponent<Manager>().OpenFitbitMenu();
            }
            _statusMessage += "end of WaitForAccess \n";
        }

        //just a utility function to get the correct date format for activity calls that require one
        public static string GetCurrentDate()
        {
            var date = "";
            date += DateTime.Now.Year;
            if (DateTime.Now.Month < 10)
            {
                date += "-" + "0" + DateTime.Now.Month;
            }
            else
            {
                date += "-" + DateTime.Now.Month;
            }

            if (DateTime.Now.Day < 10)
            {
                date += "-" +"0" + DateTime.Now.Day;
            }
            else
            {
                date += "-" + DateTime.Now.Day;
            }
            //date += "-" + 15;
            return date;
        }

        private static string GetYesterdayDate()
        {
            //Getting yesterday is a bit tricky sometimes. We have to check what day it is and what month even before actually building the string
            //This is because for example, Jan 1st, 2015. The last day would be Dec 31, 2014. This requires us to actually change the whole string
            //compared to if it was intra-month.
            var date = "";
            if (DateTime.Now.Day == 1)
            {
                //we know that we are on the first day of the month, if we are the first day of Jan then we need to go back to the last day of Dec
                if (DateTime.Today.Month == 1)
                {
                    date += DateTime.Now.Year - 1;
                    date += "-12-31";
                    return date;
                }
                //else we aren't Jan so we can just subtract a month and go to the last day of that month.
                else
                {
                    date += DateTime.Now.Year
                        + "-" + (DateTime.Today.Month - 1)
                            + "-" + (DateTime.DaysInMonth(DateTime.Now.Year,DateTime.Now.Month-1));
                    return date;
                }
            }
            date += DateTime.Now.Year;

            //Months
            if (DateTime.Now.Month < 10)
            {
                date += "-" + "0" + DateTime.Now.Month;
            }
            else
            {
                date += "-" + DateTime.Now.Month;
            }

            //Days
            if (DateTime.Now.Day-1 < 10)
            {
                date += "-" + "0" + (DateTime.Now.Day-1);
            }
            else
            {
                date += "-" + (DateTime.Now.Day-1);
            }
            return date;
        }

        private int ToInt(string thing)
        {
            var temp = 0;
            Int32.TryParse(thing, out temp);
            return temp;
        }

        private double ToDouble(string thing)
        {
            var temp = 0.0;
            Double.TryParse(thing, out temp);
            return temp;
        }

    }

}
