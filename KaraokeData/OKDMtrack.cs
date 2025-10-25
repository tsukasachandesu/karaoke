using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OKDPlayer
{
    public class OKDMTrackEvent : MIDITrackEvent
    {
        public OKDMTrackEvent(int deltaTime, byte status, byte[] data)
            : base(deltaTime, status, data)
        {
        }
    }

    public class OKDMTrackAbsoluteTimeEvent : OKDPTrackEvent
    {
        public uint AbsoluteTime { get; }

        public OKDMTrackAbsoluteTimeEvent(uint absoluteTime, byte status, byte[] data)
            : base(status, data)
        {
            AbsoluteTime = absoluteTime;
        }
    }

    public class OKDMTrack
    {
        private static readonly byte[] EndOfTrackMark = new byte[] { 0x00, 0x00, 0x00, 0x00 };

        private readonly List<OKDMTrackEvent> _events = new List<OKDMTrackEvent>();
        private readonly List<OKDMTrackAbsoluteTimeEvent> _absoluteEvents = new List<OKDMTrackAbsoluteTimeEvent>();

        public byte TrackId { get; set; }

        public IReadOnlyList<OKDMTrackEvent> Events => _events;

        public IReadOnlyList<OKDMTrackAbsoluteTimeEvent> AbsoluteEvents => _absoluteEvents;

        public (uint absoluteTime, uint tempo)[] Tempos { get; private set; } = Array.Empty<(uint absoluteTime, uint tempo)>();
        public (uint absoluteTime, uint numerator, uint denominator)[] TimeSignatures { get; private set; } = Array.Empty<(uint absoluteTime, uint numerator, uint denominator)>();
        public (uint startTime, uint endTime)[] Hooks { get; private set; } = Array.Empty<(uint startTime, uint endTime)>();
        public (uint absoluteTime, uint value)[] VisibleGuideMelDelimiters { get; private set; } = Array.Empty<(uint absoluteTime, uint value)>();
        public int TwoChorusFadeoutTime { get; private set; } = -1;
        public (uint startTime, uint endTime)[] SongSection { get; private set; } = Array.Empty<(uint startTime, uint endTime)>();
        public (uint startTime, uint endTime)[] AdpcmSections { get; private set; } = Array.Empty<(uint startTime, uint endTime)>();

        public void Parse(byte[] data)
        {
            _events.Clear();
            _absoluteEvents.Clear();

            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                while (reader.BaseStream.Position < reader.BaseStream.Length)
                {
                    int deltaTime = reader.ReadVariableInt32Ex();

                    byte[] endMark = reader.ReadBytes(4);
                    if (endMark.SequenceEqual(EndOfTrackMark))
                        break;

                    reader.BaseStream.Seek(-4, SeekOrigin.Current);

                    byte status = OKD.ReadStatusByte(reader);
                    byte[] eventData = ReadEventData(reader, status);

                    _events.Add(new OKDMTrackEvent(deltaTime, status, eventData));
                }
            }

            uint absoluteTime = 0;
            foreach (var ev in _events)
            {
                absoluteTime += (uint)ev.DeltaTime;
                _absoluteEvents.Add(new OKDMTrackAbsoluteTimeEvent(absoluteTime, ev.Status, ev.Data));
            }

            InterpretTrack();
        }

        private static byte[] ReadEventData(BinaryReader reader, byte status)
        {
            if (status == 0xFF)
            {
                return ReadSysExDataBytes(reader);
            }

            int dataLength = status switch
            {
                0xF1 => 0,
                0xF2 => 0,
                0xF3 => 1,
                0xF4 => 1,
                0xF5 => 0,
                0xF6 => 1,
                0xF8 => 1,
                _ => throw new InvalidOperationException($"Unknown M-Track status byte detected: 0x{status:X2}"),
            };

            byte[] data = reader.ReadBytes(dataLength);
            if (!OKD.IsDataBytes(data))
                throw new InvalidOperationException($"Invalid data bytes detected in M-Track event: {BitConverter.ToString(data)}");
            return data;
        }

        private static byte[] ReadSysExDataBytes(BinaryReader reader)
        {
            using (var buffer = new MemoryStream())
            {
                while (true)
                {
                    byte b = reader.ReadByte();
                    buffer.WriteByte(b);
                    if ((b & 0x80) == 0x80)
                    {
                        if (b != 0xFE)
                            throw new InvalidOperationException($"Unterminated SysEx message detected. stop_byte=0x{b:X2}");
                        break;
                    }
                }
                return buffer.ToArray();
            }
        }

        private void InterpretTrack()
        {
            var tempos = new List<(uint absoluteTime, uint tempo)>();
            var timeSignatures = new List<(uint absoluteTime, uint numerator, uint denominator)>();
            var hooks = new List<(uint startTime, uint endTime)>();
            var visibleGuideMelDelimiters = new List<(uint absoluteTime, uint value)>();
            var songSections = new List<(uint startTime, uint endTime)>();
            var adpcmSections = new List<(uint startTime, uint endTime)>();

            TwoChorusFadeoutTime = -1;

            uint? currentBeatStart = _absoluteEvents.FirstOrDefault(ev => ev.Status == 0xF1 || ev.Status == 0xF2)?.AbsoluteTime;
            uint? currentHookStart = null;
            uint? songSectionStart = null;
            uint? currentAdpcmStart = null;

            foreach (var ev in _absoluteEvents)
            {
                switch (ev.Status)
                {
                    case 0xF1:
                    case 0xF2:
                        if (currentBeatStart.HasValue && ev.AbsoluteTime > currentBeatStart.Value)
                        {
                            uint beatLength = ev.AbsoluteTime - currentBeatStart.Value;
                            if (beatLength > 0)
                            {
                                uint tempo = (uint)Math.Max(1, Math.Round(60000.0 / beatLength));
                                if (tempos.Count == 0 || tempos[^1].tempo != tempo)
                                {
                                    tempos.Add((currentBeatStart.Value, tempo));
                                }
                            }
                        }
                        currentBeatStart = ev.AbsoluteTime;
                        break;

                    case 0xF3:
                        if (ev.Data.Length > 0)
                        {
                            byte hookType = ev.Data[0];
                            if (hookType == 0x00 || hookType == 0x02)
                            {
                                currentHookStart = ev.AbsoluteTime;
                            }
                            else if ((hookType == 0x01 || hookType == 0x03) && currentHookStart.HasValue)
                            {
                                if (ev.AbsoluteTime >= currentHookStart.Value)
                                {
                                    hooks.Add((currentHookStart.Value, ev.AbsoluteTime));
                                }
                                currentHookStart = null;
                            }
                        }
                        break;

                    case 0xF4:
                        if (ev.Data.Length > 0)
                        {
                            visibleGuideMelDelimiters.Add((ev.AbsoluteTime, ev.Data[0]));
                        }
                        break;

                    case 0xF5:
                        TwoChorusFadeoutTime = (int)ev.AbsoluteTime;
                        break;

                    case 0xF6:
                        if (ev.Data.Length > 0)
                        {
                            byte sectionType = ev.Data[0];
                            if (sectionType == 0x00)
                            {
                                songSectionStart = ev.AbsoluteTime;
                            }
                            else if (sectionType == 0x01 && songSectionStart.HasValue)
                            {
                                if (ev.AbsoluteTime >= songSectionStart.Value)
                                {
                                    songSections.Add((songSectionStart.Value, ev.AbsoluteTime));
                                }
                                songSectionStart = null;
                            }
                        }
                        break;

                    case 0xF8:
                        if (ev.Data.Length > 0)
                        {
                            byte adpcmType = ev.Data[0];
                            if (adpcmType == 0x00)
                            {
                                currentAdpcmStart = ev.AbsoluteTime;
                            }
                            else if (adpcmType == 0x01 && currentAdpcmStart.HasValue)
                            {
                                if (ev.AbsoluteTime >= currentAdpcmStart.Value)
                                {
                                    adpcmSections.Add((currentAdpcmStart.Value, ev.AbsoluteTime));
                                }
                                currentAdpcmStart = null;
                            }
                        }
                        break;

                    case 0xFF:
                        if (ev.Data.Length >= 3)
                        {
                            byte numerator = ev.Data[1];
                            byte exponent = ev.Data[2];
                            uint denominator = (uint)Math.Pow(2, exponent);
                            timeSignatures.Add((ev.AbsoluteTime, numerator, denominator));
                        }
                        break;
                }
            }

            if (tempos.Count == 0)
            {
                tempos.Add((0u, 125u));
            }

            if (timeSignatures.Count == 0)
            {
                timeSignatures.Add((0u, 4u, 4u));
            }

            Tempos = tempos.OrderBy(t => t.absoluteTime).ToArray();
            TimeSignatures = timeSignatures.OrderBy(ts => ts.absoluteTime).ToArray();
            Hooks = hooks.ToArray();
            VisibleGuideMelDelimiters = visibleGuideMelDelimiters.ToArray();
            SongSection = songSections.ToArray();
            AdpcmSections = adpcmSections.ToArray();
        }
    }
}
