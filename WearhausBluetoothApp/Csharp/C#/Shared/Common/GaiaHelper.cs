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

        public bool IsSendingFile;
        public int TotalChunks;

        public GaiaHelper()
        {
            FileBuffer = null;
            FileChunksSent = 0;

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

            // See if we need to verify the checksum first
            if (receievedMessage.IsFlagSet && !receievedMessage.MatchesChecksum(checkSum))
            {
                resp = GaiaMessage.CreateErrorGaia(" Checksum did not match! Expected Checksum: " + receievedMessage.Checksum.ToString("X2") + ", Receieved Checksum: " + checkSum.ToString("X2"));
            }

            if (receievedMessage.IsAck) // If this message is an ACK, we must find what the acked command id is
            {

                switch (command)
                {
                    case (ushort)GaiaMessage.ArcCommand.StartDfu | 0x8000:
                        if (receievedMessage.PayloadSrc[0] == 0x00)
                        {
                            resp = CreateDFUBegin();
                        }
                        else
                        {
                            resp = GaiaMessage.CreateErrorGaia(" Invalid DFU Request!");
                        }
                        break;
                    
                    case (ushort)GaiaMessage.GaiaCommand.DFUBegin | 0x8000:
                        if (receievedMessage.PayloadSrc[0] != 0x00)
                        {
                            resp = GaiaMessage.CreateErrorGaia(" Invalid DFU Request, the device may not be capable of a Firmware Update.");
                        }
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
