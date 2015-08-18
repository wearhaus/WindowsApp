﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Gaia
{
    public class GaiaMessage
    {

        //    0 bytes  1        2        3        4        5        6        7        8      len+8
        //    +--------+--------+--------+--------+--------+--------+--------+--------+ +--------+--------+ +--------+
        //    |   SOF  |VERSION | FLAGS  | LENGTH |    VENDOR ID    |   COMMAND ID    | | PAYLOAD   ...   | | CHECK  |
        //    +--------+--------+--------+--------+--------+--------+--------+--------+ +--------+--------+ +--------+

        // GAIA FRAMING PARAMS
        public const byte GAIA_FRAME_START = 0xff;
        public const byte GAIA_PROTOCOL_VER = 0x01;
        public const byte GAIA_FLAG_CHECK = 0x01;
        public const ushort GAIA_CSR_VENDOR_ID = 0x000a;
        public const ushort GAIA_WEARHAUS_VENDOR_ID = 0x0a4c;
        public const byte GAIA_FRAME_LEN = 8;
        public const ushort GAIA_ACK_MASK = 0x8000;
        public const ushort GAIA_COMMAND_MASK = 0x7FFF;

        public const int OFFS_SOF = 0;
        public const int OFFS_VERSION = 1;
        public const int OFFS_FLAGS = 2;
        public const int OFFS_PAYLOAD_LENGTH = 3;
        public const int OFFS_VENDOR_ID = 4;
        public const int OFFS_VENDOR_ID_H = OFFS_VENDOR_ID;
        public const int OFFS_VENDOR_ID_L = OFFS_VENDOR_ID + 1;
        public const int OFFS_COMMAND_ID = 6;
        public const int OFFS_COMMAND_ID_H = OFFS_COMMAND_ID;
        public const int OFFS_COMMAND_ID_L = OFFS_COMMAND_ID + 1;
        public const int OFFS_PAYLOAD = GAIA_FRAME_LEN;

        public ushort CommandId { get; private set; }
        public ushort VendorId { get; private set; }
        public bool IsFlagSet { get; private set; }
        public bool IsAck { get; private set; }
        public bool IsError { get; private set; }

        public byte[] BytesSrc { get; private set; }
        public byte[] PayloadSrc { get; private set; }
        public byte Checksum { get; private set; }
        public string InfoMessage { get; set; }


        public GaiaMessage(string errorMsg)
        {
            BytesSrc = null;
            PayloadSrc = null;
            VendorId = GaiaMessage.GAIA_CSR_VENDOR_ID;
            IsFlagSet = false;

            IsError = true;
            InfoMessage = errorMsg;
        }

        public GaiaMessage(ushort usrCmd, byte flag, byte[] payload)
        {
            int msgLen = GaiaMessage.GAIA_FRAME_LEN + payload.Length;
            if (flag == GaiaMessage.GAIA_FLAG_CHECK) { msgLen += 1; }

            BytesSrc = new byte[msgLen];

            BytesSrc[OFFS_SOF] = GAIA_FRAME_START;
            BytesSrc[OFFS_VERSION] = GaiaMessage.GAIA_PROTOCOL_VER;
            BytesSrc[OFFS_FLAGS] = flag;
            BytesSrc[OFFS_PAYLOAD_LENGTH] = (byte)payload.Length;
            BytesSrc[OFFS_VENDOR_ID_H] = GaiaMessage.GAIA_CSR_VENDOR_ID >> 8;
            BytesSrc[OFFS_VENDOR_ID_L] = GaiaMessage.GAIA_CSR_VENDOR_ID & 0xff;
            BytesSrc[OFFS_COMMAND_ID_H] = (byte)(usrCmd >> 8);
            BytesSrc[OFFS_COMMAND_ID_L] = (byte)(usrCmd & 0xff);

            CommandId = usrCmd;
            VendorId = GaiaMessage.GAIA_CSR_VENDOR_ID;
            IsFlagSet = false;
            IsAck = (CommandId & GAIA_ACK_MASK) != 0;
            IsError = false;
            InfoMessage = null;

            if (Enum.IsDefined(typeof(GaiaMessage.ArcCommand), usrCmd))
            {
                BytesSrc[OFFS_VENDOR_ID_H] = GaiaMessage.GAIA_WEARHAUS_VENDOR_ID >> 8;
                BytesSrc[OFFS_VENDOR_ID_L] = GaiaMessage.GAIA_WEARHAUS_VENDOR_ID & 0xff;

                VendorId = GaiaMessage.GAIA_WEARHAUS_VENDOR_ID;
            }

            PayloadSrc = payload;
            System.Buffer.BlockCopy(payload, 0, BytesSrc, OFFS_PAYLOAD, payload.Length);

            if (flag == GaiaMessage.GAIA_FLAG_CHECK)
            {
                IsFlagSet = true;
                BytesSrc[msgLen - 1] = GaiaHelper.Checksum(BytesSrc);
                Checksum = BytesSrc[msgLen - 1];
            }
        }

        public GaiaMessage(byte[] receivedFrame, byte[] receivedPayload)
        {
            int msgLen = receivedFrame.Length + receivedPayload.Length;
            if (receivedFrame[OFFS_FLAGS] == GaiaMessage.GAIA_FLAG_CHECK) { msgLen += 1; }

            BytesSrc = new byte[msgLen];
            System.Buffer.BlockCopy(receivedFrame, 0, BytesSrc, 0, receivedFrame.Length);

            PayloadSrc = receivedPayload;
            System.Buffer.BlockCopy(receivedPayload, 0, BytesSrc, 8, receivedPayload.Length);

            CommandId = GaiaHelper.CombineBytes(BytesSrc[OFFS_COMMAND_ID_H], BytesSrc[OFFS_COMMAND_ID_L]);
            VendorId = GaiaHelper.CombineBytes(BytesSrc[OFFS_VENDOR_ID_H], BytesSrc[OFFS_VENDOR_ID_L]);
            IsFlagSet = BytesSrc[OFFS_FLAGS] == GAIA_FLAG_CHECK;
            IsAck = (CommandId & GAIA_ACK_MASK) != 0;
            IsError = false;
            InfoMessage = null;

            if (IsFlagSet)
            {
                BytesSrc[msgLen - 1] = GaiaHelper.Checksum(BytesSrc);
                Checksum = BytesSrc[msgLen - 1];
            }

        }

        public GaiaMessage(ushort usrCmd) : this(usrCmd, GaiaMessage.GAIA_FLAG_CHECK, new byte[0])
        {
        }

        public GaiaMessage(ushort usrCmd, byte[] payload) : this(usrCmd, GaiaMessage.GAIA_FLAG_CHECK, payload)
        {
        }

        public GaiaMessage(ushort usrCmd, byte flag) : this(usrCmd, flag, new byte[0])
        {
        }

        public static GaiaMessage CreateErrorGaia(string errMsg)
        {
            return new GaiaMessage(errMsg);
        }

        public static GaiaMessage CreateAck(ushort usrCmd)
        {
            byte[] ackPayload = { 0x00 };
            return new GaiaMessage((ushort)(usrCmd | GAIA_ACK_MASK), ackPayload);
        }

        public bool MatchesChecksum(byte rcvChecksum)
        {
            if (Checksum != null && rcvChecksum != null)
            {
                return Checksum == rcvChecksum;
            }
            else
            {
                return false;
            }
        }

        public enum GaiaCommand : ushort
        {
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

            DFURequest = 0x0630,
            DFUBegin = 0x0631,

            NoOp = 0x0700
        }

        public enum GaiaNotification : ushort
        {
            Register = 0x4001,
            Get = 0x4081,
            Cancel = 0x4002,
            Event = 0x4003
        }

        public enum ArcCommand : ushort
        {
            GetColor = 0x6743,
            SetColor = 0x7343,

            GetHeadphoneID = 0x6749,
            GetHeadphoneState = 0x6753,

            GetBattery = 0x6742,

            SetPulse = 0x7350,
            GetPulse = 0x6750,

            SetTouch = 0x7347,
            GetTouch = 0x6747,

            VolumeUp = 0x7655,
            VolumeDown = 0x7644,

            StartDfu = 0x6346,

            StartScan = 0x6353,

            StartBroadcast = 0x6342,
            JoinStation = 0x634C,
            GoIdle = 0x6349,

            TurnOnMultipoint = 0x634D,

            JoinNearestStation = 0x634E
        }

    }
}