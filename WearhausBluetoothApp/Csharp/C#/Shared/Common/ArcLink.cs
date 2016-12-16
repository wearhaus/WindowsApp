﻿using Gaia;
using System;
using System.Collections.Generic;
using System.Text;
using WearhausServer;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Common
{
    public class ArcLink
    {
        // ArcConnectionState abbreviation
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
            Success,

            // TODO have an int side-by-side that describes the DFU error. A DFU error requires a power cycle of arc to
            // attempt another dfu
            Error,
        };

        // Send to server the Enum number, not the string
        public enum DFUResultStatus
        {
            Success,
            Aborted,
            IOException,
            VerifyFailed,
            OtherFailure,
            DownloadFailed,
            FvMismatch,
            DisconnectedDuring,
            TimeoutDfuState,
            DfuRequestBadAck,
            CantStartWeirdOldFV,
            TimeoutFinalizing,
            // a placeholder meaning no dfu result status at all
            None,
        };


        public ArcLink()
        {
            MyHID = null;
            MyFv_full_code = null;

            MyArcConnState = ArcConnState.NoArc;
            MyDFUStep = DFUStep.None;
            MyErrorHuman = "";
            MyDeviceHumanName = "";

            BluetoothSocket = null;
            BluetoothWriter = null;
            BluetoothService = null;
            BluetoothServiceInfoCollection = null;
            GaiaHelper = null;
        }




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
        private GaiaHelper GaiaHelper;

        private StorageFile MyDfuFile;
        // may be null when choosedfufile
        private Firmware MyDFUTargetFV;
        // may be null when choosedfufile
        private Firmware MyDFUOldFv;
        // This is just for a backup, in case we are updating from a weird version; we still want to keep a record around
        private String MyDFUOldFv_full_code;
        private DataReader MyDfuReader;


        public ArcConnState MyArcConnState { get; private set; }
        public DFUStep MyDFUStep { get; private set; }
        // only useful to read when DFU is in Error state.
        public DFUResultStatus MyDFUResultStatus { get; private set; }
        // Refer to this field when ConnState is Error for useful error messages for the user
        public String MyErrorHuman { get; private set; }
        public String MyDeviceHumanName { get; private set; }
        public string MyHID { get; private set; }
        public string MyFv_full_code { get; private set; }
        // null value means unknown version that was not on our table
        public Firmware MyFirmwareVersion { get; private set; }
        // int from 0 to 15, To translate to generation, add 1. 
        // So pid = 0 results in 1st Gen Arc. pid = 1 is 2nd Gen arc
        public int MyProductId { get; private set; }
        public String GetArcGeneration()
        {
            return "" + (MyProductId + 1);
        }




        // subscribe/unsubscribe with myArcLink.ArcConnStateChanged += myListenerMethod; 
        // static void myListenerMethod(object sender, EventArgs e) {}
        // myListenerMethod should read MyArcConnState and ErrorHuman and updateUI or logic
        public event EventHandler ArcConnStateChanged;

        protected virtual void onArcConnStateChanged()
        {
            System.Diagnostics.Debug.WriteLine("ArcConnState has changed: " + MyArcConnState + ", " + MyErrorHuman + ", " + MyDeviceHumanName);
            ArcConnStateChanged?.Invoke(this, null);
        }

        public event EventHandler DFUStepChanged;

        protected virtual void onDFUStepChanged()
        {
            System.Diagnostics.Debug.WriteLine("onDFUStepChanged has changed: " + MyDFUStep + ", " + MyErrorHuman + ", " + MyDeviceHumanName);
            DFUStepChanged?.Invoke(this, null);
        }


        // TODO, finish this as use it to populate a debugger menu that doesn't require visual studio to be open;
        public event EventHandler DebugGaiaMessagePosted;




        // must be void, so caller does not wait for this slow method which may take many seconds
        // instead, we should have the UI listen and react to ArcConnState
        public async void ConnectArc()
        {
            if (MyArcConnState != ArcConnState.NoArc && MyArcConnState != ArcConnState.Error)
            {
                System.Diagnostics.Debug.WriteLine("ConnectArc() called when Arc already connected");
                return;
            }

            MyArcConnState = ArcConnState.TryingToConnect;
            MyErrorHuman = "";
            MyDeviceHumanName = "";
            onArcConnStateChanged();
            

            // Find all paired instances of the Rfcomm chat service
            BluetoothServiceInfoCollection = await DeviceInformation.FindAllAsync(
                RfcommDeviceService.GetDeviceSelector(RfcommServiceId.FromUuid(RfcommGAIAServiceUuid)));

            DeviceInformation bluetoothServiceInfo = null;


            if (BluetoothServiceInfoCollection.Count > 0)
            {

                //List<string> items = new List<string>();
                foreach (var chatServiceInfo in BluetoothServiceInfoCollection)
                {
                    //items.Add(chatServiceInfo.Name); //Added to print services!

                    string hid = ArcUtil.ParseHID(chatServiceInfo.Id);
                    System.Diagnostics.Debug.WriteLine("ParseHID result: " + hid);

                    if (hid.StartsWith("1CF03E") || hid.StartsWith("00025B"))
                    {
                        bluetoothServiceInfo = chatServiceInfo;
                    }
                }
                //cvs.Source = items;  cvs is a UI debug thing

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
                            MyArcConnState = ArcConnState.Error;
                            MyDeviceHumanName = "";
                            MyErrorHuman = "This app needs permission to connect to your Wearhaus Arc. Try reconnecting to your headphone and clicking \"Yes\" to any prompts for permission.";
                            onArcConnStateChanged();
                            return;
                        }

                        var attributes = await BluetoothService.GetSdpRawAttributesAsync();
                        /*if (!attributes.ContainsKey(SdpServiceNameAttributeId))
                        {
                            MainPage.Current.NotifyUser(
                                "The Chat service is not advertising the Service Name attribute (attribute id=0x100). " +
                                "Please verify that you are running the BluetoothRfcommChat server.",
                                NotifyType.ErrorMessage);
                            return;
                        }*/

                        var attributeReader = DataReader.FromBuffer(attributes[SdpServiceNameAttributeId]);
                        var attributeType = attributeReader.ReadByte();
                        /*if (attributeType != SdpServiceNameAttributeType)
                        {
                            MainPage.Current.NotifyUser(
                                "The Chat service is using an unexpected format for the Service Name attribute. " +
                                "Please verify that you are running the BluetoothRfcommChat server.",
                                NotifyType.ErrorMessage);
                            return;
                        }*/

                        var serviceNameLength = attributeReader.ReadByte();

                        // The Service Name attribute requires UTF-8 encoding.
                        attributeReader.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;

                        lock (this)
                        {
                            BluetoothSocket = new StreamSocket();
                        }

                        await BluetoothSocket.ConnectAsync(BluetoothService.ConnectionHostName, BluetoothService.ConnectionServiceName);

                        BluetoothWriter = new DataWriter(BluetoothSocket.OutputStream);

                        GaiaHelper = new GaiaHelper(this); // Create GAIA DFU Helper Object

                        // internet code to be moved outside ArcLink
                        //HttpController = new WearhausHttpController(bluetoothServiceInfo.Id); // Create HttpController object
                        //string guestResult = await HttpController.CreateGuest(); // Create a Guest account for basic information to the server
                        //System.Diagnostics.Debug.WriteLine(guestResult);

                        DataReader chatReader = new DataReader(BluetoothSocket.InputStream);
                        ReceiveStringLoop(chatReader);

                        GaiaMessage firmware_version_request = new GaiaMessage((ushort)GaiaMessage.GaiaCommand.GetAppVersion);
                        // Send a get app version Gaia req to the device to see what the firmware version is (for DFU)
                        SendRawBytes(firmware_version_request.BytesSrc);


                        MyArcConnState = ArcConnState.GatheringInfo;
                        MyHID = ArcUtil.ParseHID(bluetoothServiceInfo.Id);
                        String productIdChar = MyHID.Substring(6, 1);
                        MyProductId = Convert.ToInt32(productIdChar, 16);
                        System.Diagnostics.Debug.WriteLine("Connected Arc, now gathering.  HID: " + MyHID + ",  ProductId: " + MyProductId);

                        MyErrorHuman = "";
                        MyDeviceHumanName = bluetoothServiceInfo.Name;
                        onArcConnStateChanged();

                    }
                    catch (Exception ex)
                    {
                        /*if (ex.Message.ToLower().Contains("json"))
                        {
                            TopInstruction.Text = "There was an error connecting to your Arc. Could not reach the server. Your internet may be disconnected or the server is down.";
                            System.Diagnostics.Debug.WriteLine("Error: " + ex.HResult.ToString() + " - " + ex.Message);
                        }
                        else */
                        if (ex.Message.ToLower().Contains("datagram socket"))
                        {
                            MyErrorHuman = "Sorry this app does not run on Windows 8. Please consider upgrading to Windows 10 or visit wearhaus.com/updater for other options on Android or OSX.";
                            System.Diagnostics.Debug.WriteLine("Error: " + ex.ToString());
                        }
                        else
                        {
                            MyErrorHuman = "Couldn't connect to your Arc. Please make sure your Arc is powered on and connected in Bluetooth Settings. Or try turning your Arc off and then on again.";
                            System.Diagnostics.Debug.WriteLine("Error: " + ex.ToString());
                        }
                        Disconnect(MyErrorHuman);
                    }
                }
                else
                {
                    MyArcConnState = ArcConnState.Error;
                    MyErrorHuman = "No Wearhaus Arc found. Please double check you are connected to a Wearhaus Arc in Windows Bluetooth Settings and try again.";
                    MyDeviceHumanName = "";
                    onArcConnStateChanged();
                }
            }
            else
            {
                MyArcConnState = ArcConnState.Error;
                MyErrorHuman = "No Wearhaus Arc found. Please double check you are connected to a Wearhaus Arc in Windows Bluetooth Settings and try again.";
                MyDeviceHumanName = "";
                onArcConnStateChanged();
            }



        }


        /// <summary>
        /// Checks if all necessary fields have been initialized from the Arc, such as FV_full_code
        /// </summary>
        private void CheckGathering()
        {
            System.Diagnostics.Debug.WriteLine("CheckGathering()");

            if (MyArcConnState == ArcConnState.GatheringInfo)
            {
                if (MyFv_full_code != null && MyFv_full_code.Length == ArcUtil.FV_Full_code_length)
                {
                    System.Diagnostics.Debug.WriteLine("   done gathering arc info");

                    MyArcConnState = ArcConnState.Connected;
                    MyErrorHuman = "";
                    onArcConnStateChanged();

                }

            }

        }



        /// <summary>
        /// Method to clean up socket resources and disconnect from the bluetooth device
        /// Changes ArcConnState
        /// </summary>
        public void Disconnect(String errorHuman)
        {
            System.Diagnostics.Debug.WriteLine("ArcLink.Disconnect(" + errorHuman + ");");

            try
            {
                // disconnect that's part of DFU should have a flag set here
                if (GaiaHelper != null && GaiaHelper.IsWaitingForVerification)
                {

                    MyDFUStep = DFUStep.AwaitingManualPowerCycle;
                    MyDFUResultStatus = DFUResultStatus.None;
                    onDFUStepChanged();

                    // TODO set DFU flag for waiting it to reconnect
                    //RunButton.IsEnabled = false;
                    //RunButton.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                    //VerifyDfuButton.IsEnabled = true;
                    //VerifyDfuButton.Visibility = Windows.UI.Xaml.Visibility.Visible;
                }
                else
                {
                    // normal disconnect not part of normal DFU
                    //RunButton.IsEnabled = true;
                    //RunButton.Visibility = Windows.UI.Xaml.Visibility.Visible;
                }
                
                if (GaiaHelper != null) GaiaHelper.IsSendingFile = false;

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
                if (errorHuman != null && errorHuman.Length > 0)
                {
                    // let UI display useful error to human as to why their arc disconnected / had a fatal error
                    MyArcConnState = ArcConnState.Error;
                    MyErrorHuman = errorHuman;
                } else
                {
                    MyArcConnState = ArcConnState.NoArc;
                    MyErrorHuman = "";
                }
                MyDeviceHumanName = "";
                onArcConnStateChanged();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error On Disconnect: " + ex.HResult.ToString() + " - " + ex.Message);
                return;
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

                /*if (print)
                {
                    ConversationList.Items.Add("Sent: " + sendStr);
                }
                MessageTextBox.Text = "";*/
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error SendRawBytes: " + ex.HResult.ToString() + " - " + ex.Message);

                Disconnect("Error with Connecting to Arc");
            }
        }




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
                    System.Diagnostics.Debug.WriteLine("size < frameLen");
                    if (GaiaHelper.IsSendingFile)
                    {
                        System.Diagnostics.Debug.WriteLine("GaiaHelper.IsWaitingForVerification = true;");

                        //TopInstruction.Text = "Your Arc will automatically restart - please listen to your Arc for a double beep sound to indicate a restart. When you hear the beep or have waited 30 seconds, please press the \"Verify Update\" button to verify that the update worked.";
                        GaiaHelper.IsSendingFile = false;
                        GaiaHelper.IsWaitingForVerification = true;
                        Disconnect(null);

                        MyDFUStep = DFUStep.ChipPowerCycle;
                        MyDFUResultStatus = DFUResultStatus.None;
                        onDFUStepChanged();
                        return;
                    }
                    else
                    {
                        //MainPage.Current.NotifyUser("Disconnected from Stream!", NotifyType.ErrorMessage);
                        Disconnect("Arc Disconnected");
                        // TODO better error message
                        return;
                    }
                }
                System.Diagnostics.Debug.WriteLine("size >= frameLen");


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
                    resp = GaiaHelper.CreateResponseToMessage(receivedMessage, checksum);
                }
                else
                {
                    // Now we should have all the bytes, lets process the whole thing!
                    resp = GaiaHelper.CreateResponseToMessage(receivedMessage);
                }

                if (resp != null && resp.InfoMessage != null)
                {
                    receivedStr += resp.InfoMessage;
                }

                if (receivedMessage.CommandId == (ushort)GaiaMessage.GaiaCommand.GetAppVersion) // Specifically handling the case to find out the current Firmware version
                {
                    string firmware_ver = ArcUtil.ParseFirmwareVersion(receivedMessage.PayloadSrc);


                    if (GaiaHelper.IsWaitingForVerification)
                    {
                        // very end of dfu, now we check if it did update properly
                        if (MyDFUTargetFV == null)
                        {
                            // TODO
                            System.Diagnostics.Debug.WriteLine("DFU done, please check the new fv string: " + firmware_ver);
                            // debug mode, programmer will check string manually

                            MyDFUStep = DFUStep.Success;
                            MyDFUResultStatus = DFUResultStatus.Success;
                        }
                        else
                        {
                            if (MyDFUTargetFV.fullCode.Equals(firmware_ver))
                            {
                                MyDFUStep = DFUStep.Success;
                                MyDFUResultStatus = DFUResultStatus.Success;

                                //TopInstruction.Text = "Success! Your firmware update was successfully verified. Thank you for updating your Wearhaus Arc!";
                                //ConnectionProgress.IsIndeterminate = false;
                                //ConnectionProgress.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                                //ConnectionStatus.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                                //VerifyDfuButton.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                                //RunButton.Visibility = Windows.UI.Xaml.Visibility.Collapsed;

                                // TODO string response = await HttpController.DfuReport(0);
                            }
                            else
                            {
                                MyDFUStep = DFUStep.Error;
                                MyDFUResultStatus = DFUResultStatus.FvMismatch;
                                //TopInstruction.Text = "Firmware Update Failed: The firmware version attempted does not match the current firmware version post update. Try again, and if this error persists, contact customer support at support@wearhaus.com. Error 6";
                                //ConnectionProgress.IsIndeterminate = false;
                                //ConnectionProgress.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                                //ConnectionStatus.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                                //VerifyDfuButton.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                                //RunButton.Visibility = Windows.UI.Xaml.Visibility.Collapsed;

                                // TODO string response = await HttpController.DfuReport(6);
                            }
                        }
                        GaiaHelper.IsWaitingForVerification = false;
                        onDFUStepChanged();
                    }


                    MyFv_full_code = firmware_ver;
                    if (Firmware.FirmwareTable.ContainsKey(ArcUtil.GetUniqueCodeFromFull(MyFv_full_code)))
                    {
                        MyFirmwareVersion = Firmware.FirmwareTable[ArcUtil.GetUniqueCodeFromFull(MyFv_full_code)];
                    } else
                    {
                        MyFirmwareVersion = null;
                    }

                    CheckGathering();
                    
                }

                System.Diagnostics.Debug.WriteLine("Received: " + receivedStr);


                if (resp != null && resp.IsError)
                {
                    //ProgressStatus.Text = resp.InfoMessage;
                    GaiaHelper.IsSendingFile = false;
                    //DfuProgressBar.IsIndeterminate = false;

                    if (resp.MyDFUResultStatus != 0)
                    {
                        // TODO uncomment and figure out why we DFU report here
                        //string response = await HttpController.DfuReport(resp.DfuStatus);
                    }
                }

                // DFU Files Sending case
                if (GaiaHelper.IsSendingFile)
                {
                    // TODO move UI
                    //DfuProgressBar.IsIndeterminate = false;
                    //DFUProgressBar.Value = 0;
                    


                    int chunksRemaining = GaiaHelper.ChunksRemaining();
                    if (chunksRemaining > 0)
                    {
                        MyDFUStep = DFUStep.UploadingFW;
                        MyDFUResultStatus = DFUResultStatus.None;
                        onDFUStepChanged();
                    }

                    System.Diagnostics.Debug.WriteLine("DFU Progress | Chunks Remaining: " + chunksRemaining);


                    // Loop to continually send raw bytes until we finish sending the whole file
                    while (chunksRemaining > 0)
                    {
                        byte[] msg = GaiaHelper.GetNextFileChunk();

                        // Strange Thread Async bug: We must use these two lines instead of a call to SendRawBytes()
                        // in order to actually allow the DFUProgressBar to update
                        BluetoothWriter.WriteBytes(msg);
                        await BluetoothWriter.StoreAsync();
                        //SendRawBytes(msg, false);
                        chunksRemaining = GaiaHelper.ChunksRemaining();

                        //ProgressStatus.Text = "Update in progress...";
                        //DfuProgressBar.Value = 100 * (float)(GaiaHelper.TotalChunks - chunksRemaining) / (float)GaiaHelper.TotalChunks;

                        if (chunksRemaining % 1000 == 0)
                        {
                            System.Diagnostics.Debug.WriteLine("DFU Progress | Chunks Remaining: " + chunksRemaining);
                            //ConversationList.Items.Add("DFU Progress | Chunks Remaining: " + chunksRemaining);
                        }
                        //System.Diagnostics.Debug.WriteLine("Chunks Remaining: " + chunksRemaining);

                    }
                    //ProgressStatus.Text = "Finished sending file. Verifying... (Your Arc will restart soon after this step.)";
                    //DfuProgressBar.IsIndeterminate = true;
                    System.Diagnostics.Debug.WriteLine("Finished Sending DFU. Verifying...");
                    //ConversationList.Items.Add("Finished Sending DFU. Verifying...");


                }

                if (resp != null && !resp.IsError)
                {
                    SendRawBytes(resp.BytesSrc);
                }

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
                        System.Diagnostics.Debug.WriteLine("DFU Read stream failed with error: " + ex.Message);
                        // DFU error state

                        MyDFUStep = DFUStep.Error;
                        MyDFUResultStatus = DFUResultStatus.IOException;
                        onDFUStepChanged();
                        // TODO
                        //MainPage.Current.NotifyUser("Read stream failed with error: " + ex.Message, NotifyType.ErrorMessage);
                        //Disconnect();
                        // TODO disconnect yet?
                    }
                }
            }
        }


        public void FromDfuStateNotifState(DFUStep newStep)
        {
            MyDFUStep = newStep;
            MyDFUResultStatus = DFUResultStatus.None;
            onDFUStepChanged();
        }




        // VERY first endpoint called to start DFU process
        // For debug targetFV, may be null
        public void StartDFURequest(byte[] fileBuffer, Firmware targetFV)
        {
            if (MyDFUStep != DFUStep.None) {
                System.Diagnostics.Debug.WriteLine("Programmer Error. StartDFURequest() cannot start in DFUStep '" + MyDFUStep + "'");
                return;
            }

            if (fileBuffer == null) {
                System.Diagnostics.Debug.WriteLine("Programmer Error. StartDFURequest() requires non null fileBuffer.");
                return;
            }
            MyDFUTargetFV = targetFV;
            MyDFUOldFv = MyFirmwareVersion;
            MyDFUOldFv_full_code = MyFv_full_code;


            GaiaHelper.SetFileBuffer(fileBuffer);
            GaiaMessage startDfuCmd = new GaiaMessage((ushort)GaiaMessage.ArcCommand.StartDfu46);
            SendRawBytes(startDfuCmd.BytesSrc);

            MyDFUStep = DFUStep.StartingUpload;
            MyDFUResultStatus = DFUResultStatus.None;
            onDFUStepChanged();



            /**
            Refer to this for overview: https://github.com/wearhaus/CSR/wiki/DFU-details
            In this app, after we send GaiaMessage.ArcCommand.StartDfu46, GaiaHelper will receive
            the ack which calls CreateResponseToMessage() which then calls CreateDfuBegin(); 
            Then the looper fires off the command CreateDfuBegin created (0x0631). This then will 
            cause 
             
             
             
             
             */

        }



    }
}
