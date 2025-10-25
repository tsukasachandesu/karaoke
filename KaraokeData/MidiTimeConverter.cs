using System;
using System.Collections.Generic;

namespace OKDPlayer
{
    public class MidiTimeConverter
    {
        private readonly List<(uint timeMs, double tempoBpm)> _tempoChanges = new();

        public int TicksPerQuarterNote { get; }

        public MidiTimeConverter(int ticksPerQuarterNote)
        {
            if (ticksPerQuarterNote <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ticksPerQuarterNote));
            }

            TicksPerQuarterNote = ticksPerQuarterNote;
        }

        public void AddTempoChange(uint timeMs, double tempoBpm)
        {
            if (tempoBpm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tempoBpm));
            }

            _tempoChanges.Add((timeMs, tempoBpm));
            _tempoChanges.Sort((a, b) => a.timeMs.CompareTo(b.timeMs));
        }

        public int MillisecondsToTicks(uint timeMs)
        {
            if (_tempoChanges.Count == 0)
            {
                throw new InvalidOperationException("No tempo information available for conversion.");
            }

            double totalTicks = 0.0;
            uint previousChangeTime = 0;
            double currentTempo = _tempoChanges[0].tempoBpm;

            foreach (var change in _tempoChanges)
            {
                uint changeTime = change.timeMs;
                double changeTempo = change.tempoBpm;

                if (timeMs <= changeTime)
                {
                    totalTicks += CalculateTicks(timeMs - previousChangeTime, currentTempo);
                    return (int)Math.Round(totalTicks);
                }

                if (changeTime > previousChangeTime)
                {
                    totalTicks += CalculateTicks(changeTime - previousChangeTime, currentTempo);
                }

                previousChangeTime = changeTime;
                currentTempo = changeTempo;
            }

            if (timeMs > previousChangeTime)
            {
                totalTicks += CalculateTicks(timeMs - previousChangeTime, currentTempo);
            }

            return (int)Math.Round(totalTicks);
        }

        private double CalculateTicks(uint durationMs, double tempoBpm)
        {
            double microsecondsPerBeat = 60_000_000.0 / tempoBpm;
            double microseconds = durationMs * 1000.0;
            return (microseconds / microsecondsPerBeat) * TicksPerQuarterNote;
        }
    }
}
