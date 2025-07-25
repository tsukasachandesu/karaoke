using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OKDPlayer
{
    public class OKDMTrackEvent
    {

    }
    public class OKDMTrack
    {
        public (uint absoluteTime, uint tempo)[] Tempos { get; set; }
        public (uint absoluteTime, uint data1, uint data2)[] TimeSignatures { get; set; }
        public (uint startTime, uint endTime)[] Hooks { get; set; }
        public (uint absoluteTime, uint value)[] VisibleGuideMelDelimiters { get; set; }
        public int TwoChorusFadeoutTime { get; set; }
        public (uint startTime, uint endTime)[] SongSection { get; set; }
        public (uint startTime, uint endTime)[] AdpcmSections { get; set; }

        public void Parse(byte[] data)
        {

        }

    }
}
