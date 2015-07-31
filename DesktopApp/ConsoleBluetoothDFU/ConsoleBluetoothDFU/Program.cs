using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using GaiaDFU;
using InTheHand;
using InTheHand.Net;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;
using InTheHand.Net.Ports;

namespace ConsoleBluetoothDFU
{
    class Program
    {
        static void Main(string[] args)
        {
            Guid GaiaServiceID = new Guid("00001107-D102-11E1-9B23-00025B00A5A5");
            BluetoothClient btc = new BluetoothClient();
            Console.WriteLine("Scanning for devices...");
            BluetoothDeviceInfo[] devs = btc.DiscoverDevices();
            BluetoothDeviceInfo arcDev = null;
            NetworkStream btStream = null;

            GaiaDfu dfuHandler = new GaiaDfu();

            for (var i = 0; i < devs.Length; i++)
            {
                Console.WriteLine(devs[i].DeviceName);
                if (devs[i].DeviceName == "Wearhaus Arc Devboard 1")
                {
                    Console.WriteLine("Found Wearhaus!");
                    arcDev = devs[i];
                }
            }

            if (arcDev != null)
            {
                var ep = new BluetoothEndPoint(arcDev.DeviceAddress, GaiaServiceID);
                if (btc.Connected == false)
                {
                    btc.Connect(ep);
                }

                btStream = btc.GetStream();

                if (btc.Connected && btStream != null)
                {
                    Console.WriteLine("Connected!");

                    Program.ReceiveStringLoop(btStream, dfuHandler);

                    Console.WriteLine("Ready To Send Commands - Enter a Command to send! Press \"q\" to exit");
                    String usrCmd = Console.ReadLine().ToLower();

                    while (usrCmd != "q")
                    {
                        ushort gaiaCmd = Convert.ToUInt16(usrCmd, 16);
                        byte[] cmdFrame = dfuHandler.CreateGaiaCommand(gaiaCmd);
                        btStream.Write(cmdFrame, 0, cmdFrame.Length);
                        Console.WriteLine("Sent on stream: " + BitConverter.ToString(cmdFrame));
                        usrCmd = Console.ReadLine().ToLower();
                    }
                }
                
            }
            else
            {
                Console.WriteLine("No Wearhaus Found! Exiting!");
            }

            


            Console.WriteLine("Press Enter to Exit!");
            Console.ReadLine();
        }

        

        public static async void ReceiveStringLoop(NetworkStream netSocket, GaiaDfu DFUHandler)
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
                    Console.WriteLine("Received: " + receivedStr );

                    switch (command)
                    {
                        case (ushort)GaiaDfu.ArcCommand.StartDfu | 0x8000:
                            //SendDFUBegin();
                            break;

                        default:
                            break;
                    }
                }
                else // otherwise, this is an actual command! We must respond to it
                {
                    receivedStr += " [Command!] ";
                    Console.WriteLine("Received: " + receivedStr );
                    switch (command)
                    {
                        case (ushort)GaiaDfu.GaiaNotification.Event:
                            if (payload[0] == 0x10 && payload[1] == 0x00)
                            {
                                receivedStr += " [Event!] ";
                                int chunksRemaining = DFUHandler.ChunksRemaining();
                                while (chunksRemaining > 0)
                                {
                                    byte[] msg = DFUHandler.GetNextFileChunk();
                                    await netSocket.WriteAsync(msg, 0, msg.Length);
                                    
                                    if (chunksRemaining % 1000 == 0)
                                    {
                                        Console.WriteLine("DFU Progress | Chunks Remaining: " + chunksRemaining);
                                    }
                                    System.Diagnostics.Debug.WriteLine("Chunks Remaining: " + chunksRemaining);

                                    //SendRawBytes(DFUHandler.GetNextFileChunk(), false);
                                    chunksRemaining = DFUHandler.ChunksRemaining();
                                }
                                // We are in the Gaia Dfu Event!
                            }
                            break;

                        case (ushort)GaiaDfu.GaiaCommand.DFURequest:
                            //SendRawBytes(DFUHandler.CreateAck(command));
                            break;

                        default:
                            //SendRawBytes(DFUHandler.CreateAck(command));
                            break;
                    }
                }


                ReceiveStringLoop(netSocket, DFUHandler);
            }
            catch (Exception ex)
            {
                /*lock (this)
                {
                    if (netSocket == null)
                    {
                        // Do not print anything here -  the user closed the socket.
                    }
                    else
                    {
                        Console.WriteLine("Read stream failed with error: " + ex.Message);
                        //Disconnect();
                    }
                }*/
            }
        }
    }
}
