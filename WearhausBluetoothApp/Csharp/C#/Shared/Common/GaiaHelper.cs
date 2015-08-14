using System;
using System.Collections.Generic;
using System.Text;

namespace Gaia
{
    public class GaiaHelper
    {
        private const byte CHUNK_SIZE = 240;

        private byte[] FileBuffer;
        private int FileChunksSent;

        private ushort LastSentCommand;
        public bool IsSendingFile;
        public int TotalChunks;

        public GaiaHelper()
        {
            FileBuffer = null;
            FileChunksSent = 0;

            LastSentCommand = 0x0000;
            IsSendingFile = false;
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

        /*public byte[] CreateGaiaCommand(ushort usrCmd, bool isAck = false)
        {
            return CreateGaiaCommand(usrCmd, GaiaMessage.GAIA_FLAG_CHECK, new byte[0], isAck);
        }

        public byte[] CreateGaiaCommand(ushort usrCmd, byte[] payload, bool isAck = false)
        {
            return CreateGaiaCommand(usrCmd, GaiaMessage.GAIA_FLAG_CHECK, payload, isAck);
        }

        public byte[] CreateGaiaCommand(ushort usrCmd, byte flag, bool isAck = false)
        {
            return CreateGaiaCommand(usrCmd, flag, new byte[0], isAck);
        }

        public byte[] CreateGaiaCommand(ushort usrCmd, byte flag, byte[] payload, bool isAck = false)
        {
            byte[] commandMessage;
            int msgLen = GaiaMessage.GAIA_FRAME_LEN + payload.Length;

            if (flag == GaiaMessage.GAIA_FLAG_CHECK) { msgLen += 1; }
            commandMessage = new byte[msgLen];

            commandMessage[0] = GaiaMessage.GAIA_FRAME_START;
            commandMessage[1] = GaiaMessage.GAIA_PROTOCOL_VER;
            commandMessage[2] = flag;
            commandMessage[3] = (byte)payload.Length;
            commandMessage[4] = GaiaMessage.GAIA_CSR_VENDOR_ID >> 8;
            commandMessage[5] = GaiaMessage.GAIA_CSR_VENDOR_ID & 0xff;
            commandMessage[6] = (byte)(usrCmd >> 8);
            commandMessage[7] = (byte)(usrCmd & 0xff);

            if (Enum.IsDefined(typeof(ArcCommand), usrCmd))
            {
                commandMessage[4] = GaiaMessage.GAIA_WEARHAUS_VENDOR_ID >> 8;
                commandMessage[5] = GaiaMessage.GAIA_WEARHAUS_VENDOR_ID & 0xff;
            }

            System.Buffer.BlockCopy(payload, 0, commandMessage, 8, payload.Length);

            if (flag == GaiaMessage.GAIA_FLAG_CHECK)
            {
                commandMessage[msgLen - 1] = Checksum(commandMessage);
            }
            if (!isAck)
            {
                LastSentCommand = usrCmd;
            }
            return commandMessage;
        }*/

        private GaiaMessage CreateDFUBegin()
        {
            if (FileBuffer == null)
            {
                System.Diagnostics.Debug.WriteLine("Did not specify a DFU File! Please Pick a File!");
                return null;
            }

            // Get CRC first from File
            uint fileSize = (uint)FileBuffer.Length;
            byte[] crcBuffer = new byte[fileSize + 4];
            System.Buffer.BlockCopy(FileBuffer, 0, crcBuffer, 4, (int)fileSize);
            crcBuffer[0] = crcBuffer[1] = crcBuffer[2] = crcBuffer[3] = (byte)0xff;

            long crc = DfuCRC.fileCrc(crcBuffer);

            // Send DfuBegin with CRC and fileSize
            uint mCrc = (uint)(((crc & 0xFFFFL) << 16) | ((crc & 0xFFFF0000L) >> 16));
            byte[] beginPayload = new byte[8];
            beginPayload[0] = (byte)(fileSize >> 24);
            beginPayload[1] = (byte)(fileSize >> 16);
            beginPayload[2] = (byte)(fileSize >> 8);
            beginPayload[3] = (byte)(fileSize);

            beginPayload[4] = (byte)(mCrc >> 24);
            beginPayload[5] = (byte)(mCrc >> 16);
            beginPayload[6] = (byte)(mCrc >> 8);
            beginPayload[7] = (byte)(mCrc);

            return new GaiaMessage((ushort)GaiaMessage.GaiaCommand.DFUBegin, beginPayload);
        }

        public static ushort CombineBytes(byte upper, byte lower)
        {
            return ((ushort)((((ushort)upper) << 8) | ((ushort)lower)));
        }

        public GaiaMessage CreateResponseToMessage(GaiaMessage receievedMessage, byte checkSum = 0x00)
        {

            // Check if the Response is a command or an ACK
            ushort command = receievedMessage.CommandId;

            GaiaMessage resp = null;

            if (receievedMessage.IsAck) // If this message is an ACK, we must find what the acked command id is
            {

                switch (command)
                {
                    case (ushort)GaiaMessage.ArcCommand.StartDfu | 0x8000:
                        resp = CreateDFUBegin();
                        break;

                    default:
                        break;
                }
            }
            else // otherwise, this is an actual command! We must respond to it
            {
                switch (command)
                {
                    case (ushort)GaiaMessage.GaiaNotification.Event:
                        if (receievedMessage.PayloadSrc[0] == 0x10 && receievedMessage.PayloadSrc[1] == 0x00)
                        {
                            // WE NEED TO LOOP SENDING CHUNKS
                            IsSendingFile = true;
                        }
                        break;

                    case (ushort)GaiaMessage.GaiaCommand.DFURequest:
                        resp = GaiaMessage.CreateAck(command);
                        break;

                    default:
                        resp = GaiaMessage.CreateAck(command);
                        break;
                }
            }
            return resp;

        }


    
    }
}
