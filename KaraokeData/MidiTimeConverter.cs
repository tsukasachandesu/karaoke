using System;
using System.Collections.Generic;

namespace OKDPlayer
{
    internal class MidiTimeConverter
    {
        private readonly List<(uint TimeMs, double TempoBpm)> _tempoChanges = new List<(uint TimeMs, double TempoBpm)>();

        public MidiTimeConverter(int ticksPerQuarterNote)
        {
            if (ticksPerQuarterNote <= 0)
                throw new ArgumentOutOfRangeException(nameof(ticksPerQuarterNote));

            TicksPerQuarterNote = ticksPerQuarterNote;
        }

        public int TicksPerQuarterNote { get; }

        public bool HasTempoChanges => _tempoChanges.Count > 0;

        public void AddTempoChange(uint timeMs, double tempoBpm)
        {
            if (tempoBpm <= 0)
                throw new ArgumentOutOfRangeException(nameof(tempoBpm));

            _tempoChanges.RemoveAll(t => t.TimeMs == timeMs);
            _tempoChanges.Add((timeMs, tempoBpm));
            _tempoChanges.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
        }

        public int MillisecondsToTicks(uint timeMs)
        {
            if (!HasTempoChanges)
                throw new InvalidOperationException("No tempo changes defined.");

            double ticks = 0;
            uint previousTime = 0;
            double currentTempo = _tempoChanges[0].TempoBpm;

            for (int i = 0; i < _tempoChanges.Count; i++)
            {
                var change = _tempoChanges[i];
                if (change.TimeMs > timeMs)
                {
                    ticks += CalculateTicks(timeMs - previousTime, currentTempo);
                    return (int)Math.Round(ticks);
                }

                ticks += CalculateTicks(change.TimeMs - previousTime, currentTempo);
                previousTime = change.TimeMs;
                currentTempo = change.TempoBpm;
            }

            ticks += CalculateTicks(timeMs - previousTime, currentTempo);
            return (int)Math.Round(ticks);
        }

        private double CalculateTicks(uint durationMs, double tempoBpm)
        {
            double microsecondsPerBeat = 60000000.0 / tempoBpm;
            double microseconds = durationMs * 1000.0;
            return (microseconds / microsecondsPerBeat) * TicksPerQuarterNote;
        }
    }
}
