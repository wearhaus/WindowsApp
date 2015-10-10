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
        private static readonly Guid RfcommChatServiceUuid = Guid.Parse("00001107-D102-11E1-9B23-00025B00A5A5"); // "CSR Gaia Service"

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

        private WearhausHttpController HttpController;

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

            App.Current.Suspending += App_Suspending;
        }

        void App_Suspending(object sender, Windows.ApplicationModel.SuspendingEventArgs e)
        {
            // Make sure we cleanup resources on suspend
            Disconnect();
        }

        /// <summary>
        /// Message to start the UI Services and functions to search for nearby bluetooth devices
        /// </summary>
        private async void RunButton_Click(object sender, RoutedEventArgs e)
        {
            //this.Frame.Navigate(typeof(LoginPage), null);

            // Clear any previous messages
            MainPage.Current.NotifyUser("", NotifyType.StatusMessage);

            // Find all paired instances of the Rfcomm chat service
            BluetoothServiceInfoCollection = await DeviceInformation.FindAllAsync(
                RfcommDeviceService.GetDeviceSelector(RfcommServiceId.FromUuid(RfcommChatServiceUuid)));

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
                        RunButton.IsEnabled = false;
                        RunButton.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                        ServiceSelector.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                        TopInstruction.Visibility = Windows.UI.Xaml.Visibility.Collapsed;

                        ConnectionProgress.Visibility = Windows.UI.Xaml.Visibility.Visible;
                        ConnectionProgress.IsIndeterminate = true;
                        ConnectionStatus.Visibility = Windows.UI.Xaml.Visibility.Visible;

                        //var bluetoothServiceInfo = BluetoothServiceInfoCollection[ServiceList.SelectedIndex];


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
                        ServiceName.Text = "Connected to: \"" + bluetoothServiceInfo.Name + "\"";

                        lock (this)
                        {
                            BluetoothSocket = new StreamSocket();
                        }

                        await BluetoothSocket.ConnectAsync(BluetoothService.ConnectionHostName, BluetoothService.ConnectionServiceName);

                        BluetoothWriter = new DataWriter(BluetoothSocket.OutputStream);

                        GaiaHandler = new GaiaHelper(); // Create GAIA DFU Helper Object
                        HttpController = new WearhausHttpController(bluetoothServiceInfo.Id); // Create HttpController object

                        string guestResult = await HttpController.CreateGuest(); // Create a Guest account for basic information to the server
                        System.Diagnostics.Debug.WriteLine(guestResult);

                        DataReader chatReader = new DataReader(BluetoothSocket.InputStream);
                        ReceiveStringLoop(chatReader);

                        GaiaMessage firmware_version_request = new GaiaMessage((ushort)GaiaMessage.GaiaCommand.GetAppVersion); 
                        // Send a get app version Gaia req to the device to see what the firmware version is (for DFU)
                        SendRawBytes(firmware_version_request.BytesSrc);

                        ConnectionProgress.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                        ConnectionProgress.IsIndeterminate = false;
                        ConnectionStatus.Visibility = Windows.UI.Xaml.Visibility.Collapsed;

                        ChatBox.Visibility = Windows.UI.Xaml.Visibility.Visible;

                    }
                    catch (Exception ex)
                    {
                        TopInstruction.Text = "There was an error connecting to your Arc. Please make sure you are connected to the internet and are connected to your Wearhaus Arc in Bluetooth Settings and then run the App again!";
                        MainPage.Current.NotifyUser("Error: " + ex.HResult.ToString() + " - " + ex.Message,
                            NotifyType.ErrorMessage);
                        Disconnect();
                    }
                }
                else
                {
                    TopInstruction.Text = "No Wearhaus Arc found! Please double check to make sure you are connected to a Wearhaus Arc in Windows Bluetooth Settings. Then run the App again!";
                }
            }
            else
            {
                TopInstruction.Text = "No Bluetooth Devices found! Please connect to a Wearhaus Arc and then run the App again!";
                MainPage.Current.NotifyUser(
                    "No chat services were found. Please pair with a device that is advertising the chat service.",
                    NotifyType.ErrorMessage);
            }

        }
        

        /// <summary>
        /// Message to start the UI Services and functions to search for nearby bluetooth devices
        /// </summary>
        private async void VerifyDfuButton_Click(object sender, RoutedEventArgs e)
        {
            // Clear any previous messages
            MainPage.Current.NotifyUser("", NotifyType.StatusMessage);

            // Find all paired instances of the Rfcomm chat service
            BluetoothServiceInfoCollection = await DeviceInformation.FindAllAsync(
                RfcommDeviceService.GetDeviceSelector(RfcommServiceId.FromUuid(RfcommChatServiceUuid)));

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
                        RunButton.IsEnabled = false;
                        RunButton.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                        ServiceSelector.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                        TopInstruction.Visibility = Windows.UI.Xaml.Visibility.Collapsed;

                        ConnectionProgress.Visibility = Windows.UI.Xaml.Visibility.Visible;
                        ConnectionProgress.IsIndeterminate = true;
                        ConnectionStatus.Visibility = Windows.UI.Xaml.Visibility.Visible;

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
                        ServiceName.Text = "Connected to: \"" + bluetoothServiceInfo.Name + "\"";

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

                        VerifyDfuButton.Visibility = Windows.UI.Xaml.Visibility.Collapsed;

                        ConnectionProgress.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                        ConnectionProgress.IsIndeterminate = false;
                        ConnectionStatus.Visibility = Windows.UI.Xaml.Visibility.Collapsed;

                        TopInstruction.Text = "Verifying Firmware Version Matches...";
                        TopInstruction.Visibility = Windows.UI.Xaml.Visibility.Visible;
                        ConnectionProgress.IsIndeterminate = true;
                        ConnectionProgress.Visibility = Windows.UI.Xaml.Visibility.Visible;
                        ConnectionStatus.Visibility = Windows.UI.Xaml.Visibility.Visible;

                    }
                    catch (Exception ex)
                    {
                        TopInstruction.Text = "There was a problem connecting to your Wearhaus Arc. Please wait for the Wearhaus Arc to double-beep if you are doing a firmware update and then press Verify.";
                        MainPage.Current.NotifyUser("Error: " + ex.HResult.ToString() + " - " + ex.Message,
                            NotifyType.ErrorMessage);
                        Disconnect();
                    }
                }
                else
                {
                    TopInstruction.Text = "No Wearhaus Arc found! Please wait for the Wearhaus Arc to double-beep if you are doing a firmware update and then press Verify.";
                }
            }
            else
            {
                TopInstruction.Text = "No Bluetooth Devices found! Please wait for the Wearhaus Arc to double-beep if you are doing a firmware update and then press Verify.";
                MainPage.Current.NotifyUser(
                    "No chat services were found. Please pair with a device that is advertising the chat service.",
                    NotifyType.ErrorMessage);
            }

        }
        // ********************* THIS METHOD IS CURRENTLY NOT IN USE AND SHOULD NOT BE CALLED, THERE ARE NO MORE 'SERVICES' ********************
        /// <summary>
        /// Method to connect to the selected bluetooth device when clicking/tapping the device
        /// in the UI ServiceList
        /// </summary>
        private async void ServiceList_Tapped(object sender, TappedRoutedEventArgs e)
        {
            try
            {
                RunButton.IsEnabled = false;
                RunButton.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                ServiceSelector.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                TopInstruction.Visibility = Windows.UI.Xaml.Visibility.Collapsed;

                ConnectionProgress.Visibility = Windows.UI.Xaml.Visibility.Visible;
                ConnectionProgress.IsIndeterminate = true;
                ConnectionStatus.Visibility = Windows.UI.Xaml.Visibility.Visible;

                var bluetoothServiceInfo = BluetoothServiceInfoCollection[ServiceList.SelectedIndex];


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
                ServiceName.Text = "Connected to: \"" + bluetoothServiceInfo.Name + "\"";

                lock (this)
                {
                    BluetoothSocket = new StreamSocket();
                }

                await BluetoothSocket.ConnectAsync(BluetoothService.ConnectionHostName, BluetoothService.ConnectionServiceName);

                BluetoothWriter = new DataWriter(BluetoothSocket.OutputStream);
                ChatBox.Visibility = Windows.UI.Xaml.Visibility.Visible;

                GaiaHandler = new GaiaHelper(); // Create GAIA DFU Object
                HttpController = new WearhausHttpController(bluetoothServiceInfo.Id); // Create HttpController object

                DataReader chatReader = new DataReader(BluetoothSocket.InputStream);
                ReceiveStringLoop(chatReader);

                ConnectionProgress.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                ConnectionProgress.IsIndeterminate = false;
                ConnectionStatus.Visibility = Windows.UI.Xaml.Visibility.Collapsed;

                this.Frame.Navigate(typeof(LoginPage));

            }
            catch (Exception ex)
            {
                MainPage.Current.NotifyUser("Error: " + ex.HResult.ToString() + " - " + ex.Message, 
                    NotifyType.ErrorMessage);
                Disconnect();
            }
        }

        /// <summary>
        /// Method to clean up socket resources and disconnect from the bluetooth device
        /// Also resets UI Elements e.g. Buttons, progressbars, etc.
        /// Should put App in a state where RunButton can be clicked again to restart functionality
        /// </summary>
        private void Disconnect()
        {
            try
            {
                if (GaiaHandler.IsWaitingForVerification)
                {
                    RunButton.IsEnabled = false;
                    RunButton.Visibility = Windows.UI.Xaml.Visibility.Collapsed;

                    VerifyDfuButton.IsEnabled = true;
                    VerifyDfuButton.Visibility = Windows.UI.Xaml.Visibility.Visible;
                }
                else
                {
                    RunButton.IsEnabled = true;
                    RunButton.Visibility = Windows.UI.Xaml.Visibility.Visible;
                }
                TopInstruction.Visibility = Windows.UI.Xaml.Visibility.Visible;

                ConnectionProgress.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                ConnectionProgress.IsIndeterminate = false;
                ConnectionStatus.Visibility = Windows.UI.Xaml.Visibility.Collapsed;

                SendDfuButton.IsEnabled = false;
                ServiceSelector.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                ChatBox.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                DfuProgressBar.IsIndeterminate = false;
                DfuProgressBar.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                DfuProgressBar.Value = 0;

                Instructions.Visibility = Windows.UI.Xaml.Visibility.Visible;
                Instructions.Text = "Please select the file you want to update firmware with, then hit the Send DFU button!";
                PickFileButton.Visibility = Windows.UI.Xaml.Visibility.Visible;
                SendDfuButton.Visibility = Windows.UI.Xaml.Visibility.Visible;

                UpdateButton.IsEnabled = true;
                UpdateButton.Visibility = Windows.UI.Xaml.Visibility.Visible;

                if(GaiaHandler != null)GaiaHandler.IsSendingFile = false;
                ProgressStatus.Text = "";
                ProgressStatus.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                if(ConversationList != null)ConversationList.Items.Clear();

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
        /// Checkbox to toggle Debug view / ability to send commands
        /// </summary>
        private void DebugButton_Click(object sender, RoutedEventArgs e)
        {
            if (DebugControlGrid.Visibility == Windows.UI.Xaml.Visibility.Visible)
            {
                DebugControlGrid.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            }
            else if (DebugControlGrid.Visibility == Windows.UI.Xaml.Visibility.Collapsed)
            {
                DebugControlGrid.Visibility = Windows.UI.Xaml.Visibility.Visible;
            }
        }

        /// <summary>
        /// Button to disconnect from the currently connected device and
        /// end communication with the device (can be restarted with RunButton again)
        /// </summary>
        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            Disconnect();

            MainPage.Current.NotifyUser("Disconnected", NotifyType.StatusMessage);
        }

        /// <summary>
        /// Button to run the filepicker to select the firmware file to update
        /// </summary>
        private async void PickFileButton_Click(object sender, RoutedEventArgs e)
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

            SendDfuButton.IsEnabled = true;

            // Get CRC first from File
            var buf = await FileIO.ReadBufferAsync(DfuFile);
            DfuReader = DataReader.FromBuffer(buf);
            uint fileSize = buf.Length;
            byte[] fileBuffer = new byte[fileSize];
            DfuReader.ReadBytes(fileBuffer);
            GaiaHandler.SetFileBuffer(fileBuffer);

            Instructions.Text = "Picked File: " + DfuFile.Name + ". Press Send DFU to Begin Update, or Pick File again.";
        }

        /// <summary>
        /// Button to begin checking the server for a DFU File according to which firmware version is the most updated.
        /// Will continue without further user input (except in the case of an error).
        /// </summary>
        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            MainPage.Current.NotifyUser("", NotifyType.StatusMessage);

            string firmwareFullKey = "000001000AFFFF56150000000000000000";
            GaiaHandler.AttemptedFirmware = firmwareFullKey;

            FirmwareObj firmware = WearhausHttpController.FirmwareTable[GaiaHandler.AttemptedFirmware];

            DfuProgressBar.Visibility = Windows.UI.Xaml.Visibility.Visible;
            DfuProgressBar.IsIndeterminate = true;
            ProgressStatus.Visibility = Windows.UI.Xaml.Visibility.Visible;
            ProgressStatus.Text = "Downloading Firmware Update...";
            byte[] fileBuffer = await HttpController.DownloadDfuFile(firmware.url);

            
            if (fileBuffer == null)
            {
                MainPage.Current.NotifyUser("There was an error / problem downloading the Firmware Update. Please try again.", NotifyType.ErrorMessage);
                ProgressStatus.Text = "There was an error downloading the Firmware Update. Please make sure you are connected to the internet!";
                DfuProgressBar.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                DfuProgressBar.IsIndeterminate = false;

                string response = await HttpController.DfuReport(2);
                System.Diagnostics.Debug.WriteLine(response);
                return;
            }

            HttpController.Attempted_fv = GaiaHandler.AttemptedFirmware;
            GaiaHandler.SetFileBuffer(fileBuffer);
            Instructions.Text = "Downloaded File: " + firmware.desc;
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

        /// <summary>
        /// Button to begin DFU process, will start the process without any further user input (except in the case of an error)
        /// </summary>
        private void SendDfuButton_Click(object sender, RoutedEventArgs e)
        {
            MainPage.Current.NotifyUser("", NotifyType.StatusMessage);
            if (DfuFile == null)
            {
                MainPage.Current.NotifyUser("No DFU File Picked. Please Pick a DFU File!", NotifyType.StatusMessage);
                return;
            }
            // Send DFU!
            DisconnectButton.IsEnabled = false;
            GaiaMessage startDfuCmd = new GaiaMessage((ushort)GaiaMessage.ArcCommand.StartDfu);
            SendRawBytes(startDfuCmd.BytesSrc);
            DfuProgressBar.Visibility = Windows.UI.Xaml.Visibility.Visible;
            DfuProgressBar.IsIndeterminate = true;
            ProgressStatus.Visibility = Windows.UI.Xaml.Visibility.Visible;
            ProgressStatus.Text = "Beginning Update...";

            Instructions.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            PickFileButton.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            SendDfuButton.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
        }

        /// <summary>
        /// FOR DEBUG ONLY
        /// Sends an input 16-bit command ID to the device
        /// </summary>
        private void SendButton_Click(object sender, RoutedEventArgs e)
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
        }

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
                    ConversationList.Items.Add("Sent: " + sendStr);
                }
                MessageTextBox.Text = "";
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
                byte frameLen = GaiaMessage.GAIA_FRAME_LEN;

                // Frame is always FRAME_LEN long at least, so load that many bytes and process the frame
                uint size = await chatReader.LoadAsync(frameLen);

                // Buffer / Stream is closed 
                if (size < frameLen)
                {
                    if (GaiaHandler.IsSendingFile)
                    {
                        TopInstruction.Text = "Firmware Update Complete! Your Arc will automatically restart, please Listen to your Arc for a double beep startup sound to indicate a successful upgrade! When you hear the beep or have waited about 30 seconds, please press the Verify Update button to verify that the update worked!";
                        GaiaHandler.IsSendingFile = false;
                        GaiaHandler.IsWaitingForVerification = true;
                        Disconnect();
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
                    if (GaiaHandler.IsWaitingForVerification)
                    {
                        HttpController.Current_fv = firmware_ver;
                        if (HttpController.Current_fv == HttpController.Attempted_fv)
                        {
                            GaiaHandler.IsWaitingForVerification = false;
                            TopInstruction.Text = "Success! Your Firmware Update was Successfully Verified! Thank you for updating your Wearhaus Arc!";
                            ConnectionProgress.IsIndeterminate = false;
                            ConnectionProgress.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                            ConnectionStatus.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                            VerifyDfuButton.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                            RunButton.Visibility = Windows.UI.Xaml.Visibility.Collapsed;

                            string response = await HttpController.DfuReport(0);
                            System.Diagnostics.Debug.WriteLine(response);
                        }
                        else
                        {
                            GaiaHandler.IsWaitingForVerification = false;
                            TopInstruction.Text = "Firmware Update Failed. Try again, and if this error persists, contact customer support at wearhaus.com. Error 6";
                            ConnectionProgress.IsIndeterminate = false;
                            ConnectionProgress.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                            ConnectionStatus.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                            VerifyDfuButton.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                            RunButton.Visibility = Windows.UI.Xaml.Visibility.Collapsed;

                            string response = await HttpController.DfuReport(6);
                            System.Diagnostics.Debug.WriteLine(response);
                        }
                    }
                    else
                    {
                        HttpController.Old_fv = firmware_ver;
                        HttpController.Current_fv = firmware_ver;
                    }
                }

                ConversationList.Items.Add("Received: " + receivedStr);

                
                if (resp != null && resp.IsError)
                {
                    ProgressStatus.Text = resp.InfoMessage;
                    GaiaHandler.IsSendingFile = false;
                    DfuProgressBar.IsIndeterminate = false;

                    if (resp.DfuStatus != 0)
                    {
                        string response = await HttpController.DfuReport(resp.DfuStatus);
                        System.Diagnostics.Debug.WriteLine(response);
                    }
                }

                // DFU Files Sending case
                if (GaiaHandler.IsSendingFile)
                {
                    ConversationList.Items.Add("Receieved Go Ahead for DFU! Starting DFU now!");
                    ProgressStatus.Text = "Received Go Ahead for Update! Starting Update now!";
                    DfuProgressBar.IsIndeterminate = false;
                    //DFUProgressBar.Value = 0;

                    int chunksRemaining = GaiaHandler.ChunksRemaining();
                    ConversationList.Items.Add("DFU Progress | Chunks Remaining: " + chunksRemaining);

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

                        ProgressStatus.Text = "Update in progress...";
                        
                        DfuProgressBar.Value = 100 * (float)(GaiaHandler.TotalChunks - chunksRemaining) / (float)GaiaHandler.TotalChunks;

                        if (chunksRemaining % 1000 == 0)
                        {
                            ConversationList.Items.Add("DFU Progress | Chunks Remaining: " + chunksRemaining);
                        }
                        System.Diagnostics.Debug.WriteLine("Chunks Remaining: " + chunksRemaining);

                    }
                    ProgressStatus.Text = "Finished Sending File! Verifying... (Your Arc will restart soon after this step!)";
                    DfuProgressBar.IsIndeterminate = true;
                    ConversationList.Items.Add("Finished Sending DFU! Verifying...");

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
                        MainPage.Current.NotifyUser("Read stream failed with error: " + ex.Message, NotifyType.ErrorMessage);
                        Disconnect();
                    }
                }
            }
        }

        private void InfoViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {

        }

    }
}
