// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.ComponentModel;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Storage.Pickers;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

using System.Diagnostics;

using SDKTemplate;
using SDKTemplate.Common;

using Gaia;
using WearhausServer;
using WearhausBluetoothApp.Common;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml.Media.Imaging;

namespace WearhausBluetoothApp
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
#if WINDOWS_PHONE_APP
    public sealed partial class Scenario1_DfuClient : Page, IFileOpenPickerContinuable
#else
    public sealed partial class Scenario1_DfuClient : Page
#endif
    {
        // Wearhaus UUID for GAIA: 00001107-D102-11E1-9B23-00025B00A5A5
        // Only looking for this UUID e.g. App only looks for Wearhaus Arc!
        private static readonly Guid RfcommGAIAServiceUuid = Guid.Parse("00001107-D102-11E1-9B23-00025B00A5A5"); // "CSR Gaia Service"

        // The Id of the Service Name SDP attribute
        private const UInt16 SdpServiceNameAttributeId = 0x100;

        // The SDP Type of the Service Name SDP attribute.
        // The first byte in the SDP Attribute encodes the SDP Attribute Type as follows :
        //    -  the Attribute Type size in the least significant 3 bits,
        //    -  the SDP Attribute Type value in the most significant 5 bits.
        private const byte SdpServiceNameAttributeType = (4 << 3) | 5;

        private StreamSocket BluetoothSocket;
        
        private DataWriter BluetoothWriter;
        private RfcommDeviceService BluetoothService;
        private DeviceInformationCollection BluetoothServiceInfoCollection;
        private GaiaHelper GaiaHandler;
        private StorageFile DfuFile;
        private DataReader DfuReader;
        private Boolean DfuIsWaitingForVerification;

        // ArcLink local vars
        private String MyHid;
        private int MyProductId;
        private String MyFvFullCode;
        private String MyFvOldFullCode;
        private String MyDeviceHumanName;
        private Firmware MyFirmwareVersion;
        // will be null when chosen from a file
        private Firmware MyTargetFirmware;



        private DFUStep MyDFUStep;
        private ArcConnState MyArcConnState;
        private String MyErrorString;
        // None until dfu reaches an end state that requires restarting arc/app or success.
        private DFUResultStatus MyDfuErrorNum = DFUResultStatus.None;

        private WearhausHttpController MyHttpController;

        private MainPage rootPage;

        private NavigationHelper navigationHelper;
        private ObservableDictionary defaultViewModel = new ObservableDictionary();

        /// <summary>
        /// This can be changed to a strongly typed view model.
        /// </summary>
        public ObservableDictionary DefaultViewModel
        {
            get { return this.defaultViewModel; }
        }

        /// <summary>
        /// NavigationHelper is used on each page to aid in navigation and 
        /// process lifetime management
        /// </summary>
        public NavigationHelper NavigationHelper
        {
            get { return this.navigationHelper; }
        }

        public enum ArcConnState
        {
            NoArc,
            TryingToConnect,
            GatheringInfo,
            Connected,
            Error,
        };

        // UI listens for changes in this to determine what it should display, such as text/buttons/actions for users/errors
        // TODO are error messages in ErrorHuman, or the DFU Report Num?
        // Not called DFUState, since that exact name is used by GaiaHelper for a small portion of the whole Firmware Update process
        public enum DFUStep
        {
            None,
            // send inital Gaia commands to begin DFU process, about to do the first hurl. awaitng notif via DFUState
            StartingUpload,
            // sending firmware bytes
            UploadingFW,
            // chip checking firmware, awaiting notif via DFUState
            VerifyingImage,
            // internal chip will power cycle, disconnecting Gaia and forc ing us to reconnect;
            // I think Sidd skipped this step on windows???? TODO investigate
            ChipPowerCycle,
            // This step is needed for Android; does it also need for Windows? the manual power cycle?
            AwaitingManualPowerCycle,
            ConnectingAfterManualPowerCycle,
            //VerifyingAfterPowerCycle,
            Success,
            SuccessDBG,

            ErrorUnrecoverable,
        };


        // Send to server the Enum number, not the string
        public enum DFUResultStatus
        {
            Success = 0,
            Aborted = 1,
            IOException = 2,
            VerifyFailed = 3,
            OtherFailure = 4,
            DownloadFailed = 5,
            FvMismatch = 6,
            DisconnectedDuring = 7,
            TimeoutDfuState = 8,
            DfuRequestBadAck = 9,
            CantStartWeirdOldFV = 10,
            TimeoutFinalizing = 11,
            None = 99,
        };


        /// <summary>
        /// Populates the page with content passed during navigation. Any saved state is also
        /// provided when recreating a page from a prior session.
        /// </summary>
        /// <param name="sender">
        /// The source of the event; typically <see cref="NavigationHelper"/>
        /// </param>
        /// <param name="e">Event data that provides both the navigation parameter passed to
        /// <see cref="Frame.Navigate(Type, Object)"/> when this page was initially requested and
        /// a dictionary of state preserved by this page during an earlier
        /// session. The state will be null the first time a page is visited.</param>
        private void navigationHelper_LoadState(object sender, LoadStateEventArgs e)
        {
        }

        /// <summary>
        /// Preserves state associated with this page in case the application is suspended or the
        /// page is discarded from the navigation cache.  Values must conform to the serialization
        /// requirements of <see cref="SuspensionManager.SessionState"/>.
        /// </summary>
        /// <param name="sender">The source of the event; typically <see cref="NavigationHelper"/></param>
        /// <param name="e">Event data that provides an empty dictionary to be populated with
        /// serializable state.</param>
        private void navigationHelper_SaveState(object sender, SaveStateEventArgs e)
        {
        }

        #region NavigationHelper registration

        /// The methods provided in this section are simply used to allow
        /// NavigationHelper to respond to the page's navigation methods.
        /// 
        /// Page specific logic should be placed in event handlers for the  
        /// <see cref="GridCS.Common.NavigationHelper.LoadState"/>
        /// and <see cref="GridCS.Common.NavigationHelper.SaveState"/>.
        /// The navigation parameter is available in the LoadState method 
        /// in addition to page state preserved during an earlier session.

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            //rootPage = MainPage.Current;
            navigationHelper.OnNavigatedTo(e);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            navigationHelper.OnNavigatedFrom(e);
        }

        #endregion
        
        public Scenario1_DfuClient()
        {
            this.InitializeComponent();

            this.navigationHelper = new NavigationHelper(this);
            this.navigationHelper.LoadState += navigationHelper_LoadState;
            this.navigationHelper.SaveState += navigationHelper_SaveState;

            BluetoothSocket = null;
            BluetoothWriter = null;
            BluetoothService = null;
            BluetoothServiceInfoCollection = null;

            GaiaHandler = null;
            DfuIsWaitingForVerification = false;

            MyArcConnState = ArcConnState.NoArc;
            MyDFUStep = DFUStep.None;
            MyDfuErrorNum = DFUResultStatus.None;
            MyErrorString = null;
            MyHid = null;
            MyProductId = -1;
            MyFvFullCode = null;
            MyFvOldFullCode = null;
            MyDeviceHumanName = null;
            MyFirmwareVersion = null;
            MyTargetFirmware = null;

            App.Current.Suspending += App_Suspending;

            
            // NOTE: Added post revert try2
            ArcStateText.Text = "";
            UpdateFV.IsEnabled = false;
            // manually opened by button, so not directly related to UIState
            updateInstructVisibility(false);
            updateDashboardExpandVisibility(false);

            InstructionImages.Add("arc_update_1.png");
            InstructionImages.Add("arc_update_2.png");
            InstructionImages.Add("arc_update_3.png");
            //InstructionTexts.Add("Step 1: Turn on your Wearhaus Arc. Then open Windows Bluetooth Settings by searching for Bluetooth Settings in the Windows Search Bar.");
            InstructionTexts.Add("Step 1: Turn on your Wearhaus Arc. Then open Windows Bluetooth Settings.");
            InstructionTexts.Add("Step 2: Find Wearhaus Arc and click \"Pair\". Click \"Yes\" to any prompts that ask for permission.");
            InstructionTexts.Add("Step 3: Wait for the progress bar to complete all the way after clicking Pair. After that, press Connect My Arc above.");

            UpdateInstructionFrame(0); // fyi: if called when invisible, doesn't make visible
            UpdateUI();

            DfuLayout.Visibility = Visibility.Collapsed;// TODO temp location for this
            
        }

        List<string> InstructionImages = new List<string>();
        List<string> InstructionTexts = new List<string>();
        private int ImageFrame;
        private Boolean InstrucVisible = false;
        private Boolean DashboardVisible = false;
        private Boolean DashboardExpandVisible = false;



        void App_Suspending(object sender, Windows.ApplicationModel.SuspendingEventArgs e)
        {
            // Make sure we cleanup resources on suspend
            Disconnect();
        }



        // commented declaration is for when it's a listener added with +=
        void UpdateUIListener(object sender, EventArgs e)
        {
            UpdateUI();
        }
        private void UpdateUI()
        {
            Debug.WriteLine("UpdateUI " + MyArcConnState + ", " + MyDFUStep);


            DisconnectButton.Visibility = Visibility.Collapsed;
            // first of all, server doesn't matter if no arc is connected
            switch (MyArcConnState)
            {
                case ArcConnState.NoArc:
                    // TODO here, there should be the picture steps to help you
                    ArcStateText.Text = "";
                    ConnectButton.IsEnabled = true;
                    ConnectButton.Opacity = 1.0;
                    HowToButton.IsEnabled = true;
                    HowToButton.Opacity = 1.0;
                    ConnectionProgress.Opacity = 0;
                    updateDashboardVisibility(false);
                    break;

                case ArcConnState.TryingToConnect:
                case ArcConnState.GatheringInfo:
                    ArcStateText.Text = "Connecting";
                    ConnectButton.IsEnabled = false;
                    ConnectButton.Opacity = 0.0;
                    HowToButton.IsEnabled = false;
                    HowToButton.Opacity = 0.0;
                    ConnectionProgress.Opacity = 1.0;
                    updateDashboardVisibility(false);
                    //case ArcLink.ArcConnState.GatheringInfo:
                    break;

                case ArcConnState.Connected:

                    // now we can check server status
                    updateConnectUIServer();

                    break;

                case ArcConnState.Error:
                    if (MyErrorString != null && MyErrorString.Length > 0)
                    {
                        ArcStateText.Text = MyErrorString;
                    }
                    else
                    {
                        ArcStateText.Text = "We've ran into a problem";
                    }


                    ConnectButton.Content = "Try Again";
                    ConnectButton.IsEnabled = true;
                    ConnectButton.Opacity = 1.0;
                    HowToButton.IsEnabled = true;
                    HowToButton.Opacity = 1.0;
                    ConnectionProgress.Opacity = 0;
                    updateDashboardVisibility(false);
                    break;


            }

        }


        private void updateConnectUIServer()
        {
            // only called if arc is connected, updates specific state based on server status
            // this method is just to keep things easier to read (switch inside a switch case...)

            ConnectButton.IsEnabled = false;
            ConnectButton.Opacity = 0.0;
            HowToButton.IsEnabled = false;
            HowToButton.Opacity = 0.0;
            updateInstructVisibility(false);

            if (MyHttpController == null) {
                return;
            }


            switch (MyHttpController.MyAccountState)
            {
                case WearhausHttpController.AccountState.None:
                    ArcStateText.Text = "Connecting to server";
                    ConnectionProgress.Opacity = 1.0;
                    updateDashboardVisibility(false);

                    break;

                case WearhausHttpController.AccountState.Loading:
                    ArcStateText.Text = "Connecting to server";
                    ConnectionProgress.Opacity = 1.0;
                    updateDashboardVisibility(false);
                    break;

                case WearhausHttpController.AccountState.ValidGuest:
                    /////////
                    /////////

                    updateDfuStateUI();

                    break;

                case WearhausServer.WearhausHttpController.AccountState.Error:
                    ArcStateText.Text = "Error connecting to server. Please make sure you have internet access and try again. If this problem continues, try updating this app or contact customer support at wearhaus.com";
                    ConnectButton.Content = "Try Again";
                    ConnectButton.IsEnabled = true;
                    ConnectButton.Opacity = 1.0;
                    ConnectionProgress.Opacity = 0.0;
                    updateDashboardVisibility(false);
                    // try again button should appear
                    break;

            }

            
        }



        private void updateDfuStateUI()
        {

            ConnectionProgress.Opacity = 0.0;
            VerifyDfuButton.IsEnabled = false;
            VerifyDfuButton.Opacity = 0.0;
            

            if (MyDFUStep == DFUStep.None)
            {
                DfuLayout.Visibility = Visibility.Collapsed;
                DfuStateText.Text = "";
                DisconnectButton.Visibility = Visibility.Visible;
                DisconnectButton.Opacity = 1.0;
                DisconnectButton.IsEnabled = true;
                updateDashboardVisibility(true);

                ArcStateText.Text = "Connected to " + MyDeviceHumanName;
                ProductIdText.Text = WearhausHttpController.GetArcGeneration(MyProductId);
                HidText.Text = MyHid;
                FvFullText.Text = MyFvOldFullCode;

                updateDashboardVisibility(true);


                if (MyFirmwareVersion == null){
                    MyFirmwareVersion = Firmware.GetFirmwareFromFullCode(MyFvFullCode);
                }
                
                String uniqueCode = WearhausHttpController.GetUniqueCodeFromFull(MyFvFullCode);

                bool showDfuStartUI = false;

                if (MyFirmwareVersion != null) {
                    Debug.WriteLine("MyFirmwareVersion != null ");

                    // we know our version, let's check if we can update
                    FirmwareText.Text = Firmware.FirmwareTable[uniqueCode].humanName;
                    String latestUnique = Firmware.LatestByProductId[MyProductId + ""];
                    Debug.WriteLine("latestUnique = " + latestUnique);

                    if (latestUnique != null && latestUnique.Length == 4
                        && Firmware.FirmwareTable[latestUnique] != null && Firmware.FirmwareTable[latestUnique].validBases.Contains(uniqueCode))
                    {
                        Debug.WriteLine("Detected new firmware version available for this Arc: " + latestUnique);
                        Firmware latest = Firmware.FirmwareTable[latestUnique];
                        showDfuStartUI = true;
                    }
                } else {
                    FirmwareText.Text = "Unknown";
                    showDfuStartUI = false;
                    // TODO report to server
                }

                if (showDfuStartUI)
                {
                    UpdateFV.Visibility = Visibility.Visible;
                    UpdateFV.IsEnabled = true;
                    FirmwareUpToDate.Visibility = Visibility.Collapsed;
                } else  {
                    UpdateFV.Visibility = Visibility.Collapsed;
                    UpdateFV.IsEnabled = false;
                    FirmwareUpToDate.Visibility = Visibility.Visible;
                    FirmwareDescText.Text = "";
                }

            }
            else
            {
                // DFU Step!

                DisconnectButton.Opacity = 0.0;
                DisconnectButton.IsEnabled = false;
                DfuLayout.Visibility = Visibility.Visible;
                updateDashboardVisibility(false);

                switch (MyDFUStep)
                {

                    case DFUStep.StartingUpload:
                        DfuStateText.Text = "Starting Update";
                        DfuProgress.Opacity = 1.0;
                        DfuProgress.IsIndeterminate = true;
                        break;
                    case DFUStep.UploadingFW:
                        DfuStateText.Text = "Updating";
                        DfuProgress.Opacity = 1.0;
                        DfuProgress.IsIndeterminate = true;
                        break;
                    case DFUStep.VerifyingImage:
                        DfuProgress.Opacity = 1.0;
                        DfuProgress.IsIndeterminate = true;
                        DfuStateText.Text = "Updating";
                        break;
                    case DFUStep.ChipPowerCycle:
                        DfuProgress.Opacity = 1.0;
                        DfuProgress.IsIndeterminate = true;
                        DfuStateText.Text = "Your Arc will automatically restart - please listen to your Arc for a double beep sound to indicate a restart. When you hear the beep or have waited 30 seconds, please press the \"Verify Update\" button to verify that the update worked."; // TODO
                        break;
                    case DFUStep.AwaitingManualPowerCycle:
                        DfuProgress.Opacity = 0.0;
                        VerifyDfuButton.IsEnabled = true;
                        VerifyDfuButton.Opacity = 1.0;

                        // trigger for this should start a 2 min timer, with another loading bar
                        // So we can really just trigger it ourselves over and over again until it succeeds or throws an error

                        if (MyErrorString != null && !MyErrorString.Equals("")){
                            // so we can communicate soft recoverable errors here to user
                            DfuStateText.Text = MyErrorString;
                        } else {
                            DfuStateText.Text = "Wait a few more minutes. Once your Arc says 'Device Connected', press the Verify Button below.";
                        }
                        break;

                    case DFUStep.ConnectingAfterManualPowerCycle:
                        DfuProgress.Opacity = 1.0;
                        VerifyDfuButton.IsEnabled = false;
                        VerifyDfuButton.Opacity = 01.0;

                        // trigger for this should start a 2 min timer, with another loading bar
                        // So we can really just trigger it ourselves over and over again until it succeeds or throws an error

                        if (MyErrorString != null && !MyErrorString.Equals("")) {
                            // so we can communicate soft recoverable errors here to user
                            DfuStateText.Text = MyErrorString;
                        } else {
                            DfuStateText.Text = "Checking Update";
                        }

                        break;

                    case DFUStep.Success:
                        DfuProgress.Opacity = 0.0;
                        DfuStateText.Text = "Firmware Update succeeded!"; // TODO
                        break;
                    case DFUStep.SuccessDBG:
                        DfuProgress.Opacity = 0.0;
                        DfuStateText.Text = "DBG Firmware Update succeeded! Here's the old and new FV string, are they good?  OldFv=" + MyFvOldFullCode + ",  NewFv=" + MyFvFullCode; 
                        break;

                    case DFUStep.ErrorUnrecoverable:
                        DfuProgress.Opacity = 0.0;
                        // first, check if end state error
                        if (MyDfuErrorNum != DFUResultStatus.None)  {
                            DfuStateText.Text =  WearhausHttpController.GetMessageDfuResult(MyDfuErrorNum);
                        } else {
                            if (MyErrorString != null && !MyErrorString.Equals("")) {
                                DfuStateText.Text = MyErrorString;
                            } else {
                                DfuStateText.Text = "An Error has occured. Please restart your Arc, close this app, and try again.";
                            }
                        }
                        break;

                }
            }


        }



        /// //////////////////
        /// //////////////////
        /// ArcLink methods:
        /// //////////////////
        /// //////////////////


        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            ConnectArc();
        }

        private async void ConnectArc()
        {

            if (DfuIsWaitingForVerification) {
                MyErrorString = null;
                // Just make sure, so we can erase the last error message
                MyDFUStep = DFUStep.ConnectingAfterManualPowerCycle;
                UpdateUI();
            }

            // Find all paired instances of the Rfcomm chat service
            BluetoothServiceInfoCollection = await DeviceInformation.FindAllAsync(
            RfcommDeviceService.GetDeviceSelector(RfcommServiceId.FromUuid(RfcommGAIAServiceUuid)));

            DeviceInformation bluetoothServiceInfo = null;
            if (BluetoothServiceInfoCollection.Count > 0)
            {
                
                List<string> items = new List<string>();
                foreach (var chatServiceInfo in BluetoothServiceInfoCollection)
                {
                    items.Add(chatServiceInfo.Name);
                    //Added to print services!
                    string hid = WearhausHttpController.ParseHID(chatServiceInfo.Id);
                    if (hid.StartsWith("1CF03E") || hid.StartsWith("00025B")) {
                        bluetoothServiceInfo = chatServiceInfo;
                    }
                }
                cvs.Source = items;

                if (bluetoothServiceInfo != null)
                {
                    try
                    {
                        if (DfuIsWaitingForVerification) {
                            // Don't tell them since it's already loading anyways
                        } else { 
                            MyArcConnState = ArcConnState.TryingToConnect;
                            UpdateUI();
                        }

                        // Potential Bug Fix?? Wrap FromIdAsync call in the UI Thread, as per the instructions of:
                        // http://blogs.msdn.com/b/wsdevsol/archive/2014/11/10/why-doesn-t-the-windows-8-1-bluetooth-rfcomm-chat-sample-work.aspx
                        // EDIT: Actually may not work as all this does is bring the exception thrown outside of this thread, so the exception is unhandled...

                        //await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                        //{
                        // ONLY WORKS IN WINDOWS 10 RIGHT NOW!?
                        BluetoothService = await RfcommDeviceService.FromIdAsync(bluetoothServiceInfo.Id);
                        //});


                        if (BluetoothService == null)
                        {
                            if (DfuIsWaitingForVerification) {
                                // potentially recoverable dfu error
                                MyErrorString = "Error, try restarting your Arc.";
                                MyDFUStep = DFUStep.AwaitingManualPowerCycle;
                                UpdateUI();
                            } else {
                                MyErrorString = "This app needs permission to connect to your Wearhaus Arc. Try reconnecting to your headphone and clicking \"Yes\" to any prompts for permission.";
                                MyArcConnState = ArcConnState.Error;
                                UpdateUI();
                            }
                            return;
                        }

                        var attributes = await BluetoothService.GetSdpRawAttributesAsync();
                        var attributeReader = DataReader.FromBuffer(attributes[SdpServiceNameAttributeId]);
                        var attributeType = attributeReader.ReadByte();
                        var serviceNameLength = attributeReader.ReadByte();

                        // The Service Name attribute requires UTF-8 encoding.
                        attributeReader.UnicodeEncoding = UnicodeEncoding.Utf8;
                        //ServiceName.Text = "Connected to: \"" + bluetoothServiceInfo.Name + "\"";

                        lock (this)
                        {
                            BluetoothSocket = new StreamSocket();
                        }

                        await BluetoothSocket.ConnectAsync(BluetoothService.ConnectionHostName, BluetoothService.ConnectionServiceName);

                        BluetoothWriter = new DataWriter(BluetoothSocket.OutputStream);

                        DataReader chatReader = new DataReader(BluetoothSocket.InputStream);
                        ReceiveStringLoop(chatReader);

                        GaiaMessage firmware_version_request = new GaiaMessage((ushort)GaiaMessage.GaiaCommand.GetAppVersion); 
                        // Send a get app version Gaia req to the device to see what the firmware version is (for DFU)
                        SendRawBytes(firmware_version_request.BytesSrc);


                        if (DfuIsWaitingForVerification) {
                            // Don't tell user this detail, wait for firmware ver to get back
                        }  else {
                            GaiaHandler = new GaiaHelper(); // Create GAIA DFU Helper Object

                            MyHid = WearhausHttpController.ParseHID(bluetoothServiceInfo.Id);
                            String productIdChar = MyHid.Substring(6, 1); // get the 1 char, range from 0 to F
                            MyProductId = Convert.ToInt32(productIdChar, 16); // convert from hex to decimal
                            MyDeviceHumanName = bluetoothServiceInfo.Name;
                            MyHttpController = new WearhausHttpController();

                            MyArcConnState = ArcConnState.GatheringInfo;
                            UpdateUI();
                        }
                        


                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Error: " + ex.ToString());
                        if (ex.Message.ToLower().Contains("datagram socket"))
                        {
                            MyErrorString = "Sorry this app does not run on Windows 8. Please consider upgrading to Windows 10 or visit wearhaus.com/updater for other options on Android or OSX.";
                            MyArcConnState = ArcConnState.Error;
                        } else {
                            if (DfuIsWaitingForVerification) {
                                // potentially recoverable dfu error
                                // this occurs for about 2 minutes after the device resets itself. Actually no need for power cycling the arc it seems.
                                MyErrorString = "Couldn't connect to your Arc. Try again in a few minutes and make sure the updating Arc is connected in Bluetooth Settings.";
                                MyDFUStep = DFUStep.AwaitingManualPowerCycle;
                            } else {
                                MyErrorString = "Couldn't connect to your Arc. Please make sure your Arc is powered on and connected in Bluetooth Settings. Or try turning your Arc off and then on again.";
                                MyArcConnState = ArcConnState.Error;
                            }
                        }
                        Disconnect();
                        UpdateUI();
                    }
                }
                else
                {

                    if (DfuIsWaitingForVerification){
                        // potentially recoverable dfu error
                        MyErrorString = "No Wearhaus Arc found. Please reconnect the Arc that was being updated again.";
                        MyDFUStep = DFUStep.AwaitingManualPowerCycle;
                    } else {
                        MyErrorString = "No Wearhaus Arc found. Please double check you are connected to a Wearhaus Arc in Windows Bluetooth Settings and try again.";
                        MyArcConnState = ArcConnState.Error;
                    }

                    UpdateUI();
                }
            }
            else
            {
                if (DfuIsWaitingForVerification) {
                    // potentially recoverable dfu error
                    MyErrorString = "No Wearhaus Arc found. Please reconnect the Arc that was being updated again.";
                    MyDFUStep = DFUStep.AwaitingManualPowerCycle;
                } else {
                    MyErrorString = "No Wearhaus Arc found. Please double check you are connected to a Wearhaus Arc in Windows Bluetooth Settings and try again.";
                    MyArcConnState = ArcConnState.Error;
                }
            }

        }


        /// <summary>
        /// Message to start the UI Services and functions to search for nearby bluetooth devices
        /// </summary>
        private async void VerifyDfuButton_Click(object sender, RoutedEventArgs e)
        {
            ConnectArc();
        }

        /*
        private async void VerifyDfuButton_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("VerifyDfuButton_Click...");

            // Clear any previous messages
            MainPage.Current.NotifyUser("", NotifyType.StatusMessage);

            // Find all paired instances of the Rfcomm chat service
            BluetoothServiceInfoCollection = await DeviceInformation.FindAllAsync(
                RfcommDeviceService.GetDeviceSelector(RfcommServiceId.FromUuid(RfcommGAIAServiceUuid)));

            DeviceInformation bluetoothServiceInfo = null;
            if (BluetoothServiceInfoCollection.Count > 0)
            {
                
                List<string> items = new List<string>();
                foreach (var chatServiceInfo in BluetoothServiceInfoCollection)
                {
                    items.Add(chatServiceInfo.Name);
                    //Added to print services!
                    string hid = WearhausHttpController.ParseHID(chatServiceInfo.Id);
                    if (hid.StartsWith("1CF03E") || hid.StartsWith("00025B")) {
                        bluetoothServiceInfo = chatServiceInfo;
                    }
                }
                cvs.Source = items;

                if (bluetoothServiceInfo != null)
                {
                    try
                    {

                        // Potential Bug Fix?? Wrap FromIdAsync call in the UI Thread, as per the instructions of:
                        // http://blogs.msdn.com/b/wsdevsol/archive/2014/11/10/why-doesn-t-the-windows-8-1-bluetooth-rfcomm-chat-sample-work.aspx
                        // EDIT: Actually may not work as all this does is bring the exception thrown outside of this thread, so the exception is unhandled...

                        //await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                        //{
                        // ONLY WORKS IN WINDOWS 10 RIGHT NOW!?
                        BluetoothService = await RfcommDeviceService.FromIdAsync(bluetoothServiceInfo.Id);
                        //});


                        if (BluetoothService == null)
                        {
                            MainPage.Current.NotifyUser(
                                "Access to the device is denied because the application was not granted access",
                                NotifyType.StatusMessage);
                            return;
                        }

                        var attributes = await BluetoothService.GetSdpRawAttributesAsync();
                        if (!attributes.ContainsKey(SdpServiceNameAttributeId))
                        {
                            MainPage.Current.NotifyUser(
                                "The Chat service is not advertising the Service Name attribute (attribute id=0x100). " +
                                "Please verify that you are running the BluetoothRfcommChat server.",
                                NotifyType.ErrorMessage);
                            return;
                        }

                        var attributeReader = DataReader.FromBuffer(attributes[SdpServiceNameAttributeId]);
                        var attributeType = attributeReader.ReadByte();
                        if (attributeType != SdpServiceNameAttributeType)
                        {
                            MainPage.Current.NotifyUser(
                                "The Chat service is using an unexpected format for the Service Name attribute. " +
                                "Please verify that you are running the BluetoothRfcommChat server.",
                                NotifyType.ErrorMessage);
                            return;
                        }

                        var serviceNameLength = attributeReader.ReadByte();

                        // The Service Name attribute requires UTF-8 encoding.
                        attributeReader.UnicodeEncoding = UnicodeEncoding.Utf8;
                        //ServiceName.Text = "Connected to: \"" + bluetoothServiceInfo.Name + "\"";

                        lock (this)
                        {
                            BluetoothSocket = new StreamSocket();
                        }

                        await BluetoothSocket.ConnectAsync(BluetoothService.ConnectionHostName, BluetoothService.ConnectionServiceName);

                        BluetoothWriter = new DataWriter(BluetoothSocket.OutputStream);

                        DataReader chatReader = new DataReader(BluetoothSocket.InputStream);
                        ReceiveStringLoop(chatReader);

                        GaiaMessage firmware_version_request = new GaiaMessage((ushort)GaiaMessage.GaiaCommand.GetAppVersion); 
                        // Send a get app version Gaia req to the device to see what the firmware version is (for DFU)
                        SendRawBytes(firmware_version_request.BytesSrc);
                        // so we just wait for this resp



                    }
                    catch (Exception ex)
                    {
                        MyErrorString = "Try pressing verify again in a few minutes or restart your Arc.";
                        Disconnect();

                        //MyArcConnState = ArcConnState.Error;
                        // dfu state, most likely recoverable by restarting
                        UpdateUI();
                    }
                }
                else
                {
                    MyErrorString = "No Wearhaus Arc found. Please reconnect the Arc that was being updated again.";
                    UpdateUI();
                }
            }
            else
            {
                MyErrorString = "No Wearhaus Arc found. Please reconnect the Arc that was being updated again.";
                UpdateUI();
            }

        }*/
        




        /// <summary>
        /// Does NOT change ARcConnState; that must be done by the caller
        /// </summary>
        private void Disconnect()
        {
            try
            {
                Debug.WriteLine("Disconnect()");

                if (GaiaHandler != null)
                {
                    GaiaHandler.IsSendingFile = false;
                }

                if (BluetoothWriter != null)
                {
                    BluetoothWriter.DetachStream();
                    BluetoothWriter = null;
                }

                lock (this)
                {
                    if (BluetoothSocket != null)
                    {
                        BluetoothSocket.Dispose();
                        BluetoothSocket = null;
                    }
                }
            }
            catch (Exception ex)
            {
                MainPage.Current.NotifyUser("Error On Disconnect: " + ex.HResult.ToString() + " - " + ex.Message,
                   NotifyType.ErrorMessage);
                return;
            }
        }


        /// <summary>
        /// Button to disconnect from the currently connected device and
        /// end communication with the device (can be restarted with RunButton again)
        /// </summary>
        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            Disconnect();
            MyArcConnState = ArcConnState.NoArc;
            UpdateUI();

            MainPage.Current.NotifyUser("Disconnected", NotifyType.StatusMessage);
        }

        /// <summary>
        /// Button to run the filepicker to select the firmware file to update
        /// </summary>
        private async void UpdateDbg_Click(object sender, RoutedEventArgs e)
        {

            // Open DFU File Buffer and DataReader
            FileOpenPicker filePicker = new FileOpenPicker();
            filePicker.SuggestedStartLocation = PickerLocationId.Downloads;
            filePicker.FileTypeFilter.Clear();
            filePicker.FileTypeFilter.Add("*");
#if WINDOWS_PHONE_APP
            filePicker.PickSingleFileAndContinue();
#else
            DfuFile = await filePicker.PickSingleFileAsync();
#endif

            MainPage.Current.NotifyUser("", NotifyType.StatusMessage);
            if( DfuFile == null){
                return;
            }


            // Get CRC first from File
            var buf = await FileIO.ReadBufferAsync(DfuFile);
            DfuReader = DataReader.FromBuffer(buf);
            uint fileSize = buf.Length;
            byte[] fileBuffer = new byte[fileSize];
            DfuReader.ReadBytes(fileBuffer);
            GaiaHandler.SetFileBuffer(fileBuffer);


            // Send DFU!
            DisconnectButton.IsEnabled = false;
            GaiaMessage startDfuCmd = new GaiaMessage((ushort)GaiaMessage.ArcCommand.StartDfu);
            SendRawBytes(startDfuCmd.BytesSrc);
            //DfuProgressBar.Visibility = Windows.UI.Xaml.Visibility.Visible;
            //DfuProgressBar.IsIndeterminate = true;
            //DfuStatus.Visibility = Windows.UI.Xaml.Visibility.Visible;
            //DfuStatus.Text = "Beginning Update...";

            MyDFUStep = DFUStep.StartingUpload;
            UpdateUI();

            //TopInstruction.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            //PickFileButton.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            //SendDfuButton.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
        }

        /// <summary>
        /// Button to begin checking the server for a DFU File according to which firmware version is the most updated.
        /// Will continue without further user input (except in the case of an error).
        /// </summary>
        /// TODO TODO TODO This is the upodate endpoint for NORMAL muggle update
        /*private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("UpdateButton_Click()");

            MainPage.Current.NotifyUser("", NotifyType.StatusMessage);

            // NOTE: This not only returns the most recent Firmware ID but it also updates our entire Static Firmware Table in Firmware.cs
            try
            {
                ProgressStatus.Visibility = Windows.UI.Xaml.Visibility.Visible;
                ProgressStatus.Text = "Checking for updates...";
                DfuProgressBar.Visibility = Windows.UI.Xaml.Visibility.Visible;
                DfuProgressBar.IsIndeterminate = true;

                // TODO parse correctly
                string latestFirmwareShortCode = "1202";
                //string latestFirmwareShortCode = await HttpController.GetLatestFirmwareTable();

                // If latest Firmware is empty from the server, then we have disabled firmware updates for now
                if (latestFirmwareShortCode == "" || latestFirmwareShortCode == null)
                {
                    Instructions.Text = "It seems the update service is temporarily down. There are no updates for now.";
                    ProgressStatus.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                    DfuProgressBar.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                    DfuProgressBar.IsIndeterminate = false;
                    return;
                }

                System.Diagnostics.Debug.WriteLine("LATEST FIRMWARE: " + latestFirmwareShortCode);
                Firmware firmwareToUpdate = Firmware.FirmwareTable[latestFirmwareShortCode];

                if (firmwareToUpdate.fullCode == HttpController.Current_fv)
                {
                    Instructions.Text = "Your Wearhaus Arc is already up to date. You're all done!";
                    ProgressStatus.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                    DfuProgressBar.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                    DfuProgressBar.IsIndeterminate = false;
                    return;
                }

                ProgressStatus.Visibility = Windows.UI.Xaml.Visibility.Visible;
                ProgressStatus.Text = "Downloading Firmware Update...";

                HttpController.Attempted_fv = firmwareToUpdate.fullCode;
                string currentFV_ShortCode = HttpController.Current_fv.Substring(14, 4);
                if (!firmwareToUpdate.validBases.Contains(currentFV_ShortCode))
                {
                    string text = "Invalid base in update - valid bases are: [";
                    foreach (string s in firmwareToUpdate.validBases)
                    {
                        //textBox3.Text += ("Key = {0}, Value = {1}", kvp.Key, kvp.Value);
                        text += string.Format("\t{0},", s);
                    }
                    text += "]";
                    System.Diagnostics.Debug.WriteLine(text);
                    ProgressStatus.Text = "This version of the firmware cannot be directly updated to the latest version. Please contact customer support at support@wearhaus.com ERROR 10";
                    string response = await HttpController.DfuReport(10);
                    return;
                }

                DfuProgressBar.Visibility = Windows.UI.Xaml.Visibility.Visible;
                DfuProgressBar.IsIndeterminate = true;

                byte[] fileBuffer = await HttpController.DownloadDfuFile(firmwareToUpdate.url);

                
                if (fileBuffer == null)
                {
                    System.Diagnostics.Debug.WriteLine("There was an error / problem downloading the firmware update. Please try again.");
                    ProgressStatus.Text = "There was an error downloading the firmware update. Please make sure you are connected to the internet.";
                    DfuProgressBar.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                    DfuProgressBar.IsIndeterminate = false;

                    string response = await HttpController.DfuReport(2);
                    return;
                }

                GaiaHandler.SetFileBuffer(fileBuffer);
                Instructions.Text = "Downloaded File: " + firmwareToUpdate.desc;
                // Send DFU!
                DisconnectButton.IsEnabled = false;
                UpdateButton.IsEnabled = false;
                GaiaMessage startDfuCmd = new GaiaMessage((ushort)GaiaMessage.ArcCommand.StartDfu);

                SendRawBytes(startDfuCmd.BytesSrc);
                DfuProgressBar.Visibility = Windows.UI.Xaml.Visibility.Visible;
                DfuProgressBar.IsIndeterminate = true;
                ProgressStatus.Visibility = Windows.UI.Xaml.Visibility.Visible;
                ProgressStatus.Text = "Finished downloading file. Beginning Update...";

                //Instructions.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                PickFileButton.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                SendDfuButton.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                if (ex.Message.ToLower().Contains("json"))
                {
                    Instructions.Text = "Could not reach the server. Your internet may be disconnected or the server is down.";
                    System.Diagnostics.Debug.WriteLine("Error: " + ex.HResult.ToString() + " - " + ex.Message);
                    return;
                }
                else
                {
                    Instructions.Text = "There was an error downloading the update. Please make sure you are connected to the internet and are connected to your Wearhaus Arc. The server may be down. If the problem persists, contact Wearhaus Support support@wearhaus.com";
                    System.Diagnostics.Debug.WriteLine("Error: " + ex.HResult.ToString() + " - " + ex.Message);
                    return;
                }
            }
        }*/


        /// <summary>
        /// FOR DEBUG ONLY
        /// Sends an input 16-bit command ID to the device
        /// </summary>
        /*private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MessageTextBox.Text == "") { return; }
                if (MessageTextBox.Text.Contains("/"))
                {
                    // FOR HTTP POST DEBUG
                    //HttpController.HttpGet(MessageTextBox.Text);
                }
                else
                {
                    ushort usrCmd = Convert.ToUInt16(MessageTextBox.Text, 16);

                    GaiaMessage msg = new GaiaMessage(usrCmd);
                    SendRawBytes(msg.BytesSrc);
                }
            }
            catch (Exception ex)
            {
                MainPage.Current.NotifyUser("Error: " + ex.HResult.ToString() + " - " + ex.Message,
                    NotifyType.StatusMessage);
                Disconnect();
            }
        }*/

        /// <summary>
        /// Send bytes from a Byte Array down the socket to the currently connected device
        /// </summary>
        /// <param name="msg">Byte Array containing the bytes of the data to send</param>
        /// <param name="print">Optional Flag to print the sent data into the debug panel</param>
        private async void SendRawBytes(byte[] msg, bool print = true)
        {
            try
            {
                BluetoothWriter.WriteBytes(msg);
                await BluetoothWriter.StoreAsync();
                string sendStr = BitConverter.ToString(msg);

                if (print)
                {
                    //ConversationList.Items.Add("Sent: " + sendStr);
                    System.Diagnostics.Debug.WriteLine("Sent: " + sendStr);

                }
                //MessageTextBox.Text = "";
            }
            catch (Exception ex)
            {
                MainPage.Current.NotifyUser("Error: " + ex.HResult.ToString() + " - " + ex.Message,
                    NotifyType.StatusMessage);
                Disconnect();
            }
        }

#if WINDOWS_PHONE_APP
        /// <summary> 
        /// Handle the returned files from file picker 
        /// This method is triggered by ContinuationManager based on ActivationKind 
        /// </summary> 
        /// <param name="args">
        /// File open picker continuation activation arugment. 
        /// It cantains the list of files user selected with file open picker 
        /// </param> 
        public void ContinueFileOpenPicker(FileOpenPickerContinuationEventArgs args)
        {
            if (args.Files.Count > 0)
            {
                DfuFile = args.Files[0];
                MainPage.Current.NotifyUser("Picked File: " + DfuFile.Name, NotifyType.StatusMessage);
            }
            else
            {
                DfuFile = null;
                MainPage.Current.NotifyUser("Not a Valid File / No File Chosen!", NotifyType.StatusMessage);
            }
        }
#endif

        /// <summary>
        /// Main receive logic loop to handle data coming in on the socket connected to the bluetoth device
        /// Async, Recursive method - only needs to be called once outside this function
        /// </summary>
        /// <param name="chatReader">
        /// DataReader object encapsulating the Socket used for 
        /// communicating with the connectedbluetooth device
        /// </param>
        private async void ReceiveStringLoop(DataReader chatReader)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("ReceiveStringLoop()");

                byte frameLen = GaiaMessage.GAIA_FRAME_LEN;

                // Frame is always FRAME_LEN long at least, so load that many bytes and process the frame
                uint size = await chatReader.LoadAsync(frameLen);

                // Buffer / Stream is closed 
                if (size < frameLen)
                {
                    System.Diagnostics.Debug.WriteLine("   size < frameLen");

                    if (GaiaHandler.IsSendingFile)
                    {

                        
                        System.Diagnostics.Debug.WriteLine("TopInstruction.Text = Firmware update complete.Your Arc will...");
                        //TopInstruction.Text = "HIHI Wait a few more minutes. Once your Arc says 'Device Connected', press the Veriufy Button below";
                        GaiaHandler.IsSendingFile = false;
                        DfuIsWaitingForVerification = true;

                        Disconnect();

                        MyDFUStep = DFUStep.AwaitingManualPowerCycle;
                        UpdateUI();
                        return;
                    }
                    else
                    {
                        MainPage.Current.NotifyUser("Disconnected from Stream!", NotifyType.ErrorMessage);
                        Disconnect();
                        return;
                    }
                }

                string receivedStr = "";
                byte[] receivedFrame = new byte[frameLen];
                chatReader.ReadBytes(receivedFrame);

                receivedStr += BitConverter.ToString(receivedFrame);
                byte payloadLen = receivedFrame[3];

                byte[] payload = new byte[payloadLen];
                if (payloadLen > 0)
                {
                    await chatReader.LoadAsync(payloadLen);
                    chatReader.ReadBytes(payload);
                    receivedStr += " Payload: " + BitConverter.ToString(payload); 
                }

                GaiaMessage receivedMessage = new GaiaMessage(receivedFrame, payload);

                GaiaMessage resp;
                // If we get 0x01 in the Flags, we received a CRC (also we should check it probably)
                if (receivedMessage.IsFlagSet)
                {
                    await chatReader.LoadAsync(sizeof(byte));
                    byte checksum = chatReader.ReadByte();
                    receivedStr += " CRC: " + checksum.ToString("X2");
                    System.Diagnostics.Debug.WriteLine(" CRC: " + checksum.ToString("X2"));


                    // Now we should have all the bytes, lets process the whole thing!
                    resp = GaiaHandler.CreateResponseToMessage(receivedMessage, checksum);
                }
                else
                {
                    // Now we should have all the bytes, lets process the whole thing!
                    resp = GaiaHandler.CreateResponseToMessage(receivedMessage);
                }

                if (resp != null && resp.InfoMessage != null)
                {
                    receivedStr += resp.InfoMessage;
                }

                if (receivedMessage.CommandId == (ushort)GaiaMessage.GaiaCommand.GetAppVersion) // Specifically handling the case to find out the current Firmware version
                {
                    string firmware_ver = WearhausHttpController.ParseFirmwareVersion(receivedMessage.PayloadSrc);
                    System.Diagnostics.Debug.WriteLine("gaia says firmware_ver: " + firmware_ver);

                    if (DfuIsWaitingForVerification)
                    {
                        DfuIsWaitingForVerification = false;
                        MyFvFullCode = firmware_ver;

                        if (MyTargetFirmware != null)
                        {

                            MyFirmwareVersion = Firmware.GetFirmwareFromFullCode(MyFvFullCode);
                            // try to get new version; if null, then what happened??
                            if (MyFirmwareVersion != null && MyFirmwareVersion == MyTargetFirmware)
                            {
                                MyDFUStep = DFUStep.Success;
                                MyDfuErrorNum = DFUResultStatus.Success;
                                
                                string response = await MyHttpController.DfuReport(0);
                            }
                            else
                            {
                                MyDFUStep = DFUStep.ErrorUnrecoverable;
                                MyDfuErrorNum = DFUResultStatus.FvMismatch;
                                
                                string response = await MyHttpController.DfuReport(6);
                            }


                        } else
                        {
                            MyDFUStep = DFUStep.SuccessDBG;
                            MyDfuErrorNum = DFUResultStatus.Success;
                        }
                        UpdateUI();

                    }
                    else
                    {
                        MyFvFullCode = firmware_ver;
                        MyFvOldFullCode = firmware_ver;

                        if (MyHttpController.MyAccountState == WearhausHttpController.AccountState.None)
                        {
                            MyHttpController.AccountStateChanged += UpdateUIListener;
                            await MyHttpController.startServerRegistration(MyHid, MyFvFullCode);
                        }

                        MyArcConnState = ArcConnState.Connected;
                        UpdateUI();
                        // Done gathering
                    }
                }

                //ConversationList.Items.Add("Received: " + receivedStr);
                Debug.WriteLine("Received: " + receivedStr);



                if (resp != null && resp.IsError)
                {
                    Debug.WriteLine(" resp.IsError: " + resp.InfoMessage);

                    MyDFUStep = DFUStep.ErrorUnrecoverable;
                    MyDfuErrorNum = DFUResultStatus.OtherFailure;
                    UpdateUI();

                    //DfuStatus.Text = resp.InfoMessage;
                    //DfuStatus.Visibility = Visibility.Visible;
                    GaiaHandler.IsSendingFile = false;
                    //DfuProgressBar.IsIndeterminate = false;

                    if (resp.DfuStatus != 0)
                    {
                        string response = await MyHttpController.DfuReport(resp.DfuStatus);
                    }
                }

                // DFU Files Sending case
                if (GaiaHandler.IsSendingFile)
                {
                    DfuProgress.Visibility = Visibility.Visible;
                    DfuProgress.IsIndeterminate = false;
                    DfuProgress.Value = 0;
                    MyDFUStep = DFUStep.UploadingFW;
                    UpdateUI();

                    int chunksRemaining = GaiaHandler.ChunksRemaining();
                    //ConversationList.Items.Add("DFU Progress | Chunks Remaining: " + chunksRemaining);

                    // Loop to continually send raw bytes until we finish sending the whole file
                    while (chunksRemaining > 0)
                    {
                        byte[] msg = GaiaHandler.GetNextFileChunk();

                        // Strange Thread Async bug: We must use these two lines instead of a call to SendRawBytes()
                        // in order to actually allow the DFUProgressBar to update
                        BluetoothWriter.WriteBytes(msg);
                        await BluetoothWriter.StoreAsync();
                        //SendRawBytes(msg, false);
                        chunksRemaining = GaiaHandler.ChunksRemaining();

                        //DfuStatus.Text = "Update in progress...";
                        
                        DfuProgress.Value = 100 * (float)(GaiaHandler.TotalChunks - chunksRemaining) / (float)GaiaHandler.TotalChunks;

                        if (chunksRemaining % 1000 == 0)
                        {
                            //ConversationList.Items.Add("DFU Progress | Chunks Remaining: " + chunksRemaining);
                            System.Diagnostics.Debug.WriteLine("Chunks Remaining: " + chunksRemaining);
                        }
                        

                    }
                    MyDFUStep = DFUStep.VerifyingImage;
                    UpdateUI();

                    //DfuStatus.Text = "Finished sending file. Verifying... (Your Arc will restart soon after this step.)";
                    //DfuStatus.Visibility = Visibility.Visible;
                    //DfuProgress.IsIndeterminate = true;
                    //ConversationList.Items.Add("Finished Sending DFU. Verifying...");
                    System.Diagnostics.Debug.WriteLine("Finished Sending DFU. Verifying...");


                }

                if (resp != null && !resp.IsError) SendRawBytes(resp.BytesSrc);

                ReceiveStringLoop(chatReader);
            }
            catch (Exception ex)
            {
                lock (this)
                {
                    if (BluetoothSocket == null)
                    {
                        // Do not print anything here -  the user closed the socket.
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Read stream failed with error: " + ex);
                        Disconnect();

                        if (MyDFUStep == DFUStep.None)
                        {
                            MyArcConnState = ArcConnState.Error;
                            MyErrorString = "Error connecting to Arc";
                            UpdateUI();
                        } else
                        {
                            MyDFUStep = DFUStep.ErrorUnrecoverable;
                            MyDfuErrorNum = DFUResultStatus.IOException;
                        }
                        
                    }
                }
            }
        }


        /// //////////////////
        /// //////////////////
        /// UI stuff added post revert try2
        /// //////////////////
        /// //////////////////



        private void previousItem_Click(object sender, RoutedEventArgs e)
        {
            UpdateInstructionFrame(ImageFrame - 1);
        }
        private void nextItem_Click(object sender, RoutedEventArgs e)
        {
            UpdateInstructionFrame(ImageFrame + 1);
        }

        private void UpdateInstructionFrame(int newFrame)
        {
            newFrame = Math.Min(newFrame, InstructionTexts.Count);
            newFrame = Math.Max(newFrame, 0);

            ImageFrame = newFrame;

            if (ImageFrame >= InstructionImages.Count - 1)
            {
                NextButton.Opacity = 0.5;
                NextButton.IsEnabled = false;
                PreviousButton.Opacity = 1.0;
                PreviousButton.IsEnabled = true;
            }
            else if (ImageFrame <= 0)
            {
                NextButton.Opacity = 1.0;
                NextButton.IsEnabled = true;
                PreviousButton.Opacity = 0.5;
                PreviousButton.IsEnabled = false;
            }
            else
            {
                NextButton.Opacity = 1.0;
                NextButton.IsEnabled = true;
                PreviousButton.Opacity = 1.0;
                PreviousButton.IsEnabled = true;
            }

            InstructionImage.Source = new BitmapImage(new Uri("ms-appx:///Assets/" + InstructionImages[ImageFrame].ToString()));
            InstructionText.Text = InstructionTexts[ImageFrame];
        }



        private void UpdateFV_Click(object sender, RoutedEventArgs e)
        {
            // TODO
        }

        private void HowToButton_Click(object sender, RoutedEventArgs e)
        {
            updateInstructVisibility(!InstrucVisible);
        }
        private void DetailsExpand_Click(object sender, RoutedEventArgs e)
        {
            updateDashboardExpandVisibility(!DashboardExpandVisible);
        }

        private void updateInstructVisibility(Boolean b)
        {
            InstrucVisible = b;
            if (InstrucVisible)
            {
                InstructionLayout.Visibility = Visibility.Visible;
            }
            else
            {
                InstructionLayout.Visibility = Visibility.Collapsed;
            }
        }

        private void updateDashboardVisibility(Boolean b)
        {
            DashboardVisible = b;
            if (DashboardVisible)
            {
                DashboardLayout.Visibility = Visibility.Visible;
            }
            else
            {
                DashboardLayout.Visibility = Visibility.Collapsed;
            }
        }

        private void updateDashboardExpandVisibility(Boolean b)
        {
            DashboardExpandVisible = b;
            if (DashboardExpandVisible)
            {
                HeadphoneIdTextLeft.Visibility = Visibility.Visible;
                FVStringTextLeft.Visibility = Visibility.Visible;
                HidText.Visibility = Visibility.Visible;
                FvFullText.Visibility = Visibility.Visible;
                DetailsExpand.Content = "Show Less";
            }
            else
            {
                HeadphoneIdTextLeft.Visibility = Visibility.Collapsed;
                FVStringTextLeft.Visibility = Visibility.Collapsed;
                HidText.Visibility = Visibility.Collapsed;
                FvFullText.Visibility = Visibility.Collapsed;
                DetailsExpand.Content = "Show More";
            }
        }

    }
}
