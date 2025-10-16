using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OKDPlayer.KaraokeData
{
    internal class OKDADPCM
    {
        //copy-paste from adpcm.py
        const int FRAMES_PER_FRAME_GROUP = 18;

        const int SUB_FRAMES = 4;
        const int SUB_FRAME_NIBBLES = 28;
        const int FRAME_SIZE_BYTES = 128;    //16 params + 112 sample-bytes
        const int PARAM_BYTES_PER_FRAME = 16;
        const int SAMPLE_BYTES_PER_FRAME = 112; //contains 224 nibbles (low/high)
        const int GROUP_PADDING_BYTES = 20;

        const int SHIFT_LIMIT = 12;
        const int INDEX_LIMIT = 3;

        static readonly double[] K0 = { 0.0, 0.9375, 1.796875, 1.53125 };
        static readonly double[] K1 = { 0.0, 0.0, -0.8125, -0.859375 };
        static readonly int[] SIGNED_NIBBLES = { 0, 1, 2, 3, 4, 5, 6, 7, -8, -7, -6, -5, -4, -3, -2, -1 };

        //YAWV to WAV
        public static byte[] ConvertToWAV(byte[] adpcmBytes, int sampleRate /* e.g., 32000 */)
        {
            if (adpcmBytes == null || adpcmBytes.Length == 0)
                throw new ArgumentException("Empty ADPCM input");

            short[] pcm = DecodeAdpcmToPcm(adpcmBytes);
            return BuildWav(pcm, sampleRate, channels: 1, bitsPerSample: 16);
        }

        //YAWV -> PCM
        public static byte[] ConvertToPCM(byte[] adpcmBytes)
        {
            if (adpcmBytes == null || adpcmBytes.Length == 0)
                throw new ArgumentException("Empty ADPCM input.");
            short[] pcm = DecodeAdpcmToPcm(adpcmBytes);
            return PcmToBytes(pcm);
        }

        private static byte[] PcmToBytes(short[] pcm)
        {
            var bytes = new byte[pcm.Length * 2];
            Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        private static short[] DecodeAdpcmToPcm(byte[] data)
        {
            var samples = new List<short>(capacity: data.Length * 2);

            int prev1 = 0;
            int prev2 = 0;

            int pos = 0;
            bool keepGoing = true;

            while (keepGoing)
            {
                //Decode frame group (up to 18 frames)
                for (int fg = 0; fg < FRAMES_PER_FRAME_GROUP; fg++)
                {
                    if (pos + FRAME_SIZE_BYTES > data.Length)
                    {
                        keepGoing = false;
                        break;
                    }

                    //Read one 128-byte frame
                    Span<byte> frame = data.AsSpan(pos, FRAME_SIZE_BYTES);
                    pos += FRAME_SIZE_BYTES;

                    Span<byte> parameters = frame.Slice(0, PARAM_BYTES_PER_FRAME);
                    Span<byte> sampleBytes = frame.Slice(PARAM_BYTES_PER_FRAME, SAMPLE_BYTES_PER_FRAME);

                    //Decode a frame -> 224 samples (8 sub-subframes * 28)
                    //i = subframe index (0..3), j = nibble selector (0: low, 1: high)
                    for (int i = 0; i < SUB_FRAMES; i++)
                    {
                        for (int j = 0; j < 2; j++)
                        {
                            int spIndex = j + i * 2;
                            if (i >= 2) spIndex += 4;
                            byte sp = parameters[spIndex];

                            //decode one subframe -> 28 samples
                            for (int k = 0; k < SUB_FRAME_NIBBLES; k++)
                            {
                                int suIndex = k * SUB_FRAMES + i;   //interleaved
                                byte suByte = sampleBytes[suIndex];
                                int nibble = (j == 0) ? (suByte & 0x0F) : (suByte >> 4);

                                int sample = DecodeSample(sp, nibble, ref prev1, ref prev2);
                                samples.Add((short)sample);
                            }
                        }
                    }
                }

                if (keepGoing)
                {
                    int toSkip = Math.Min(GROUP_PADDING_BYTES, Math.Max(0, data.Length - pos));
                    pos += toSkip;

                    //If can't read the very next frame fully, stop on next loop
                    if (pos + FRAME_SIZE_BYTES > data.Length)
                        keepGoing = false;
                }
            }

            return samples.ToArray();
        }

        private static int DecodeSample(byte sp, int suNibble, ref int prev1, ref int prev2)
        {
            int shift = sp & 0x0F;
            if (shift > SHIFT_LIMIT)
                throw new InvalidDataException("ADPCM parameter `shift` out of range.");

            int index = sp >> 4;
            if (index > INDEX_LIMIT)
                throw new InvalidDataException("ADPCM parameter `index` out of range.");

            //sign-extend nibble via lookup
            int signedNib = SIGNED_NIBBLES[suNibble & 0x0F];

            //predictor + scaling (12-bit base as in reference)
            double predicted = (signedNib << (12 - (shift & 0x1F)))
                               + K0[index] * prev1
                               + K1[index] * prev2;

            int clamped = Clamp16(predicted);

            prev2 = prev1;
            prev1 = clamped;

            return clamped;
            static int Clamp16(double v)
            {
                if (v > 32767.0) return 32767;
                if (v < -32768.0) return -32768;
                return (int)Math.Round(v);
            }
        }

        //RIFF header writer for PCM16 mono
        private static byte[] BuildWav(short[] pcm, int sampleRate, short channels, short bitsPerSample)
        {
            if (channels < 1) throw new ArgumentOutOfRangeException(nameof(channels));
            if (bitsPerSample != 16) throw new ArgumentOutOfRangeException(nameof(bitsPerSample), "Only 16-bit supported here.");

            int byteRate = sampleRate * channels * (bitsPerSample / 8);
            short blockAlign = (short)(channels * (bitsPerSample / 8));
            int dataSize = pcm.Length * (bitsPerSample / 8);
            int riffChunkSize = 4 + (8 + 16) + (8 + dataSize); // "WAVE" + fmt + data

            using var ms = new MemoryStream(44 + dataSize);
            using var bw = new BinaryWriter(ms);

            //RIFF header
            bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(riffChunkSize);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

            //fmt
            bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16);                 // PCM fmt chunk size
            bw.Write((short)1);           //AudioFormat = 1 (PCM)
            bw.Write(channels);
            bw.Write(sampleRate);
            bw.Write(byteRate);
            bw.Write(blockAlign);
            bw.Write(bitsPerSample);

            //data chunk
            bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            bw.Write(dataSize);

            //PCM data (LE)
            foreach (short s in pcm)
                bw.Write(s);

            bw.Flush();
            return ms.ToArray();
        }
    }
}
