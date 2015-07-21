using System;
using System.Collections.Generic;
using System.Text;
using Windows.Storage;
using Windows.Storage.Streams;

namespace GaiaDFU
{
    public class GaiaDfu
    {
            
        // GAIA FRAMING PARAMS
        public const byte GAIA_FRAME_START = 0xff;
        public const byte GAIA_PROTOCOL_VER = 0x01;
        public const byte GAIA_FLAG_CHECK = 0x01;
        public const ushort GAIA_CSR_VENDOR_ID = 0x000a;
        public const ushort GAIA_WEARHAUS_VENDOR_ID = 0x0a4c;
        public const byte GAIA_FRAME_LEN = 8;
        
        private const byte CHUNK_SIZE = 240;

        private byte[] FileBuffer;
        private int FileChunksSent = 0;
        private DataWriter SocketWriter;

        public ushort LastSentCommand;
        public int TotalChunks;

        public GaiaDfu(DataWriter dw)
        {
            SocketWriter = dw;
            LastSentCommand = 0x0000;
        }

        public static byte Checksum(byte[] b)
        {
            byte checkSum = b[0];
            for (int i = 1; i < b.Length; i++)
            {
                checkSum ^= b[i];
            }
            return checkSum;
        }

        public void SetFileBuffer(byte[] buf)
        {
            FileBuffer = buf;
            TotalChunks = (int)Math.Ceiling((float)buf.Length / CHUNK_SIZE);
        }

        public int ChunksRemaining()
        {
            return (int)Math.Ceiling((float)FileBuffer.Length / CHUNK_SIZE) - FileChunksSent;
        }

        public int BytesRemaining()
        {
            return (int)FileBuffer.Length - (FileChunksSent * CHUNK_SIZE);
        }

        public byte[] GetNextFileChunk()
        {
            byte[] fileChunk;
            if (ChunksRemaining() == 1)
            {
                int bytesToWrite = BytesRemaining();
                fileChunk = new byte[bytesToWrite];
                System.Buffer.BlockCopy(FileBuffer, FileChunksSent * CHUNK_SIZE, fileChunk, 0, bytesToWrite);
            }
            else
            {
                fileChunk = new byte[CHUNK_SIZE];
                System.Buffer.BlockCopy(FileBuffer, FileChunksSent * CHUNK_SIZE, fileChunk, 0, CHUNK_SIZE);
            }
            FileChunksSent++;

            return fileChunk;
        }

        public byte[] CreateGaiaCommand(ushort usrCmd, bool isAck = false)
        {
            return CreateGaiaCommand(usrCmd, GAIA_FLAG_CHECK, new byte[0], isAck);
        }

        public byte[] CreateGaiaCommand(ushort usrCmd, byte[] payload, bool isAck = false)
        {
            return CreateGaiaCommand(usrCmd, GAIA_FLAG_CHECK, payload, isAck);
        }

        public byte[] CreateGaiaCommand(ushort usrCmd, byte flag, bool isAck = false)
        {
            return CreateGaiaCommand(usrCmd, flag, new byte[0], isAck);
        }

        public byte[] CreateGaiaCommand(ushort usrCmd, byte flag, byte[] payload, bool isAck = false)
        {
            byte[] commandMessage;
            int msgLen = GAIA_FRAME_LEN + payload.Length;

            if(flag == GAIA_FLAG_CHECK){ msgLen += 1; }
            commandMessage = new byte[msgLen];

            commandMessage[0] = GAIA_FRAME_START;
            commandMessage[1] = GAIA_PROTOCOL_VER;
            commandMessage[2] = flag;
            commandMessage[3] = (byte)payload.Length;
            commandMessage[4] = GAIA_CSR_VENDOR_ID >> 8;
            commandMessage[5] = GAIA_CSR_VENDOR_ID & 0xff;
            commandMessage[6] = (byte)(usrCmd >> 8);
            commandMessage[7] = (byte)(usrCmd & 0xff);
            
            if (Enum.IsDefined(typeof(ArcCommand), usrCmd))
            {
                commandMessage[4] = GAIA_WEARHAUS_VENDOR_ID >> 8;
                commandMessage[5] = GAIA_WEARHAUS_VENDOR_ID & 0xff;
            }

            System.Buffer.BlockCopy(payload, 0, commandMessage, 8, payload.Length);

            if (flag == GAIA_FLAG_CHECK)
            {
                commandMessage[msgLen - 1] = Checksum(commandMessage);
            }
            if (!isAck)
            {
                LastSentCommand = usrCmd;
            }
            return commandMessage;
        }

        public byte[] CreateAck(ushort usrCmd)
        {
            byte[] ackPayload = { 0x00 };
            return CreateGaiaCommand((ushort)(usrCmd | 0x8000), ackPayload, isAck: true);
        }

        public byte[] CreateDfuBegin(long crc, uint filesize)
        {
            // Swap the 16 bit parts
            uint mCrc = (uint) (((crc & 0xFFFFL) << 16) | ((crc & 0xFFFF0000L) >> 16));
            byte[] beginPayload = new byte[8];
            beginPayload[0] = (byte)(filesize >> 24);
            beginPayload[1] = (byte)(filesize >> 16);
            beginPayload[2] = (byte)(filesize >> 8);
            beginPayload[3] = (byte)(filesize);

            beginPayload[4] = (byte)(mCrc >> 24);
            beginPayload[5] = (byte)(mCrc >> 16);
            beginPayload[6] = (byte)(mCrc >> 8);
            beginPayload[7] = (byte)(mCrc);

            return CreateGaiaCommand((ushort)GaiaCommand.DFUBegin, beginPayload);
        }

        public static ushort CombineBytes(byte upper, byte lower)
        {
            return ((ushort)((((ushort)upper) << 8) | ((ushort)lower)));
        }

        public byte[] ProcessReceievedMessage(byte[] receivedFrame, byte[] receivedPayload, byte checkSum = 0x00)
        {

            // Check if the Response is a command or an ACK
            byte commandUpperByte = receivedFrame[6];
            ushort command = CombineBytes(receivedFrame[6], receivedFrame[7]);
            return receivedFrame;

            /*if (commandUpperByte >> 4 == ((LastSentCommand >> 12) | 0x8)) // ACK is always the command id (16 bits) masked with 0x8000 so upper byte must start with 0x8_
            {
                receivedStr += "[ACK!] ";
                ConversationList.Items.Add("Received: " + receivedStr );
            }
            else // otherwise, this is an actual command! We must respond to it
            {
                receivedStr += "[Command!] ";
                ConversationList.Items.Add("Received: " + receivedStr );
                SendGaiaMessage(DFUHandler.CreateAck(GaiaDfu.CombineBytes(receivedFrame[6], receivedFrame[7])));
            }*/

        }

        public enum GaiaCommand : ushort
        {
            GetAppVersion       = 0x0304,
            GetRssi             = 0x0301,

            SetLED              = 0x0101,
            GetLED              = 0x0181,

            SetTone             = 0x0102,
            GetTone             = 0x0182,

            SetDefaultVolume    = 0x0103,
            GetDefaultVolume    = 0x0183,

            ChangeVolume        = 0x0201,
            ToggleBassBoost     = 0x0218,
            Toggle3DEnhancement = 0x0219,

            SetLEDControl       = 0x0207,
            GetLEDControl       = 0x0287,

            DeviceReset         = 0x0202,
            PowerOff            = 0x0204,

            GetBattery          = 0x0302,
            GetModuleID         = 0x0303,

            DFURequest          = 0x0630,
            DFUBegin            = 0x0631,

            NoOp                = 0x0700
        }

        public enum GaiaNotification : ushort
        {
            Register            = 0x4001,
            Get                 = 0x4081,
            Cancel              = 0x4002,
            Event               = 0x4003
        }

        public enum ArcCommand : ushort
        {
            GetColor	  		= 0x6743,
            SetColor 	  	 	= 0x7343,

            GetHeadphoneID	    = 0x6749,
            GetHeadphoneState   = 0x6753,
            
            GetBattery	  		= 0x6742,
            
            SetPulse  	 		= 0x7350,
            GetPulse	 	 	= 0x6750,
            
            SetTouch  	 		= 0x7347,
            GetTouch	 	 	= 0x6747,
            
            VolumeUp			= 0x7655,
            VolumeDown 			= 0x7644,
            
            StartDfu	 		= 0x6346, 
            
            StartScan     		= 0x6353,
            
            StartBroadcast		= 0x6342,
            JoinStation    	 	= 0x634C,
            GoIdle     		 	= 0x6349,
            
            TurnOnMultipoint	= 0x634D,
            
            JoinNearestStation 	= 0x634E
        }
    }
}
