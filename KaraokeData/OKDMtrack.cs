using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OKDPlayer
{
    public class OKDMTrackEvent
    {
        public uint DeltaTime { get; set; }
        public byte Status { get; set; }
        public byte[] Data { get; set; }
    }

    public class OKDMTrackAbsoluteEvent
    {
        public uint AbsoluteTime { get; set; }
        public byte Status { get; set; }
        public byte[] Data { get; set; }
    }

    public class OKDMTrackInterpretation
    {
        public (uint absoluteTime, uint tempo)[] Tempos { get; set; } = Array.Empty<(uint, uint)>();
        public (uint absoluteTime, uint numerator, uint denominator)[] TimeSignatures { get; set; } = Array.Empty<(uint, uint, uint)>();
        public (uint startTime, uint endTime)[] Hooks { get; set; } = Array.Empty<(uint, uint)>();
        public (uint absoluteTime, uint value)[] VisibleGuideMelDelimiters { get; set; } = Array.Empty<(uint, uint)>();
        public int TwoChorusFadeoutTime { get; set; } = -1;
        public (uint startTime, uint endTime)[] SongSections { get; set; } = Array.Empty<(uint, uint)>();
        public (uint startTime, uint endTime)[] AdpcmSections { get; set; } = Array.Empty<(uint, uint)>();
    }

    public class OKDMTrack
    {
        private static readonly byte[] EndOfTrackMark = new byte[] { 0x00, 0x00, 0x00, 0x00 };

        public byte TrackID { get; set; }
        public List<OKDMTrackEvent> Events { get; private set; } = new List<OKDMTrackEvent>();
        public OKDMTrackInterpretation Interpretation { get; private set; } = new OKDMTrackInterpretation();

        public (uint absoluteTime, uint tempo)[] Tempos => Interpretation.Tempos;
        public (uint absoluteTime, uint data1, uint data2)[] TimeSignatures => Interpretation.TimeSignatures;
        public (uint startTime, uint endTime)[] Hooks => Interpretation.Hooks;
        public (uint absoluteTime, uint value)[] VisibleGuideMelDelimiters => Interpretation.VisibleGuideMelDelimiters;
        public int TwoChorusFadeoutTime => Interpretation.TwoChorusFadeoutTime;
        public (uint startTime, uint endTime)[] SongSection => Interpretation.SongSections;
        public (uint startTime, uint endTime)[] AdpcmSections => Interpretation.AdpcmSections;

        public void Parse(byte[] data)
        {
            Events = new List<OKDMTrackEvent>();
            using var stream = new MemoryStream(data ?? Array.Empty<byte>());
            using var reader = new BinaryReader(stream);

            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                uint delta = (uint)reader.ReadVariableInt32Ex();

                byte[] marker = reader.ReadBytes(4);
                if (marker.Length < 4)
                {
                    break;
                }
                if (marker.SequenceEqual(EndOfTrackMark))
                {
                    break;
                }
                reader.BaseStream.Seek(-4, SeekOrigin.Current);

                byte status = OKD.ReadStatusByte(reader);
                byte[] eventData = status switch
                {
                    0xFF => ReadSysExDataBytes(reader),
                    0xF1 or 0xF2 or 0xF5 => Array.Empty<byte>(),
                    0xF3 or 0xF4 or 0xF6 or 0xF8 => ValidateDataBytes(reader.ReadBytes(1)),
                    _ => throw new InvalidOperationException($"Unknown M-Track status byte detected: {status:X2}")
                };

                Events.Add(new OKDMTrackEvent
                {
                    DeltaTime = delta,
                    Status = status,
                    Data = eventData
                });
            }

            Interpretation = InterpretTrack();
        }

        private static byte[] ValidateDataBytes(byte[] data)
        {
            if (!OKD.IsDataBytes(data))
            {
                throw new InvalidOperationException($"Invalid data bytes detected in M-Track event: {BitConverter.ToString(data)}");
            }
            return data;
        }

        private static byte[] ReadSysExDataBytes(BinaryReader reader)
        {
            using var buffer = new MemoryStream();
            while (true)
            {
                if (reader.BaseStream.Position >= reader.BaseStream.Length)
                {
                    throw new InvalidOperationException("Unexpected end of stream while reading M-Track SysEx data.");
                }

                byte b = reader.ReadByte();
                buffer.WriteByte(b);
                if ((b & 0x80) == 0x80)
                {
                    if (b != 0xFE)
                    {
                        throw new InvalidOperationException($"Unterminated SysEx message detected. stop_byte={b:X2}");
                    }
                    break;
                }
            }
            return buffer.ToArray();
        }

        private List<OKDMTrackAbsoluteEvent> GetAbsoluteEvents()
        {
            List<OKDMTrackAbsoluteEvent> absoluteEvents = new List<OKDMTrackAbsoluteEvent>();
            uint absoluteTime = 0;
            foreach (var ev in Events)
            {
                absoluteTime += ev.DeltaTime;
                absoluteEvents.Add(new OKDMTrackAbsoluteEvent
                {
                    AbsoluteTime = absoluteTime,
                    Status = ev.Status,
                    Data = ev.Data
                });
            }
            return absoluteEvents;
        }

        private OKDMTrackInterpretation InterpretTrack()
        {
            List<(uint absoluteTime, uint tempo)> tempos = new List<(uint, uint)>();
            List<(uint absoluteTime, uint numerator, uint denominator)> timeSignatures = new List<(uint, uint, uint)>();
            List<(uint startTime, uint endTime)> hooks = new List<(uint, uint)>();
            List<(uint absoluteTime, uint value)> visibleGuideMelDelimiters = new List<(uint, uint)>();
            List<(uint startTime, uint endTime)> songSections = new List<(uint, uint)>();
            List<(uint startTime, uint endTime)> adpcmSections = new List<(uint, uint)>();

            int twoChorusFadeoutTime = -1;

            var absoluteEvents = GetAbsoluteEvents();
            uint? currentBeatStart = absoluteEvents.FirstOrDefault(e => e.Status == 0xF1 || e.Status == 0xF2)?.AbsoluteTime;
            int currentBpm = 125;
            uint currentHookStart = 0;
            uint? songSectionStart = null;
            uint? currentAdpcmSectionStart = null;

            foreach (var ev in absoluteEvents)
            {
                switch (ev.Status)
                {
                    case 0xF1:
                    case 0xF2:
                        if (currentBeatStart.HasValue)
                        {
                            uint beatLength = ev.AbsoluteTime - currentBeatStart.Value;
                            if (beatLength > 0)
                            {
                                uint bpm = (uint)Math.Round(60000.0 / beatLength);
                                if (tempos.Count == 0 || tempos[^1].absoluteTime != currentBeatStart.Value || tempos[^1].tempo != bpm)
                                {
                                    tempos.Add((currentBeatStart.Value, bpm));
                                }
                                currentBpm = (int)bpm;
                            }
                        }
                        currentBeatStart = ev.AbsoluteTime;
                        break;
                    case 0xF3:
                        if (ev.Data != null && ev.Data.Length > 0)
                        {
                            byte markType = ev.Data[0];
                            if (markType == 0x00 || markType == 0x02)
                            {
                                currentHookStart = ev.AbsoluteTime;
                            }
                            else if ((markType == 0x01 || markType == 0x03) && currentHookStart <= ev.AbsoluteTime)
                            {
                                hooks.Add((currentHookStart, ev.AbsoluteTime));
                            }
                        }
                        break;
                    case 0xF4:
                        if (ev.Data != null && ev.Data.Length > 0)
                        {
                            visibleGuideMelDelimiters.Add((ev.AbsoluteTime, ev.Data[0]));
                        }
                        break;
                    case 0xF5:
                        twoChorusFadeoutTime = (int)ev.AbsoluteTime;
                        break;
                    case 0xF6:
                        if (ev.Data != null && ev.Data.Length > 0)
                        {
                            byte sectionType = ev.Data[0];
                            if (sectionType == 0x00)
                            {
                                songSectionStart = ev.AbsoluteTime;
                            }
                            else if (sectionType == 0x01 && songSectionStart.HasValue)
                            {
                                songSections.Add((songSectionStart.Value, ev.AbsoluteTime));
                                songSectionStart = null;
                            }
                        }
                        break;
                    case 0xF8:
                        if (ev.Data != null && ev.Data.Length > 0)
                        {
                            byte sectionType = ev.Data[0];
                            if (sectionType == 0x00)
                            {
                                currentAdpcmSectionStart = ev.AbsoluteTime;
                            }
                            else if (sectionType == 0x01 && currentAdpcmSectionStart.HasValue)
                            {
                                adpcmSections.Add((currentAdpcmSectionStart.Value, ev.AbsoluteTime));
                                currentAdpcmSectionStart = null;
                            }
                        }
                        break;
                    case 0xFF:
                        if (ev.Data != null && ev.Data.Length >= 3)
                        {
                            byte metaType = ev.Data[0];
                            if (metaType == 0x58)
                            {
                                byte numerator = ev.Data[1];
                                byte denominatorExponent = ev.Data[2];
                                uint denominator = 1;
                                if (denominatorExponent < 32)
                                {
                                    denominator <<= denominatorExponent;
                                }
                                timeSignatures.Add((ev.AbsoluteTime, numerator, denominator));
                            }
                        }
                        break;
                }
            }

            if (tempos.Count == 0)
            {
                uint startTime = currentBeatStart ?? 0;
                tempos.Add((startTime, (uint)currentBpm));
            }

            if (timeSignatures.Count == 0)
            {
                timeSignatures.Add((0, 4, 4));
            }

            return new OKDMTrackInterpretation
            {
                Tempos = tempos.ToArray(),
                TimeSignatures = timeSignatures.ToArray(),
                Hooks = hooks.ToArray(),
                VisibleGuideMelDelimiters = visibleGuideMelDelimiters.ToArray(),
                TwoChorusFadeoutTime = twoChorusFadeoutTime,
                SongSections = songSections.ToArray(),
                AdpcmSections = adpcmSections.ToArray()
            };
        }

    }
}
