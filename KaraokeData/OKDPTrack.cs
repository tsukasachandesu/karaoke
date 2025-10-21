using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace OKDPlayer
{
    public class OKDPTrackChannelInfoEntry
    {
        public ushort Attribute { get; set; }
        public ushort Ports { get; set; }
        public byte ControlChange_ax { get; set; }
        public byte ControlChange_cx { get; set; }
    }
    public class OKDPTrackInfoEntry
    {
        public byte TrackNum { get; set; }
        public byte TrackStatus { get; set; }
        public ushort UseChannelGroupFlag { get; set; }
        public ushort[] SingleChannelGroups { get; set; }
        public ushort[] ChannelGroups { get; set; }
        public OKDPTrackChannelInfoEntry[] ChannelInfo { get; set; }
        public ushort SysExPorts { get; set; }
        public bool IsLosslessTrack()
        {
            return (this.TrackStatus & 0x80) == 0x80;
        }
    }
   

    public class OKDExtendedPTrackChannelInfoEntry : OKDPTrackChannelInfoEntry
    {
        public ushort Reserved;
    };

    public class OKDExtendedPTrackInfoEntry : OKDPTrackInfoEntry
    {
        public ushort Reserved;
        public ushort Reserved2;
    };

    

    public class OKDPTrack
    {
        /*
         * PORTS = 4
    CHANNELS_PER_PORT = 16
    TOTAL_CHANNELS = CHANNELS_PER_PORT * PORTS

    CHUNK_NUMBER_PORT_MAP = [0, 1, 2, 2, 3]
         */

        public List<PTrackEvent> TrackEvents { get; private set; }
        public List<PTrackAbsoluteTimeEvent> PTrackAbsoluteEvents { get; private set; }

        public readonly byte[] EOTMARK = new byte[] { 0, 0, 0, 0 };
        public const byte ports = 4;
        public const byte channelsPerPort = 16;
        public const byte totalChannels = channelsPerPort * ports;

        public byte TrackID { get; set; }
        public uint FirstNoteOnTime { get; private set; } = 0; 
        public byte[] ReadSysExBytes(BinaryReader reader)
        {
            List<byte> bytes = new List<byte>();
            while (true)
            {
                byte b = reader.ReadByte();
                bytes.Add(b);
                if ((b & 0x80) == 0x80)
                {
                    if (b != 0xF7)
                    {
                        throw new InvalidOperationException($"Unterminated SysEx message detected. stop_byte={b:X2}");
                    }
                    break;
                }
            }

            return bytes.ToArray();
        }      
        private PTrackEvent ParseEvent(BinaryReader reader)
        {
            List<byte> dataBytes = new List<byte>();
            int DeltaTime = reader.ReadVariableInt32Ex();
            byte[] eot = reader.ReadBytes(4);
            if (eot.SequenceEqual(EOTMARK))
            {
                return null;
            }
            reader.BaseStream.Seek(-4, SeekOrigin.Current); //Go back to the start of the EOT marker

            byte statusByte = OKD.ReadStatusByte(reader);
            byte statusType = (byte)(statusByte & 0xF0);
            byte dataBytesLength = 0;
            switch (statusType)
            {
                case 0x80: //Note Off
                    dataBytesLength = 3;
                    break;
                case 0x90: //Note On
                    dataBytesLength = 2;
                    break;
                case 0xA0: //Alternative CC AX
                    dataBytesLength = 1;
                    break;
                case 0xB0: //Control Change
                    dataBytesLength = 2;
                    break;
                case 0xC0: //Alternative CC CX
                    dataBytesLength = 1;
                    break;
                case 0xD0: //Channge Pressure
                    dataBytesLength = 1;
                    break;
                case 0xE0: //Pitch Bend
                    dataBytesLength = 2;
                    break;
                
                default:
                    break;
            }
            //System
            switch (statusByte)
            {
                case 0xF0: //SysEx
                    dataBytes.AddRange(ReadSysExBytes(reader));
                    return new PTrackEvent(DeltaTime, statusByte, dataBytes.ToArray());
                case 0xF8: //ADPCM Note ON
                    dataBytesLength = 3;
                    break;
                case 0xF9: //Unknown
                    dataBytesLength = 1;
                    break;
                case 0xFA: //ADPCM Channel Vol
                    dataBytesLength = 1;
                    break;
                case 0xFD: //Channel Grouping enable
                    dataBytesLength = 0;
                    break;
                case 0xFE: //Compensation of Alternative CC
                    byte b = reader.ReadByte();
                    reader.BaseStream.Seek(-1, SeekOrigin.Current); //Go back to the start of the byte
                    if ((b & 0xF0) == 0xA0)
                    {
                        //Polyphonic Key Pressure
                        dataBytesLength = 3;
                    }
                    else if ((b & 0xF0) == 0xC0)
                    {
                        //PC 
                        dataBytesLength = 2;
                    }
                    else
                    {
                        throw new InvalidOperationException($"Unknown status byte for FE: {b:X2}");
                    }
                    break;
                default:
                    break;
            }
            byte[] data = reader.ReadBytes(dataBytesLength);
            byte[] dataBytesValidate = (statusByte == 0xFE) ? data[1..] : data;
            if (!OKD.IsDataBytes(dataBytesValidate))
                throw new InvalidOperationException("Invalid data bytes detected in MIDI event.");
            int Duration = 0;
            if (statusType == 0x80 || statusType == 0x90)
            {
                Duration = reader.ReadVariableInt32();
            }

            return new PTrackEvent(DeltaTime, statusByte, data, Duration);
        }

        private PTrackAbsoluteTimeEvent[] RelocatePTrackEvent(OKDPTrackInfoEntry ptrackInfoEntry, byte statusByte, byte[] data, uint absTime, bool channelGroupingEnabled)
        {
            List<PTrackAbsoluteTimeEvent> reEvent = new List<PTrackAbsoluteTimeEvent>();
            byte statusType = (byte)(statusByte & 0xF0);

            if (statusByte == 0xFE)
            {
                //Compensation of Alternative CC
                statusByte = data[0];
                statusType = (byte)(statusByte & 0xF0);
                data = data[1..];
            }

            if (statusType == 0xF0)
            {
                for (byte port = 0; port < OKDPTrack.ports; port++)
                {
                    if (((ptrackInfoEntry.SysExPorts >> port) & 0x0001) != 0x0001)
                        continue;

                    byte track = (byte)(port * OKDPTrack.channelsPerPort);
                    reEvent.Add(new PTrackAbsoluteTimeEvent(port, track, absTime, statusByte, data));
                }
                return reEvent.ToArray();
            }

            byte channel = (byte)(statusByte & 0x0F);
            OKDPTrackChannelInfoEntry channelInfoEntry = ptrackInfoEntry.ChannelInfo[channel];
            ushort defaultChannelGroup = ptrackInfoEntry.SingleChannelGroups[channel];

            if (defaultChannelGroup == 0)
            {
                defaultChannelGroup = (ushort)(0x1 << channel);
            }

            for (byte port = 0; port < OKDPTrack.ports; port++)
            {
                if (((channelInfoEntry.Ports >> port) & 0x0001) != 0x0001)
                    continue;

                for (byte groupedChannel = 0; groupedChannel < OKDPTrack.channelsPerPort; groupedChannel++)
                {
                    if (channelGroupingEnabled)
                    {
                        if (((ptrackInfoEntry.ChannelGroups[channel] >> groupedChannel) & 0x0001) != 0x0001)
                            continue;
                    }
                    else
                    {
                        if (((defaultChannelGroup >> groupedChannel) & 0x0001) != 0x0001)
                            continue;
                    }

                    byte track = (byte)((port * OKDPTrack.channelsPerPort) + groupedChannel);
                    byte reStatusByte = (byte)(statusType | groupedChannel);
                    reEvent.Add(new PTrackAbsoluteTimeEvent(port, track, absTime, reStatusByte, data));
                }
            }
            return reEvent.ToArray();
        }

        public void CalculateFirstNoteONTime()
        {
            foreach(var ev in PTrackAbsoluteEvents)
            {
                if (ev.GetStatusType() == 0x90 && ev.Data.Length >= 2 && ev.Data[1] > 0)
                {
                    this.FirstNoteOnTime = ev.AbsoluteTime;
                    return;
                }
            }
        }

        public void ConvertToAbsoluteTimeTrack(OKDPTrackInfoEntry ptrackInfoEntry)
        {
            uint absoluteTime = 0;
            bool isLoselessTrack = ptrackInfoEntry.IsLosslessTrack();
            bool channelGroupingEnabled = false;
            this.PTrackAbsoluteEvents = new List<PTrackAbsoluteTimeEvent>();

            foreach (var ev in TrackEvents)
            {
                absoluteTime += (uint)ev.DeltaTime;
                byte statusType = ev.GetStatusType();
                if (statusType == 0x80)
                {
                    byte channel = ev.GetChannel();
                    byte noteNum = ev.Data[0];
                    byte onVelocity = ev.Data[1];
                    byte offVelocity = ev.Data[2];
                    uint duration = (uint)ev.Duration;

                    if (!isLoselessTrack)
                    {
                        duration <<= 2;
                    }

                    //NoteON
                    if(onVelocity > 1) //temp fix
                    {
                        this.PTrackAbsoluteEvents.AddRange(RelocatePTrackEvent(
                            ptrackInfoEntry,
                            (byte)(0x90 | channel),
                            new byte[] { noteNum, onVelocity },
                            absoluteTime, channelGroupingEnabled)
                        );
                    }
                    

                    //NoteOFF
                    this.PTrackAbsoluteEvents.AddRange(RelocatePTrackEvent(
                        ptrackInfoEntry,
                        (byte)(0x80 | channel),
                        new byte[] { noteNum, offVelocity },
                        absoluteTime + duration, 
                        channelGroupingEnabled)
                    );
                }
                else if(statusType == 0x90)
                {
                    byte channel = ev.GetChannel();
                    byte noteNum = ev.Data[0];
                    byte onVelocity = ev.Data[1];
                    uint duration = (uint)ev.Duration;
                    if (!isLoselessTrack)
                    {
                        duration <<= 2;
                    }

                    //NoteON
                    if(onVelocity > 1)//temp fix
                    {
                        this.PTrackAbsoluteEvents.AddRange(RelocatePTrackEvent(
                            ptrackInfoEntry,
                            ev.Status,
                            ev.Data,
                            absoluteTime,
                            channelGroupingEnabled)
                        );
                    }
                    

                    //NoteOFF
                    this.PTrackAbsoluteEvents.AddRange(RelocatePTrackEvent(
                        ptrackInfoEntry,
                        (byte)(0x80 | channel),
                        new byte[] { noteNum, 0x40 },
                        absoluteTime + duration,
                        channelGroupingEnabled)
                    );
                }
                else if(statusType == 0xA0)
                {
                    //CC: channel_info_entry.control_change_ax
                    byte channel = ev.GetChannel();
                    OKDPTrackChannelInfoEntry ent = ptrackInfoEntry.ChannelInfo[channel];
                    this.PTrackAbsoluteEvents.AddRange(RelocatePTrackEvent(
                        ptrackInfoEntry,
                        (byte)(0xB0 | channel),
                        new byte[] { ent.ControlChange_ax, ev.Data[0] },
                        absoluteTime,
                        channelGroupingEnabled)
                    );
                }
                else if(statusType == 0xC0)
                {
                    //CC: channel_info_entry.control_change_cx
                    byte channel = ev.GetChannel();
                    OKDPTrackChannelInfoEntry ent = ptrackInfoEntry.ChannelInfo[channel];
                    this.PTrackAbsoluteEvents.AddRange(RelocatePTrackEvent(
                        ptrackInfoEntry,
                        (byte)(0xB0 | channel),
                        new byte[] { ent.ControlChange_cx, ev.Data[0] },
                        absoluteTime,
                        channelGroupingEnabled)
                    );
                }
                else
                {
                    this.PTrackAbsoluteEvents.AddRange(RelocatePTrackEvent(
                        ptrackInfoEntry,
                        ev.Status,
                        ev.Data,
                        absoluteTime,
                        channelGroupingEnabled)
                    );
                }
                channelGroupingEnabled = ev.Status == 0xFD;
            }

            //Sort by absolute time, then by CC event
            //sort CC event first if same time
            //this will be prevent volume issue
            this.PTrackAbsoluteEvents = this.PTrackAbsoluteEvents
                .OrderBy(ev => ev.AbsoluteTime)
                .ThenBy(ev => (ev.GetStatusType() == 0xB0) ? 0 : 1)
                .ToList();

            //Calculate the first Note ON time
            CalculateFirstNoteONTime();
        }

        public void Parse(BinaryReader reader)
        {
            while(reader.BaseStream.Position < reader.BaseStream.Length)
            {
                PTrackEvent trackEvent = ParseEvent(reader);
                if (trackEvent == null)
                {
                    break; //EOT
                }
                if (TrackEvents == null)
                {
                    TrackEvents = new List<PTrackEvent>();
                }
                TrackEvents.Add(trackEvent);
            }

            

        }


    }

    public class OKDPTrackInfo
    {
        public int EntryCount { get; set; }
        public OKDPTrackInfoEntry[] trackInfoEntry { get; set; }

        public void Parse(byte[] data)
        {
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                EntryCount = reader.ReadUInt16BE();
                trackInfoEntry = new OKDPTrackInfoEntry[EntryCount];
                for (int i = 0; i < EntryCount; i++)
                {
                    var entry = new OKDPTrackInfoEntry();
                    entry.TrackNum = reader.ReadByte();
                    entry.TrackStatus = reader.ReadByte();
                    entry.UseChannelGroupFlag = reader.ReadUInt16BE();

                    entry.SingleChannelGroups = new ushort[16];
                    for (ushort j = 0; j < 16; j++)
                    {
                        if (((entry.UseChannelGroupFlag >> j) & 0x0001) == 0x0001)
                        {
                            entry.SingleChannelGroups[j] = reader.ReadUInt16BE();
                        }
                        else
                        {
                            entry.SingleChannelGroups[j] = 0;
                        }
                    }
                   
                    

                    entry.ChannelGroups = new ushort[16];
                    for (ushort j = 0; j < 16; j++)
                    {
                        entry.ChannelGroups[j] = reader.ReadUInt16BE();
                    }

                    entry.ChannelInfo = new OKDPTrackChannelInfoEntry[16];
                    for (ushort j = 0; j < 16; j++)
                    {
                        entry.ChannelInfo[j] = new OKDPTrackChannelInfoEntry
                        {
                            Attribute = reader.ReadByte(),
                            Ports = reader.ReadByte(),
                            ControlChange_ax = reader.ReadByte(),
                            ControlChange_cx = reader.ReadByte()
                        };
                    }

                    entry.SysExPorts = reader.ReadUInt16();
                    trackInfoEntry[i] = entry;
                }
            }

        }
    }
    public class OKDExtendedPTrackInfo : OKDPTrackInfo
    {
        public ushort TGMode { get; set; }

        private ulong _unk1;

        public new void Parse(byte[] data) //ext인경우는 이것만써야됨
        {
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                _unk1 = reader.ReadUInt64BE(); // Unknown, 8 bytes

                TGMode = reader.ReadUInt16BE();
                EntryCount = reader.ReadUInt16BE();

                trackInfoEntry = new OKDExtendedPTrackInfoEntry[EntryCount];
                for (int i = 0; i < EntryCount; i++)
                {
                    var entry = new OKDExtendedPTrackInfoEntry();
                    entry.TrackNum = reader.ReadByte();
                    entry.TrackStatus = reader.ReadByte();
                    entry.Reserved = reader.ReadUInt16BE();
                    entry.SingleChannelGroups = new ushort[16];
                    for (ushort j = 0; j < 16; j++)
                    {
                        entry.SingleChannelGroups[j] = reader.ReadUInt16BE();
                    }

                    entry.ChannelGroups = new ushort[16];
                    for (ushort j = 0; j < 16; j++)
                    {
                        entry.ChannelGroups[j] = reader.ReadUInt16BE();
                    }

                    entry.ChannelInfo = new OKDExtendedPTrackChannelInfoEntry[16];
                    for (ushort j = 0; j < 16; j++)
                    {
                        entry.ChannelInfo[j] = new OKDExtendedPTrackChannelInfoEntry
                        {
                            Attribute = reader.ReadUInt16(),
                            Ports = reader.ReadUInt16BE(),
                            Reserved = reader.ReadUInt16BE(),
                            ControlChange_ax = reader.ReadByte(),
                            ControlChange_cx = reader.ReadByte()
                        };
                    }

                    entry.SysExPorts = reader.ReadUInt16BE();
                    entry.Reserved2 = reader.ReadUInt16BE();

                    trackInfoEntry[i] = entry;

                }
            }
        }
    }
}
