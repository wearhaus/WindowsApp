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

        private DataWriter SocketWriter;

        public ushort LastSentCommand;

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

        public byte[] CreateGaiaCommand(ushort usrCmd)
        {
            return CreateGaiaCommand(usrCmd, GAIA_FLAG_CHECK, new byte[0]);
        }

        public byte[] CreateGaiaCommand(ushort usrCmd, byte[] payload)
        {
            return CreateGaiaCommand(usrCmd, GAIA_FLAG_CHECK, payload);
        }

        public byte[] CreateGaiaCommand(ushort usrCmd, byte flag)
        {
            return CreateGaiaCommand(usrCmd, flag, new byte[0]);
        }

        public byte[] CreateGaiaCommand(ushort usrCmd, byte flag, byte[] payload)
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
            LastSentCommand = usrCmd;
            return commandMessage;
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
