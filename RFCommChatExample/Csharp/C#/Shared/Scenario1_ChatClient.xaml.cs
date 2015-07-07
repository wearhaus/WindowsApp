// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
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

namespace BluetoothRfcommChat
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class Scenario1_ChatClient : Page
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


        // GAIA FRAMING PARAMS
        private const byte GAIA_FRAME_START = 0xff;
        private const byte GAIA_PROTOCOL_VER = 0x01;
        private const byte GAIA_FLAG_CHECK = 0x01;
        private const ushort GAIA_CSR_VENDOR_ID = 0x000a;

        private MainPage rootPage;

        public Scenario1_ChatClient()
        {
            this.InitializeComponent();

            chatSocket = null;
            chatWriter = null;
            chatService = null;
            chatServiceInfoCollection = null;

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
                ServiceName.Text = "Service Name: \"" + attributeReader.ReadString(serviceNameLength) + "\"";

                lock (this)
                {
                    chatSocket = new StreamSocket();
                }

                await chatSocket.ConnectAsync(chatService.ConnectionHostName, chatService.ConnectionServiceName);

                chatWriter = new DataWriter(chatSocket.OutputStream);
                ChatBox.Visibility = Windows.UI.Xaml.Visibility.Visible;

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

        private async void ReceiveStringLoop(DataReader chatReader)
        {
            try
            {
                /*
                uint size = await chatReader.LoadAsync(sizeof(uint));
                if (size < sizeof(uint))
                {
                    // The underlying socket was closed before we were able to read the whole data
                    return;
                }

                uint stringLength = chatReader.ReadUInt32();
                uint actualStringLength = await chatReader.LoadAsync(stringLength);
                if (actualStringLength != stringLength)
                {
                    // The underlying socket was closed before we were able to read the whole data
                    return;
                }
                */

                //ConversationList.Items.Add("Received: \"" + chatReader.ReadString(stringLength) + "\"");

                uint frameLen = 8;

                uint size = await chatReader.LoadAsync(frameLen);
                if (size < frameLen)
                {
                    return;
                }
                string receivedStr = "";
                byte[] receivedFrame = new byte[frameLen];
                chatReader.ReadBytes(receivedFrame);
                receivedStr += BitConverter.ToString(receivedFrame);
                byte payloadLen = receivedFrame[3];

                if (payloadLen > 0)
                {
                    byte[] payload = new byte[payloadLen];
                    await chatReader.LoadAsync(payloadLen);
                    chatReader.ReadBytes(payload);
                    receivedStr += " Payload: " + BitConverter.ToString(payload); 
                }

                //await chatReader.LoadAsync(sizeof(byte));
                //byte checksum = chatReader.ReadByte();


                ConversationList.Items.Add("Received: " + receivedStr );

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

        private void Disconnect()
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

        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            Disconnect();

            MainPage.Current.NotifyUser("Disconnected", NotifyType.StatusMessage);
        }

        private byte Checksum(byte[] b)
        {
            byte checkSum = b[0];
            for (int i = 1; i < b.Length; i++)
            {
                checkSum ^= b[i];
            }
            return checkSum;
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                /*chatWriter.WriteUInt32((uint)MessageTextBox.Text.Length);
                chatWriter.WriteString(MessageTextBox.Text);
                await chatWriter.StoreAsync();
                ConversationList.Items.Add("Sent: " + MessageTextBox.Text);*/
                if (MessageTextBox.Text == "") { return; }
                ushort usrCmd = Convert.ToUInt16(MessageTextBox.Text, 16);

                byte[] s = { GAIA_FRAME_START, GAIA_PROTOCOL_VER, 0x00, 0x00, GAIA_CSR_VENDOR_ID >> 8, GAIA_CSR_VENDOR_ID & 0xff, (byte)(usrCmd >> 8), (byte)(usrCmd & 0xff) };
                chatWriter.WriteBytes(s);
                byte checkSum = Checksum(s);
                chatWriter.WriteByte(checkSum);
                await chatWriter.StoreAsync();

                ConversationList.Items.Add("Sent: " + BitConverter.ToString(s) + " Checksum: " + checkSum.ToString("X2"));
                MessageTextBox.Text = "";
            }
            catch (Exception ex)
            {
                MainPage.Current.NotifyUser("Error: " + ex.HResult.ToString() + " - " + ex.Message,
                    NotifyType.StatusMessage);
            }
        }

        private enum GaiaCommand : ushort
        {
            NoOp = 0x0700,
            GetAppVersion = 0x0304,
            GetRssi = 0x0301,
            SetLED = 0x0101,
            GetLED = 0x0181,
            SetTone = 0x0102,
            GetTone = 0x0182,
            SetDefaultVolume = 0x0103,
            GetDefaultVolume = 0x0183,
            ChangeVolume = 0x0201,
            ToggleBassBoost = 0x0218,
            Toggle3DEnhancement = 0x0219,
            SetLEDControl = 0x0207,
            GetLEDControl = 0x0287,
            DeviceReset = 0x0202,
            PowerOff = 0x0204,
            GetBattery = 0x0302,
            GetModuleID = 0x0303,
            GetAppVer = 0x0304,
            DFURequest = 0x0630,
            DFUBegin = 0x0631
        }
    }
}
