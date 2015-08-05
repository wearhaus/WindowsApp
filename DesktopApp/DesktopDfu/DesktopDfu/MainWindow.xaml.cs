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

        GaiaDfu DfuHandler;
        String dfuFileName;
        
        public MainWindow()
        {
            InitializeComponent();
            GaiaServiceID = new Guid("00001107-D102-11E1-9B23-00025B00A5A5");
            ArcDev = null;
            BluetoothStream = null;
            DfuHandler = new GaiaDfu();
            dfuFileName = null;
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
                        ReceiveStringLoop(BluetoothStream, DfuHandler);
                    }
                }
                else
                {
                    Console.WriteLine("No Wearhaus Found! Exiting!");
                }
                
            }
            catch (Exception ex)
            {
                RunButton.IsEnabled = true;
            }
        }

        private void Disconnect()
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
                dfuFileName = dlg.FileName;
                System.Diagnostics.Debug.WriteLine("Picked File: " + dlg.FileName);
            }
        }

        private async void SendDFUButton_Click(object sender, RoutedEventArgs e)
        {
            if (dfuFileName == null)
            {
                System.Diagnostics.Debug.WriteLine("No DFU File Picked. Please Pick a DFU File!");
                return;
            }
            // Send DFU!
            SendRawBytes(DfuHandler.CreateGaiaCommand((ushort)GaiaDfu.ArcCommand.StartDfu));
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MessageTextBox.Text == "") { return; }
                ushort usrCmd = Convert.ToUInt16(MessageTextBox.Text, 16);

                byte[] msg = DfuHandler.CreateGaiaCommand(usrCmd);
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
        
        private async void ReceiveStringLoop(NetworkStream netSocket, GaiaDfu DFUHandler)
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
                    //resp = DFUHandler.ProcessReceievedMessage(receivedFrame, payload, checksum);
                }
                else
                {
                    // Now we should have all the bytes, lets process the whole thing!
                    //resp = DFUHandler.ProcessReceievedMessage(receivedFrame, payload);
                }


                // Check if the Response is a command or an ACK
                byte commandUpperByte = receivedFrame[6];
                ushort command = GaiaDfu.CombineBytes(receivedFrame[6], receivedFrame[7]);

                if (commandUpperByte >> 4 == ((DFUHandler.LastSentCommand >> 12) | 0x8)) // ACK is always the command id (16 bits) masked with 0x8000 so upper byte must start with 0x8_
                {
                    receivedStr += " [ACK!] ";
                    ConversationList.Items.Add("Received: " + receivedStr);

                    switch (command)
                    {
                        case (ushort)GaiaDfu.ArcCommand.StartDfu | 0x8000:
                            SendDFUBegin();
                            break;

                        default:
                            break;
                    }
                }
                else // otherwise, this is an actual command! We must respond to it
                {
                    receivedStr += " [Command!]";
                    switch (command)
                    {
                        case (ushort)GaiaDfu.GaiaNotification.Event:
                            if (payload[0] == 0x10 && payload[1] == 0x00)
                            {
                                receivedStr += " [Event!] ";
                                ConversationList.Items.Add("Received: " + receivedStr);
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
                                // We are in the Gaia Dfu Event!
                            }
                            break;

                        case (ushort)GaiaDfu.GaiaCommand.DFURequest:
                            receivedStr += " [DFU Request! ]";
                            ConversationList.Items.Add("Received: " + receivedStr);
                            SendRawBytes(DFUHandler.CreateAck(command));
                            break;

                        default:
                            ConversationList.Items.Add("Received: " + receivedStr);
                            SendRawBytes(DFUHandler.CreateAck(command));
                            break;
                    }
                }


                ReceiveStringLoop(netSocket, DFUHandler);
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

        private async void SendDFUBegin()
        {
            if (dfuFileName == null)
            {
                System.Diagnostics.Debug.WriteLine("No DFU File Picked. Please Pick a DFU File!");
                return;
            }

            // Get CRC first from File
            byte[] fileBuffer = File.ReadAllBytes(dfuFileName);
            uint fileSize = (uint)fileBuffer.Length;
            DfuHandler.SetFileBuffer(fileBuffer);
            byte[] crcBuffer = new byte[fileSize + 4];
            System.Buffer.BlockCopy(fileBuffer, 0, crcBuffer, 4, (int)fileSize);
            crcBuffer[0] = crcBuffer[1] = crcBuffer[2] = crcBuffer[3] = (byte)0xff;

            long crc = DfuCRC.fileCrc(crcBuffer);

            // Send DfuBegin with CRC and fileSize
            SendRawBytes(DfuHandler.CreateDfuBegin(crc, fileSize));
        }



    }
}
