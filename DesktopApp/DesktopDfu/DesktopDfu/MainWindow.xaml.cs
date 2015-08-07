using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Net.Sockets;
using GaiaDFU;
using InTheHand;
using InTheHand.Net;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;
using InTheHand.Net.Ports;

namespace DesktopDfu
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        Guid GaiaServiceID;
        BluetoothClient BluetoothClient;
        BluetoothDeviceInfo[] Devices;
        BluetoothDeviceInfo ArcDev;
        NetworkStream BluetoothStream;

        GaiaDfu DFUHandler;
        
        public MainWindow()
        {
            InitializeComponent();
            GaiaServiceID = new Guid("00001107-D102-11E1-9B23-00025B00A5A5");
            ArcDev = null;
            BluetoothStream = null;
            DFUHandler = new GaiaDfu();
        }

        private async void RunButton_Click(object sender, RoutedEventArgs e)
        { 
            BluetoothClient = new BluetoothClient();
            Devices = BluetoothClient.DiscoverDevices();
            for (var i = 0; i < Devices.Length; i++)
            {
                Console.WriteLine(Devices[i].DeviceName);
                if (Devices[i].DeviceName == "Wearhaus Arc Devboard 1")
                {
                    Console.WriteLine("Found Wearhaus!");
                }

                ListBoxItem itm = new ListBoxItem();
                itm.Content = Devices[i].DeviceName;
                ServiceList.Items.Add(itm);
            }
            ServiceSelector.Visibility = System.Windows.Visibility.Visible;
        }

        private async void ServiceList_Tapped(object sender, MouseButtonEventArgs e)
        {
            try
            {
                RunButton.IsEnabled = false;
                ServiceSelector.Visibility = System.Windows.Visibility.Collapsed;

                ArcDev = Devices[ServiceList.SelectedIndex];

                if (ArcDev != null)
                {
                    var ep = new BluetoothEndPoint(ArcDev.DeviceAddress, GaiaServiceID);

                    if (BluetoothClient.Connected == false)
                    {
                        lock (this)
                        {
                            BluetoothClient.Connect(ep);
                            BluetoothStream = BluetoothClient.GetStream();
                        }
                    }

                    if (BluetoothClient.Connected && BluetoothStream != null)
                    {
                        Console.WriteLine("Connected!");
                        ChatBox.Visibility = System.Windows.Visibility.Visible;
                        ReceiveStringLoop(BluetoothStream);
                    }
                }
                else
                {
                    Console.WriteLine("No Wearhaus Found! Exiting!");
                }
                
            }
            catch (Exception ex)
            {
                ServiceList.Items.Clear();
                ConversationList.Items.Clear();
                RunButton.IsEnabled = true;
            }
        }

        private void Disconnect()
        {
            try
            {
                if (BluetoothStream != null)
                {
                    System.Diagnostics.Debug.WriteLine("DISCONNECTING AND CLOSING THE STREAM!");
                    BluetoothStream.Dispose();
                    BluetoothStream = null;
                }

                if (BluetoothClient != null)
                {
                    System.Diagnostics.Debug.WriteLine("DISCONNECTING BLUETOOTH CLIENT!");
                    BluetoothClient.Dispose();
                    BluetoothClient = null;
                }

                RunButton.IsEnabled = true;
                ServiceSelector.Visibility = System.Windows.Visibility.Collapsed;
                ChatBox.Visibility = System.Windows.Visibility.Collapsed;
                ServiceList.Items.Clear();
                ConversationList.Items.Clear();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error on Disconnect: " + ex.HResult.ToString() + " - " + ex.Message);
            }
        }

        private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            Disconnect();
        }

        private async void PickFileButton_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("TRYING TO PICK A FILE!!");
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();

            dlg.DefaultExt = ".dfu";
            var result = dlg.ShowDialog();

            if (result == true)
            {
                byte[] fileBuffer = File.ReadAllBytes(dlg.FileName);
                DFUHandler.SetFileBuffer(fileBuffer);
                System.Diagnostics.Debug.WriteLine("Picked File: " + dlg.FileName);
            }
        }

        private async void SendDFUButton_Click(object sender, RoutedEventArgs e)
        {
            //if (DfuHandler.DfuFileName == null)
            //{
            //    System.Diagnostics.Debug.WriteLine("No DFU File Picked. Please Pick a DFU File!");
            //    return;
            //}
            // Send DFU!
            SendRawBytes(DFUHandler.CreateGaiaCommand((ushort)GaiaDfu.ArcCommand.StartDfu));
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
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
                System.Diagnostics.Debug.WriteLine("Error: " + ex.HResult.ToString() + " - " + ex.Message);
            }
        }

        private async void SendRawBytes(byte[] msg, bool print = true)
        {
            BluetoothStream.Write(msg, 0, msg.Length);
            string sendStr = BitConverter.ToString(msg);

            if (print)
            {
                ConversationList.Items.Add("Sent: " + sendStr);
            }
            MessageTextBox.Text = "";
        }
        
        private async void ReceiveStringLoop(NetworkStream netSocket)
        {
            try
            {
                byte frameLen = GaiaDfu.GAIA_FRAME_LEN;
                string receivedStr = "";
                byte[] receivedFrame = new byte[frameLen];

                // Frame is always FRAME_LEN long at least, so load that many bytes and process the frame
                int size = await netSocket.ReadAsync(receivedFrame, 0, frameLen);

                // Buffer / Stream is closed 
                if (size < frameLen)
                {
                    Console.WriteLine("Disconnected from Stream!");
                    return;
                }

                receivedStr += BitConverter.ToString(receivedFrame);
                byte payloadLen = receivedFrame[3];

                byte[] payload = new byte[payloadLen];
                if (payloadLen > 0)
                {
                    int receivedPayloadLen = await netSocket.ReadAsync(payload, 0, payloadLen);
                    receivedStr += " Payload: " + BitConverter.ToString(payload);
                }
                
                byte[] resp;
                // If we get 0x01 in the Flags, we received a CRC (also we should check it probably)
                if (receivedFrame[2] == GaiaDfu.GAIA_FLAG_CHECK)
                {
                    byte checksum = (byte)netSocket.ReadByte();
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
                        await netSocket.WriteAsync(msg, 0, msg.Length);

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
                
                if(resp != null) SendRawBytes(resp);

                ReceiveStringLoop(netSocket);
            }
            catch (Exception ex)
            {
                lock (this)
                {
                    if (netSocket == null)
                    {
                        // Do not print anything here -  the user closed the socket.
                    }
                    else
                    {
                        Console.WriteLine("Read stream failed with error: " + ex.Message);
                        Disconnect();
                    }
                }
            }
        }

   


    }
}
