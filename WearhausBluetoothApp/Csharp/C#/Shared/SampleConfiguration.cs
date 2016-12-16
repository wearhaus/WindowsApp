//*********************************************************
//
// Copyright (c) Microsoft. All rights reserved.
//
//*********************************************************

using System.Collections.Generic;
using Windows.UI.Xaml.Controls;using System;
using WearhausBluetoothApp;

namespace SDKTemplate
{
    public partial class MainPage : Page
    {
        // Change the string below to reflect the name of your sample.
        // This is used on the main page as the title of the sample.
        public const string FEATURE_NAME = "Wearhaus Arc";

        // Change the array below to reflect the name of your scenarios.
        // This will be used to populate the list of scenarios on the main page with
        // which the user will choose the specific scenario that they are interested in.
        // These should be in the form: "Navigating to a web page".
        // The code in MainPage will take care of turning this into: "1) Navigating to a web page"
        List<Scenario> scenarios = new List<Scenario>
        {
            new Scenario() { Title = "Run Firmware Update App", ClassType = typeof(Scenario1_DfuClient) },
            new Scenario() { Title = "Wearhaus Arc", ClassType = typeof(Dashboard) },
            new Scenario() { Title = "Firmware Update", ClassType = typeof(DFUPage) },
            //new Scenario() { Title = "Login", ClassType = typeof(LoginPage) }
        };
    }

    public class Scenario
    {
        public string Title { get; set; }

        public Type ClassType { get; set; }

        public override string ToString()
        {
            return Title;
        }
    }
}
