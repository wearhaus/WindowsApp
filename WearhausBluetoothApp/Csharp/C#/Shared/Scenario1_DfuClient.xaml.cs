// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.ComponentModel;
using System.Threading;
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
using WearhausHttp;
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
        
        public Scenario1_DfuClient()
        {
            this.InitializeComponent();

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

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {            
            rootPage = MainPage.Current;
        }

        /// <summary>
        /// Message to start the UI Services and functions to search for nearby bluetooth devices
        /// </summary>
        private async void RunButton_Click(object sender, RoutedEventArgs e)
        {
            // Clear any previous messages
            MainPage.Current.NotifyUser("", NotifyType.StatusMessage);

            // Find all paired instances of the Rfcomm chat service
            BluetoothServiceInfoCollection = await DeviceInformation.FindAllAsync(
                RfcommDeviceService.GetDeviceSelector(RfcommServiceId.FromUuid(RfcommChatServiceUuid)));

            if (BluetoothServiceInfoCollection.Count > 0)
            {
                List<string> items = new List<string>();
                foreach (var chatServiceInfo in BluetoothServiceInfoCollection)
                {
                    items.Add(chatServiceInfo.Name);
                    //Added to print services!
                }
                cvs.Source = items;
                ServiceSelector.Visibility = Windows.UI.Xaml.Visibility.Visible;
            }
            else
            {
                MainPage.Current.NotifyUser(
                    "No chat services were found. Please pair with a device that is advertising the chat service.",
                    NotifyType.ErrorMessage);
            }

        }
        
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

                DataReader chatReader = new DataReader(BluetoothSocket.InputStream);
                ReceiveStringLoop(chatReader);

                ConnectionProgress.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                ConnectionProgress.IsIndeterminate = false;
                ConnectionStatus.Visibility = Windows.UI.Xaml.Visibility.Collapsed;

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
                RunButton.IsEnabled = true;
                RunButton.Visibility = Windows.UI.Xaml.Visibility.Visible;
                TopInstruction.Visibility = Windows.UI.Xaml.Visibility.Visible;

                ConnectionProgress.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                ConnectionProgress.IsIndeterminate = false;
                ConnectionStatus.Visibility = Windows.UI.Xaml.Visibility.Collapsed;

                SendDFUButton.IsEnabled = false;
                ServiceSelector.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                ChatBox.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                DFUProgressBar.IsIndeterminate = false;
                DFUProgressBar.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                DFUProgressBar.Value = 0;

                Instructions.Visibility = Windows.UI.Xaml.Visibility.Visible;
                Instructions.Text = "Please select the file you want to update firmware with, then hit the Send DFU button!";
                PickFileButton.Visibility = Windows.UI.Xaml.Visibility.Visible;
                SendDFUButton.Visibility = Windows.UI.Xaml.Visibility.Visible;

                GaiaHandler.IsSendingFile = false;
                ProgressStatus.Text = "";
                ProgressStatus.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                ConversationList.Items.Clear();

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

            SendDFUButton.IsEnabled = true;

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
        /// Button to begin DFU process, will start the process without any further user input (except in the case of an error)
        /// </summary>
        private void SendDFUButton_Click(object sender, RoutedEventArgs e)
        {
            MainPage.Current.NotifyUser("", NotifyType.StatusMessage);
            if (DfuFile == null)
            {
                MainPage.Current.NotifyUser("No DFU File Picked. Please Pick a DFU File!", NotifyType.StatusMessage);
                return;
            }
            // Send DFU!
            GaiaMessage startDfuCmd = new GaiaMessage((ushort)GaiaMessage.ArcCommand.StartDfu);
            SendRawBytes(startDfuCmd.BytesSrc);
            DFUProgressBar.Visibility = Windows.UI.Xaml.Visibility.Visible;
            DFUProgressBar.IsIndeterminate = true;
            ProgressStatus.Visibility = Windows.UI.Xaml.Visibility.Visible;
            ProgressStatus.Text = "Beginning Update...";

            Instructions.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            PickFileButton.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            SendDFUButton.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
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
                string sendStr = BitConverter.ToString(msg);
                await BluetoothWriter.StoreAsync();

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
                        TopInstruction.Text = "DFU Complete! Your Arc will automatically restart, please Listen to your Arc for a double beep startup sound to indicate a successful upgrade!";
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

                ConversationList.Items.Add("Received: " + receivedStr);

                // DFU Files Sending case
                if (GaiaHandler.IsSendingFile)
                {
                    ConversationList.Items.Add("Receieved Go Ahead for DFU! Starting DFU now!");
                    ProgressStatus.Text = "Received Go Ahead for Update! Starting Update now!";
                    DFUProgressBar.IsIndeterminate = false;
                    DFUProgressBar.Value = 0;

                    int chunksRemaining = GaiaHandler.ChunksRemaining();
                    ConversationList.Items.Add("DFU Progress | Chunks Remaining: " + chunksRemaining);

                    // Loop to continually send raw bytes until we finish sending the whole file
                    while (chunksRemaining > 0)
                    {
                        byte[] msg = GaiaHandler.GetNextFileChunk();
                        BluetoothWriter.WriteBytes(msg);
                        await BluetoothWriter.StoreAsync();

                        ProgressStatus.Text = "Update in progress...";
                        DFUProgressBar.Value = 100 * (float)(GaiaHandler.TotalChunks - chunksRemaining) / (float)GaiaHandler.TotalChunks;

                        if (chunksRemaining % 1000 == 0)
                        {
                            ConversationList.Items.Add("DFU Progress | Chunks Remaining: " + chunksRemaining);
                        }
                        System.Diagnostics.Debug.WriteLine("Chunks Remaining: " + chunksRemaining);

                        SendRawBytes(GaiaHandler.GetNextFileChunk(), false);
                        chunksRemaining = GaiaHandler.ChunksRemaining();
                    }
                    ProgressStatus.Text = "Finished Sending File! Verifying... (Your Arc will restart soon after this step!)";
                    DFUProgressBar.IsIndeterminate = true;
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
