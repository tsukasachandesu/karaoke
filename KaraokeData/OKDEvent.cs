using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OKDPlayer
{
    public class OKDPTrackEvent
    {
        public byte Status { get; set; }
        public byte[] Data { get; set; }
        public byte[] FullSysExData { get; private set; }
        public OKDPTrackEvent(byte status, byte[] data)
        {
            Status = status;
            Data = data;
            if (status == 0xF0)
            {
                FullSysExData = new byte[data.Length + 1];
                FullSysExData[0] = 0xF0;
                Buffer.BlockCopy(data, 0, FullSysExData, 1, data.Length);
            }
        }
        public byte GetChannel()
        {
            if (Status < 0x80 || Status > 0xEF)
                throw new InvalidOperationException("Invalid MIDI status byte for channel retrieval.");
            return (byte)(Status & 0x0F);
        }


        public byte GetStatusType()
        {
            //if (Status < 0x80 || Status > 0xEF)
            //    throw new InvalidOperationException("Invalid MIDI status byte for status type retrieval.");
            return (byte)(Status & 0xF0);
        }

        public byte[] ToBytes()
        {
            var result = new List<byte>();
            result.Add(Status);
            if (Data != null && Data.Length > 0)
            {
                result.AddRange(Data);
            }
            return result.ToArray();
        }
    }

    public class MIDITrackEvent : OKDPTrackEvent
    {
        public int DeltaTime { get; set; }
        public MIDITrackEvent(int deltaTime, byte status, byte[] data)
            : base(status, data)
        {
            DeltaTime = deltaTime;
        }

    }
    public class PTrackAbsoluteTimeEvent : OKDPTrackEvent
    {
        public byte Port { get; set; }
        public byte Track { get; set; }
        public uint AbsoluteTime { get; set; } //Absolute time millis

        public PTrackAbsoluteTimeEvent(byte port, byte track, uint abstime, byte status, byte[] data)
            : base(status, data)
        {
            Port = port;
            Track = track;
            AbsoluteTime = abstime;
        }
    }
    public class PTrackEvent : MIDITrackEvent
    {
        public int? Duration { get; set; } //Duration millis      

        public PTrackEvent(int deltaTime, byte status, byte[] data, int? duration = null)
            : base(deltaTime, status, data)
        {
            Duration = duration;
        }

    }
}
