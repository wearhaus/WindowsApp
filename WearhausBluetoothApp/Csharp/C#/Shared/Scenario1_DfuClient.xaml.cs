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
        private Windows.UI.Xaml.DispatcherTimer DfuReconnectTimer;

        // ArcLink local vars
        private String MyHid;
        private int MyProductId;
        private String MyFvFullCode;
        private String MyFvOldFullCode;
        private String MyDeviceHumanName;
        private Firmware MyFirmwareVersion;
        // will be null when chosen from a file
        private Firmware MyTargetFirmware;
        private Firmware MyPotentialTargetFirmware;



        private DFUStep MyDFUStep;
        private ArcConnState MyArcConnState;
        private String MyErrorString;
        // None until dfu reaches an end state that requires restarting arc/app or success.
        private DFUResultStatus MyDfuErrorNum = DFUResultStatus.None;
        private Boolean ReportedDfuToServer = false;

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
            // 2 minute grace period where we hide this odd fact of DFU
            AwaitingManualPowerCycleForcedWait,
            // Now the user has control to try to press VerifyAgain
            AwaitingManualPowerCycle,
            // Connecting. Next states are AwaitingManualPowerCycle with a temp error, or a success
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
            UpdateFVButton.IsEnabled = false;
            // manually opened by button, so not directly related to UIState
            updateInstructVisibility(false);
            updateDashboardExpandVisibility(false);

            InstructionImages.Add("arc_update_1.png");
            InstructionImages.Add("arc_update_2.png");
            InstructionImages.Add("arc_update_3.png");
            //InstructionTexts.Add("Step 1: Turn on your Wearhaus Arc. Then open Windows Bluetooth Settings by searching for Bluetooth Settings in the Windows Search Bar.");
            InstructionTexts.Add("Step 1: Turn on your Wearhaus Arc. Then open Windows Bluetooth Settings.");
            InstructionTexts.Add("Step 2: Find Wearhaus Arc and click \"Pair\". Click \"Yes\" to any prompts that ask for permission.");
            InstructionTexts.Add("Step 3: After it connects, press My Arc is Paired above.");

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
                    // If this error state is hit, redo everything
                    ConnectButton.Content = "Try Again";
                    updateDashboardVisibility(false);
                    Disconnect();

                    MyArcConnState = ArcConnState.Error;
                    MyErrorString = "Error connecting to server. Please make sure you have internet access and try again. If this problem continues, try updating this app or contact customer support at wearhaus.com";
                    UpdateUI();


                    //ArcStateText.Text = "Error connecting to server. Please make sure you have internet access and try again. If this problem continues, try updating this app or contact customer support at wearhaus.com";
                    //ConnectButton.Content = "Try Again";
                    //ConnectButton.IsEnabled = true;
                    //ConnectButton.Opacity = 1.0;
                    //ConnectionProgress.Opacity = 0.0;
                    //updateDashboardVisibility(false);
                    //Disconnect();
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
                MyPotentialTargetFirmware = null;

                if (MyFirmwareVersion != null) {
                    Debug.WriteLine("MyFirmwareVersion != null ");

                    // we know our version, let's check if we can update
                    FirmwareText.Text = Firmware.FirmwareTable[uniqueCode].humanName;
                    String latestUnique = Firmware.LatestByProductId[MyProductId + ""];
                    Debug.WriteLine("latestUnique = " + latestUnique);

                    if (latestUnique != null && (latestUnique.Length == 4 || latestUnique.Length == 5)
                        && Firmware.FirmwareTable[latestUnique] != null && Firmware.FirmwareTable[latestUnique].validBases.Contains(uniqueCode))
                    {
                        Debug.WriteLine("Detected new firmware version available for this Arc: " + latestUnique);
                        MyPotentialTargetFirmware = Firmware.FirmwareTable[latestUnique];
                        showDfuStartUI = true;
                    }
                } else {
                    FirmwareText.Text = "Unknown";
                    showDfuStartUI = false;
                    // TODO report to server
                }

                if (showDfuStartUI)
                {
                    UpdateFVButton.Visibility = Visibility.Visible;
                    UpdateFVButton.IsEnabled = true;
                    FirmwareUpToDate.Text = "There is a new firmware update available for your Arc";
                    FirmwareDescText.Text = MyPotentialTargetFirmware.desc;
                } else  {
                    UpdateFVButton.Visibility = Visibility.Collapsed;
                    UpdateFVButton.IsEnabled = false;
                    FirmwareUpToDate.Text = "Your Arc is up to date";
                    // if we are uptodate, then display any inbof aobut our current version
                    if (MyFirmwareVersion != null){
                        FirmwareDescText.Text = MyFirmwareVersion.desc;
                    } else {
                        FirmwareDescText.Text = "";
                    }
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
                    case DFUStep.VerifyingImage:
                    case DFUStep.ChipPowerCycle:
                        //DfuStateText.Text = "Your Arc will automatically restart - please listen to your Arc for a double beep sound to indicate a restart. When you hear the beep or have waited 30 seconds, please press the \"Verify Update\" button to verify that the update worked.";
                    case DFUStep.AwaitingManualPowerCycleForcedWait:
                        DfuStateText.Text = "Updating";
                        DfuProgress.Opacity = 1.0;
                        DfuProgress.IsIndeterminate = true;
                        break;

                    case DFUStep.AwaitingManualPowerCycle:
                        DfuProgress.Opacity = 0.0;
                        VerifyDfuButton.IsEnabled = true;
                        VerifyDfuButton.Opacity = 1.0;
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
                        DfuStateText.Text = "Firmware successfully updated to " + MyTargetFirmware.humanName + ". Enjoy your newly updated Arc!";
                        // TODO consider adding in a show button that shows the MyTargetFirmware.desc here
                        if (!ReportedDfuToServer && MyTargetFirmware != null) {
                            MyHttpController.DfuReport((int)DFUResultStatus.Success,
                                    MyFvOldFullCode, MyFvFullCode, MyTargetFirmware.fullCode);
                        }
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
                            if (!ReportedDfuToServer && MyTargetFirmware != null) {
                                ReportedDfuToServer = true;
                                MyHttpController.DfuReport((int)MyDfuErrorNum,
                                    MyFvOldFullCode, MyFvFullCode, MyTargetFirmware.fullCode);
                            }
                        } else {
                            // TODO is it even possible to reach thos code?
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
                                MyErrorString = "Make sure the updating Arc is connected in Bluetooth Settings and press Verify below.";
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
                        MyErrorString = "Please reconnect the Arc that was being updated again.";
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
                    MyErrorString = "Please reconnect the Arc that was being updated again.";
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
                Debug.WriteLine("Error On Disconnect: " + ex);
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
                    Debug.WriteLine("Sent: " + sendStr);

                }
                //MessageTextBox.Text = "";
            }
            catch (Exception ex)
            {
                Debug.WriteLine("SendRawBytes Error: " + ex);
                Disconnect();
                if (MyDFUStep != DFUStep.None) {
                    MyDFUStep = DFUStep.ErrorUnrecoverable;
                    MyDfuErrorNum = DFUResultStatus.IOException;
                } else {
                    MyErrorString = "Arc Disconnected";
                    MyArcConnState = ArcConnState.Error;
                }
                UpdateUI();
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
                Debug.WriteLine("ReceiveStringLoop()");

                byte frameLen = GaiaMessage.GAIA_FRAME_LEN;

                // Frame is always FRAME_LEN long at least, so load that many bytes and process the frame
                uint size = await chatReader.LoadAsync(frameLen);

                // Buffer / Stream is closed 
                if (size < frameLen)
                {
                    Debug.WriteLine("Buffer / Stream is closed   (size < frameLen)");

                    if (GaiaHandler.IsSendingFile)
                    {

                        
                        Debug.WriteLine("TopInstruction.Text = Firmware update complete.Your Arc will...");
                        //TopInstruction.Text = "HIHI Wait a few more minutes. Once your Arc says 'Device Connected', press the Veriufy Button below";
                        GaiaHandler.IsSendingFile = false;
                        DfuIsWaitingForVerification = true;

                        Disconnect();

                        MyErrorString = null;
                        MyDFUStep = DFUStep.AwaitingManualPowerCycleForcedWait;
                        UpdateUI();

                        // Start timer for reconnect attempt; it takes about 2 minutes for current versions.
                        // After 2 minutes, try an auto reconnect, and if that fails, prompt user to reconnect
                        // their arc under BT
                        DfuReconnectTimer = new DispatcherTimer();
                        DfuReconnectTimer.Interval = TimeSpan.FromSeconds(90);
                        DfuReconnectTimer.Tick += ReconnectTimerCalled;
                        DfuReconnectTimer.Start();
                        Debug.WriteLine("90 sec Timer started before auto-verify attempt");

                        return;
                    }
                    else
                    {
                        if (MyDFUStep == DFUStep.None) {
                            // User manually disconnect Arc
                            MyArcConnState = ArcConnState.Error;
                            MyErrorString = "Arc Disconnected";
                            UpdateUI();

                        } else {
                            MyDFUStep = DFUStep.ErrorUnrecoverable;
                            MyDfuErrorNum = DFUResultStatus.DisconnectedDuring;
                            UpdateUI();
                            // User manually disconnect Arc during the update. May or may not mean update still succeeded.
                        }
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
                                
                                //string response = await MyHttpController.DfuReport(0);
                            }
                            else
                            {
                                MyDFUStep = DFUStep.ErrorUnrecoverable;
                                MyDfuErrorNum = DFUResultStatus.FvMismatch;
                                
                                //string response = await MyHttpController.DfuReport(6);
                                // let UI send the report
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


                    GaiaHandler.IsSendingFile = false;

                    if (resp.DfuStatus != DFUResultStatus.None){
                        MyDFUStep = DFUStep.ErrorUnrecoverable;
                        MyDfuErrorNum = resp.DfuStatus;
                        UpdateUI();

                        return;
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
                        // TODO test this area, can this even happen
                        Debug.WriteLine("the user deliberately closed the socket. " + ex);
                        if (MyDFUStep == DFUStep.None){
                            MyArcConnState = ArcConnState.Error;
                            MyErrorString = "Error connecting to Arc";
                        }
                    } else {
                        Debug.WriteLine("Read stream failed with error: " + ex);
                        Disconnect();

                        if (MyDFUStep == DFUStep.None) {
                            MyArcConnState = ArcConnState.Error;
                            MyErrorString = "Error connecting to Arc";
                        } else {
                            MyDFUStep = DFUStep.ErrorUnrecoverable;
                            MyDfuErrorNum = DFUResultStatus.DisconnectedDuring;
                        }
                    }
                    UpdateUI();
                }
            }
        }


        //void ReconnectTimerCalled(object sender, EventArgs e)
        // contrary to advice on stackoverflow, the 2nd param also must be generic object
        private void ReconnectTimerCalled(object sender, object e)
        {
            // try once, see what happens
            ConnectArc();
            DfuReconnectTimer.Stop();
        }




        private async void UpdateFVButton_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("UpdateFVButton_Click: ");
            if (MyDFUStep != DFUStep.None) return;
            if (MyPotentialTargetFirmware == null) return;

            MyTargetFirmware = MyPotentialTargetFirmware;
            MyDFUStep = DFUStep.StartingUpload;
            UpdateUI();

            byte[] fileBuffer = await MyHttpController.DownloadDfuFile(MyPotentialTargetFirmware);
            if (fileBuffer == null) {
                MyDFUStep = DFUStep.ErrorUnrecoverable;
                MyDfuErrorNum = DFUResultStatus.DownloadFailed;
                UpdateUI();
            } else {
                StartDfu(fileBuffer);
            }
        }



        /// <summary>
        /// Button to run the filepicker to select the firmware file to update
        /// </summary>
        private async void UpdateDbg_Click(object sender, RoutedEventArgs e)
        {
            if (MyDFUStep != DFUStep.None) {
                return;
            }

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

            if (DfuFile == null) {
                return;
            }


            // Get CRC first from File
            var buf = await FileIO.ReadBufferAsync(DfuFile);
            DfuReader = DataReader.FromBuffer(buf);
            uint fileSize = buf.Length;
            byte[] fileBuffer = new byte[fileSize];
            DfuReader.ReadBytes(fileBuffer);
            MyTargetFirmware = null;
            StartDfu(fileBuffer);
        }


        private void StartDfu(byte[] fileBuffer)
        {
            GaiaHandler.SetFileBuffer(fileBuffer);

            GaiaMessage startDfuCmd = new GaiaMessage((ushort)GaiaMessage.ArcCommand.StartDfu);
            SendRawBytes(startDfuCmd.BytesSrc);

            MyDFUStep = DFUStep.StartingUpload;
            UpdateUI();
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
                UpdateDbg.Visibility = Visibility.Visible;
                DetailsExpand.Content = "Show Less";
            }
            else
            {
                HeadphoneIdTextLeft.Visibility = Visibility.Collapsed;
                FVStringTextLeft.Visibility = Visibility.Collapsed;
                HidText.Visibility = Visibility.Collapsed;
                FvFullText.Visibility = Visibility.Collapsed;
                UpdateDbg.Visibility = Visibility.Collapsed;
                DetailsExpand.Content = "Show More";
            }
        }

    }
}
