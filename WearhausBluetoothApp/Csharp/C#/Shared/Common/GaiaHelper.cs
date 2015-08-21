﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Gaia
{
    /// <summary>
    /// GaiaHelper - Class to encapsulate methods and fields relating to Gaia protocol communcation and firmware update
    /// </summary>
    public class GaiaHelper
    {
        private const byte CHUNK_SIZE = 240;

        private byte[] FileBuffer;
        private int FileChunksSent;

        public bool IsSendingFile;
        public int TotalChunks;

        /// <summary>
        /// Default constructor
        /// </summary>
        public GaiaHelper()
        {
            FileBuffer = null;
            FileChunksSent = 0;

            IsSendingFile = false;
            TotalChunks = 0;
        }

        /// <summary>
        /// Helper method to calculate checksum using XOR of a byte array
        /// </summary>
        /// <param name="b">Byte array to calculate the checksum of</param>
        /// <returns>Byte value of calculated checksum</returns>
        public static byte Checksum(byte[] b)
        {
            byte checkSum = b[0];
            for (int i = 1; i < b.Length; i++)
            {
                checkSum ^= b[i];
            }
            return checkSum;
        }
        
        /// <summary>
        /// Method to set the instance of the FileBuffer corresponding to 
        /// the .dfu file for the purposes of firmware update
        /// </summary>
        /// <param name="buf">FileBuffer as a byte array</param>
        public void SetFileBuffer(byte[] buf)
        {
            FileBuffer = buf;
            TotalChunks = (int)Math.Ceiling((float)buf.Length / CHUNK_SIZE);
        }

        /// <summary>
        /// Calculates how many chunks of the FileBuffer are left to 
        /// send based on a counter of the number of file chunks already sent
        /// </summary>
        /// <returns>Number of Chunks remaining to be sent before the entire DFU file has been sent</returns>
        public int ChunksRemaining()
        {
            return (int)Math.Ceiling((float)FileBuffer.Length / CHUNK_SIZE) - FileChunksSent;
        }

        /// <summary>
        /// Calculate the number of Bytes remaining
        /// MEANT FOR THE CASE WHERE ONLY 1 CHUNK REMAINS WITH FEWER BYTES THAN THE CHUNK_SIZE 
        /// e.g. the final remaining bytes of the file buffer
        /// </summary>
        /// <returns>Number of bytes remaining to be sent</returns>
        public int BytesRemaining()
        {
            return (int)FileBuffer.Length - (FileChunksSent * CHUNK_SIZE);
        }

        /// <summary>
        /// Create and return the next portion of the file buffer to be sent for firmware update
        /// </summary>
        /// <returns>Byte array of CHUNK_SIZE (or less if on the last chunk with fewer than CHUNK_SIZE bytes remaining) of the FileBuffer</returns>
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

        /// <summary>
        /// Prepare the CRC and the 8 bytes required as part of the payload for the DFU Begin Gaia Command
        /// </summary>
        /// <returns>A GaiaMessage object containing the first bytes necessary for DFU Begin</returns>
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

        /// <summary>
        /// Helper static method to combine 2 bytes into a ushort
        /// </summary>
        /// <param name="upper">Upper byte to be combined</param>
        /// <param name="lower">Lower byte to be combined</param>
        /// <returns>Ushort as a result of the combined upper and lower bytes</returns>
        public static ushort CombineBytes(byte upper, byte lower)
        {
            return ((ushort)((((ushort)upper) << 8) | ((ushort)lower)));
        }

        /// <summary>
        /// Method to create responses to received messages to be called wherever the message receive logic code is
        /// </summary>
        /// <param name="receievedMessage">GaiaMessage object representing the message we need to respond to</param>
        /// <param name="checkSum">Optional Parameter of a single byte checksum</param>
        /// <returns>
        /// GaiaMessage object representing the proper response according to the Gaia Protocol
        /// Returns NULL in the case we don't need to respond
        /// </returns>
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
                        resp = GaiaMessage.CreateErrorGaia(" Error, that command cannot be handled at this time!");
                        break;
                }
            }

            return resp;

        }


    
    }
}
