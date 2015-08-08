// Copyright (c) Microsoft. All rights reserved.

using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

using GaiaDFU;
using Windows.ApplicationModel.Activation;

namespace WearhausBluetoothApp
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
#if WINDOWS_PHONE_APP
    public sealed partial class Scenario1_ChatClient : Page, IFileOpenPickerContinuable
#else
    public sealed partial class Scenario1_ChatClient : Page
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

        private StreamSocket chatSocket;
        private DataWriter chatWriter;
        private RfcommDeviceService chatService;
        private DeviceInformationCollection chatServiceInfoCollection;
        private GaiaDfu DFUHandler;
        private StorageFile dfuFile;
        private DataReader dfuReader;

        private MainPage rootPage;
        

        public Scenario1_ChatClient()
        {
            this.InitializeComponent();

            chatSocket = null;
            chatWriter = null;
            chatService = null;
            chatServiceInfoCollection = null;

            DFUHandler = null;

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

        private async void RunButton_Click(object sender, RoutedEventArgs e)
        {

            // Clear any previous messages
            MainPage.Current.NotifyUser("", NotifyType.StatusMessage);

            // Find all paired instances of the Rfcomm chat service
            chatServiceInfoCollection = await DeviceInformation.FindAllAsync(
                RfcommDeviceService.GetDeviceSelector(RfcommServiceId.FromUuid(RfcommChatServiceUuid)));

            if (chatServiceInfoCollection.Count > 0)
            {
                List<string> items = new List<string>();
                foreach (var chatServiceInfo in chatServiceInfoCollection)
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

        private async void ServiceList_Tapped(object sender, TappedRoutedEventArgs e)
        {
            try
            {
                RunButton.IsEnabled = false;
                ServiceSelector.Visibility = Windows.UI.Xaml.Visibility.Collapsed;

                var chatServiceInfo = chatServiceInfoCollection[ServiceList.SelectedIndex];


                // Potential Bug Fix?? Wrap FromIdAsync call in the UI Thread, as per the instructions of:
                // http://blogs.msdn.com/b/wsdevsol/archive/2014/11/10/why-doesn-t-the-windows-8-1-bluetooth-rfcomm-chat-sample-work.aspx
                // EDIT: Actually may not work as all this does is bring the exception thrown outside of this thread, so the exception is unhandled...

                //await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                //{
                chatService = await RfcommDeviceService.FromIdAsync(chatServiceInfo.Id);
                //});

                if (chatService == null)
                {
                    MainPage.Current.NotifyUser(
                        "Access to the device is denied because the application was not granted access",
                        NotifyType.StatusMessage);
                    return;
                }

                var attributes = await chatService.GetSdpRawAttributesAsync();
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
                ServiceName.Text = "Connected to: \"" + chatServiceInfo.Name + "\"";
                //ServiceName.Text = "Service Name: \"" + attributeReader.ReadString(serviceNameLength) + "\"";

                lock (this)
                {
                    chatSocket = new StreamSocket();
                }

                await chatSocket.ConnectAsync(chatService.ConnectionHostName, chatService.ConnectionServiceName);

                chatWriter = new DataWriter(chatSocket.OutputStream);
                ChatBox.Visibility = Windows.UI.Xaml.Visibility.Visible;

                DFUHandler = new GaiaDfu(); // Create GAIA DFU Object

                DataReader chatReader = new DataReader(chatSocket.InputStream);
                ReceiveStringLoop(chatReader);
                

            }
            catch (Exception ex)
            {
                RunButton.IsEnabled = true;
                MainPage.Current.NotifyUser("Error: " + ex.HResult.ToString() + " - " + ex.Message, 
                    NotifyType.ErrorMessage);
            }
        }

        private void Disconnect()
        {
            try
            {
                if (chatWriter != null)
                {
                    chatWriter.DetachStream();
                    chatWriter = null;
                }

                lock (this)
                {
                    if (chatSocket != null)
                    {
                        chatSocket.Dispose();
                        chatSocket = null;
                    }
                }

                RunButton.IsEnabled = true;
                ServiceSelector.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                ChatBox.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                ConversationList.Items.Clear();
            }
            catch (Exception ex)
            {
                MainPage.Current.NotifyUser("Error On Disconnect: " + ex.HResult.ToString() + " - " + ex.Message,
                   NotifyType.ErrorMessage);
            }
        }

        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            Disconnect();

            MainPage.Current.NotifyUser("Disconnected", NotifyType.StatusMessage);
        }

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
            dfuFile = await filePicker.PickSingleFileAsync();
#endif

            MainPage.Current.NotifyUser("", NotifyType.StatusMessage);
            if( dfuFile == null){
                MainPage.Current.NotifyUser("Invalid DFU File!", NotifyType.ErrorMessage);
                return;
            }

            // Get CRC first from File
            var buf = await FileIO.ReadBufferAsync(dfuFile);
            dfuReader = DataReader.FromBuffer(buf);
            uint fileSize = buf.Length;
            byte[] fileBuffer = new byte[fileSize];
            dfuReader.ReadBytes(fileBuffer);
            DFUHandler.SetFileBuffer(fileBuffer);

            MainPage.Current.NotifyUser("Picked File: " + dfuFile.Name, NotifyType.StatusMessage);

        }

        private async void SendDFUButton_Click(object sender, RoutedEventArgs e)
        {
            MainPage.Current.NotifyUser("", NotifyType.StatusMessage);
            if (dfuFile == null)
            {
                MainPage.Current.NotifyUser("No DFU File Picked. Please Pick a DFU File!", NotifyType.StatusMessage);
                return;
            }
            // Send DFU!
            SendRawBytes(DFUHandler.CreateGaiaCommand((ushort)GaiaDfu.ArcCommand.StartDfu));
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MessageTextBox.Text == "") { return; }
                ushort usrCmd = Convert.ToUInt16(MessageTextBox.Text, 16);

                byte[] msg = DFUHandler.CreateGaiaCommand(usrCmd);
                SendRawBytes(msg);
            }
            catch (Exception ex)
            {
                MainPage.Current.NotifyUser("Error: " + ex.HResult.ToString() + " - " + ex.Message,
                    NotifyType.StatusMessage);
            }
        }

        private async void SendRawBytes(byte[] msg, bool print = true)
        {
            chatWriter.WriteBytes(msg);
            string sendStr = BitConverter.ToString(msg);
            await chatWriter.StoreAsync();

            if (print)
            {
                ConversationList.Items.Add("Sent: " + sendStr);
            }
            MessageTextBox.Text = "";
        }

#if WINDOWS_PHONE_APP
        /// <summary> 
        /// Handle the returned files from file picker 
        /// This method is triggered by ContinuationManager based on ActivationKind 
        /// </summary> 
        /// <param name="args">File open picker continuation activation argment. It cantains the list of files user selected with file open picker </param> 
        public void ContinueFileOpenPicker(FileOpenPickerContinuationEventArgs args)
        {
            if (args.Files.Count > 0)
            {
                dfuFile = args.Files[0];
                MainPage.Current.NotifyUser("Picked File: " + dfuFile.Name, NotifyType.StatusMessage);
            }
            else
            {
                dfuFile = null;
                MainPage.Current.NotifyUser("Not a Valid File / No File Chosen!", NotifyType.StatusMessage);
            }
        }
#endif


        private async void ReceiveStringLoop(DataReader chatReader)
        {
            try
            {
                byte frameLen = GaiaDfu.GAIA_FRAME_LEN;

                // Frame is always FRAME_LEN long at least, so load that many bytes and process the frame
                uint size = await chatReader.LoadAsync(frameLen);

                // Buffer / Stream is closed 
                if (size < frameLen)
                {
                    MainPage.Current.NotifyUser("Disconnected from Stream!", NotifyType.ErrorMessage);
                    Disconnect();
                    return;
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

                byte[] resp;
                // If we get 0x01 in the Flags, we received a CRC (also we should check it probably)
                if (receivedFrame[2] == GaiaDfu.GAIA_FLAG_CHECK)
                {
                    await chatReader.LoadAsync(sizeof(byte));
                    byte checksum = chatReader.ReadByte();
                    receivedStr += " CRC: " + checksum.ToString("X2");

                    // Now we should have all the bytes, lets process the whole thing!
                    resp = DFUHandler.CreateResponseToMessage(receivedFrame, payload, checksum);
                }
                else
                {
                    // Now we should have all the bytes, lets process the whole thing!
                    resp = DFUHandler.CreateResponseToMessage(receivedFrame, payload);
                }

                ConversationList.Items.Add("Received: " + receivedStr);

                if (DFUHandler.IsSendingFile)
                {
                    ConversationList.Items.Add("Receieved Go Ahead for DFU! Starting DFU now!");
                    int chunksRemaining = DFUHandler.ChunksRemaining();
                    ConversationList.Items.Add("DFU Progress | Chunks Remaining: " + chunksRemaining);
                    while (chunksRemaining > 0)
                    {
                        byte[] msg = DFUHandler.GetNextFileChunk();
                        chatWriter.WriteBytes(msg);
                        await chatWriter.StoreAsync();

                        if (chunksRemaining % 1000 == 0)
                        {
                            ConversationList.Items.Add("DFU Progress | Chunks Remaining: " + chunksRemaining);
                        }
                        System.Diagnostics.Debug.WriteLine("Chunks Remaining: " + chunksRemaining);

                        SendRawBytes(DFUHandler.GetNextFileChunk(), false);
                        chunksRemaining = DFUHandler.ChunksRemaining();
                    }
                    ConversationList.Items.Add("Finished Sending DFU! Verifying...");
                    DFUHandler.IsSendingFile = false;

                }

                if (resp != null) SendRawBytes(resp);

                ReceiveStringLoop(chatReader);
            }
            catch (Exception ex)
            {
                lock (this)
                {
                    if (chatSocket == null)
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

    }
}