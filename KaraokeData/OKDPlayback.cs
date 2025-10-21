using System.Diagnostics;
using System.IO;
using System.Numerics;
using ManagedBass;

namespace OKDPlayer
{
    public class PCMSampleBank : IDisposable
    {
        readonly List<int> _samples = new(); // sample handles
        readonly int _rate, _ch;

        //PCM player with bass 
        public PCMSampleBank(int sampleRate, int channels)
        {
            _rate = sampleRate; _ch = channels;
            //저지연
            Bass.Init(-1, _rate, DeviceInitFlags.Default);
            Bass.Configure(Configuration.DevNonStop, true);
            Bass.Configure(Configuration.UpdatePeriod, 5);  //5~10ms
            Bass.Configure(Configuration.DeviceBufferLength, 60); //40~90ms
        }

        public int AddPcm(byte[] pcm, int maxSimultaneous = 8)
        {
            int s = Bass.CreateSample(
                Length: pcm.Length,
                Frequency: _rate,
                Channels: _ch,
                Max: maxSimultaneous,
                Flags: BassFlags.Default
            );
            if (s == 0) 
            {
                _samples.Add(0);
                Console.WriteLine("Warning CreateSample failed: " + Bass.LastError);
                return -1;
            }

            if (!Bass.SampleSetData(s, pcm))
                throw new InvalidOperationException("SampleSetData failed: " + Bass.LastError);

            _samples.Add(s);
            return _samples.Count - 1;
        }

        //raw 메세지가 1~127이므로 0~127 스케일로 맞춤
        static double Vol127ToLinear01(int v) => Math.Clamp(v, 0, 127) / 127.0;
        static int Vol127ToGlobal10000(int v) => (int)Math.Round(Math.Clamp(v, 0, 127) * (10000.0 / 127.0));

        //감마 보정
        static double Vol127ToPerceptual01(int v, double gamma = 2.0)
        {
            double x = Math.Clamp(v, 0, 127) / 127.0;
            return Math.Pow(x, gamma); // x^gamma
        }

        //글로벌 볼륨
        public void SetDeviceVolumeFrom127(int vol127, bool perceptual = false)
        {
            double v = perceptual ? Vol127ToPerceptual01(vol127, 2.0) : Vol127ToLinear01(vol127);
            Bass.Volume = v; //0.0~1.0
        }

        //샘플 볼륨
        public void SetSampleDefaultVolumeFrom127(int sampleIndex, int vol127)
        {
            int s = _samples[sampleIndex];
            var info = Bass.SampleGetInfo(s);
            info.Volume = Vol127ToGlobal10000(vol127); //0~10000
            if (!Bass.SampleSetInfo(s, info))
                throw new InvalidOperationException("SampleSetInfo failed: " + Bass.LastError);
        }

        public void SetAllSamplesDefaultVolumeFrom127(int vol127)
        {
            foreach (var s in _samples)
            {
                var info = Bass.SampleGetInfo(s);
                info.Volume = Vol127ToGlobal10000(vol127);
                if (!Bass.SampleSetInfo(s, info))
                    throw new InvalidOperationException("SampleSetInfo failed: " + Bass.LastError);
            }
        }

        public void PlayIndex(int index, bool restart = true, double? volume01 = null)
        {
            int sample = _samples[index];
            int ch = Bass.SampleGetChannel(sample, OnlyNew: false);
            if (ch == 0) throw new InvalidOperationException("SampleGetChannel failed: " + Bass.LastError);

            if (volume01.HasValue)
                Bass.ChannelSetAttribute(ch, ChannelAttribute.Volume, Math.Clamp(volume01.Value, 0, 1));

            if (!Bass.ChannelPlay(ch, restart))
                throw new InvalidOperationException("ChannelPlay failed: " + Bass.LastError);
        }

        //볼륨 0~127 스케일
        public void PlayIndexFrom127(int index, int vol127, bool restart = true, bool perceptual = false)
        {
            int sample = _samples[index];
            int ch = Bass.SampleGetChannel(sample, OnlyNew: false);
            if (ch == 0) 
                Console.WriteLine("SampleGetChannel failed: " + Bass.LastError);

            double v01 = perceptual ? Vol127ToPerceptual01(vol127, 2.0) : Vol127ToLinear01(vol127);
            Bass.ChannelSetAttribute(ch, ChannelAttribute.Volume, v01);

            if (!Bass.ChannelPlay(ch, restart))
                Console.WriteLine("ChannelPlay failed: " + Bass.LastError);
        }

        public void Dispose()
        {
            foreach (var s in _samples) Bass.SampleFree(s);
            Bass.Free();
        }
    }
    public class OKDPlayback
    {
        public bool IsPlaying { get; set; }
        public bool HasADPCMChorus { get; private set; }
        public OKDMIDIDevice MidiDevice { get; private set; }
        public byte[] ChannelVolumes { get; private set; } = new byte[16];
        public bool IsMutedAll { get; private set; } = false;

        private OKDPTrack ptrack = null;
        private int _currentIndex;
        private Stopwatch _stopwatch;
        private MasterClock _masterClock;
        private CancellationTokenSource _cancellationTokenSource;

        private readonly object _lock = new object(); //for thread safety
        private double _speedRatio = 1.0;
        private bool _isPaused = false;
        private bool _stopRequest = false;
        private long _totalPlayTime = 0; 
        private byte _backChorusVolume = 0x7f;

        private byte[][] _pcmChorusData = null;
        private PCMSampleBank pcmChorusPlayer = null;
        private bool[] _channelMuteStatus = new bool[16];
        

        //get or set speedrate
        public double SpeedRatio
        {
            get
            {
                lock (_lock) return _speedRatio;
            }
            set
            {
                lock (_lock)
                {
                    if (value > 0) _speedRatio = value;
                }
            }
        }

        public OKDPlayback(OKDPTrack ptrack, OKDMIDIDevice device, byte[][] pcmChorusData = null, int chorusSyncMS = 0)
        {
            Array.Fill( this._channelMuteStatus, false); //initialize mute status
            this.ptrack = ptrack ?? throw new ArgumentNullException(nameof(ptrack), "PTrack cannot be null.");
            MidiDevice = device ?? throw new ArgumentNullException(nameof(device), "MIDI device cannot be null.");

            this._pcmChorusData = pcmChorusData;
            if(pcmChorusData != null)
            {
                Console.WriteLine("Setting up ADPCM Back chorus playing...");
                if (!Bass.Init(-1, 44100, DeviceInitFlags.Default))
                    Console.WriteLine("BASS init failed: " + Bass.LastError);

                //sync chorus ADPCM Note ON events
                if (chorusSyncMS > 0)
                {
                    foreach (var ev in this.ptrack.PTrackAbsoluteEvents)
                    {
                        if (ev.Status == 0xF8) //ADPCM Note ON
                        {
                            int absTime = (int)ev.AbsoluteTime;
                            if (absTime < 0)
                                absTime = 0;
                            absTime += chorusSyncMS;
                            ev.AbsoluteTime = (uint)absTime;
                        }
                    }
                    ptrack.PTrackAbsoluteEvents.Sort((a, b) => a.AbsoluteTime.CompareTo(b.AbsoluteTime));
                }

                pcmChorusPlayer = new PCMSampleBank(22050, 1);
                for (int i = 0; i < pcmChorusData.Length; i++)
                {
                    pcmChorusPlayer.AddPcm(pcmChorusData[i]);
                }
                HasADPCMChorus = true;
            }
            //calculate total play time
            _totalPlayTime = ptrack.PTrackAbsoluteEvents.Count > 0 ? ptrack.PTrackAbsoluteEvents.Last().AbsoluteTime : 0;
        }


        public void Play(MasterClock clock)
        {
            if (this.ptrack.PTrackAbsoluteEvents == null || this.ptrack.PTrackAbsoluteEvents.Count == 0) return;

            _masterClock = clock;

            //init
            _currentIndex = 0;
            _stopwatch = new Stopwatch();
            _isPaused = false;
            _stopRequest = false;
            _speedRatio = 1.0;
            IsPlaying = true;
            _cancellationTokenSource = new CancellationTokenSource();


            //thread run
            Task.Run(() => ProcessingLoop(_cancellationTokenSource.Token));
        }

        public void Stop()
        {
            _stopRequest = true;
            _cancellationTokenSource?.Cancel();
        }

        public void MuteChannel(byte channelNum)
        {
            if (channelNum < 0 || channelNum > 15)
                throw new ArgumentOutOfRangeException(nameof(channelNum), "Channel number must be between 0 and 15.");
            this._channelMuteStatus[channelNum] = true;
            MidiDevice.Device.SendShortMsg((byte)(0xB0 | channelNum), new byte[] { 123, 0 }); //All Notes Off
        }

        public void UnmuteChannel(byte channelNum)
        {
            if (channelNum < 0 || channelNum > 15)
                throw new ArgumentOutOfRangeException(nameof(channelNum), "Channel number must be between 0 and 15.");
            this._channelMuteStatus[channelNum] = false;
        }

        public void SetMuteAll(bool mute)
        {
            this.IsMutedAll = mute;
            if (mute)
            {
                for (byte ch = 0; ch < 16; ch++)
                {
                    MidiDevice.Device.SendShortMsg((byte)(0xB0 | ch), new byte[] { 123, 0 }); //All Notes Off
                }
            }
        }

        public void ToggleMuteAll()
        {
            SetMuteAll(!this.IsMutedAll);
        }

        private void ProcessingLoop(CancellationToken token)
        {
            long lastVirtualTime = _masterClock.CurrentVirtualTime;

            //for determining normal playback vs seek
            const long NORMAL_PLAYBACK_THRESHOLD_MS = 100;

            while (!token.IsCancellationRequested)
            {
                long currentVirtualTime = _masterClock.CurrentVirtualTime;
                long timeDelta = currentVirtualTime - lastVirtualTime;

                //back seek
                if (timeDelta < 0 || timeDelta >= NORMAL_PLAYBACK_THRESHOLD_MS)
                {
                    //reset current index to 0
                    if (timeDelta < 0)
                    {
                        _currentIndex = 0;
                    }

                    //In seeking mode only seek to the _currentIndex position without playing
                    //this will skip all events less than the current time
                    while (_currentIndex < this.ptrack.PTrackAbsoluteEvents.Count && this.ptrack.PTrackAbsoluteEvents[_currentIndex].AbsoluteTime < currentVirtualTime)
                    {
                        _currentIndex++;
                    }
                }
                //normal playback
                else
                {
                    while (_currentIndex < this.ptrack.PTrackAbsoluteEvents.Count && this.ptrack.PTrackAbsoluteEvents[_currentIndex].AbsoluteTime <= currentVirtualTime)
                    {
                        var currentEvent = this.ptrack.PTrackAbsoluteEvents[_currentIndex];
                        //Console.WriteLine($"vT: {currentVirtualTime:D5}ms --- eveent: {currentEvent.Data}");
                        byte status = this.ptrack.PTrackAbsoluteEvents[_currentIndex].Status;
                        byte channel = (byte)(status & 0x0F);
                        if (status == 0xF0)
                        {
                            //check master volume SysEx
                            if (this.ptrack.PTrackAbsoluteEvents[_currentIndex].FullSysExData.AsSpan().StartsWith(new byte[] { 0xF0, 0x43, 0x75, 0x72, 0x20, 0x30, 6, 4 }))
                            {
                                //ignore master volume SysEx
                                Console.WriteLine($"vT: {currentVirtualTime:D5}ms --- Ignore MasterVol SysEx: {BitConverter.ToString(this.ptrack.PTrackAbsoluteEvents[_currentIndex].FullSysExData)}");
                            }
                            else
                                MidiDevice.Device.SendSysEx(this.ptrack.PTrackAbsoluteEvents[_currentIndex].FullSysExData);
                            //Console.WriteLine($"vT: {currentVirtualTime:D5}ms --- SysEx event: {BitConverter.ToString(this.ptrack.PTrackAbsoluteEvents[_currentIndex].FullSysExData)}");
                        }
                        else if (status == 0xF8) //ADPCM Note ON
                        {
                            if (this._pcmChorusData != null)
                            {
                                Console.WriteLine($"vT: {currentVirtualTime:D5}ms --- ADPCM Note ON event (F8) received. {BitConverter.ToString(this.ptrack.PTrackAbsoluteEvents[_currentIndex].Data)}");


                                byte pcmIndex = this.ptrack.PTrackAbsoluteEvents[_currentIndex].Data[1];
                                pcmChorusPlayer.PlayIndexFrom127(pcmIndex, this._backChorusVolume);


                            }

                        }
                        else if (status == 0xFA)
                        {
                            Console.WriteLine($"vT: {currentVirtualTime:D5}ms --- ADPCM Vol set event (FA) received. {BitConverter.ToString(this.ptrack.PTrackAbsoluteEvents[_currentIndex].Data)}");


                            this._backChorusVolume = this.ptrack.PTrackAbsoluteEvents[_currentIndex].Data[0];
                        }
                        else if (status == 0xFD) { } //skip FD
                        else if ((status & 0xF0) ==  0x90 ||
                            (status & 0xF0) == 0x80)
                        {
                            //byte channel = (byte)(this.ptrack.PTrackAbsoluteEvents[_currentIndex].Status & 0x0f);
                            //Console.WriteLine("channel=" + channel + " mute=" + this._channelMuteStatus[channel]);
                            if(!IsMutedAll)
                            {
                                if (!this._channelMuteStatus[channel])
                                {
                                    MidiDevice.Device.SendShortMsg(
                                       this.ptrack.PTrackAbsoluteEvents[_currentIndex].Status,
                                       this.ptrack.PTrackAbsoluteEvents[_currentIndex].Data);
                                }
                            }
                           
                            
                        }
                        else
                        {
                            //Console.WriteLine($"vT: {currentVirtualTime:D5}ms --- Send MIDI event: status {this.ptrack.PTrackAbsoluteEvents[_currentIndex].Status:X2} {BitConverter.ToString(this.ptrack.PTrackAbsoluteEvents[_currentIndex].Data)}");
                            MidiDevice.Device.SendShortMsg(
                                this.ptrack.PTrackAbsoluteEvents[_currentIndex].Status,
                                this.ptrack.PTrackAbsoluteEvents[_currentIndex].Data);
                        }
                        _currentIndex++;
                    }
                }

                lastVirtualTime = currentVirtualTime;
                if (_totalPlayTime > 0 && currentVirtualTime >= _totalPlayTime)
                    break; //break loop if playback is finished


                Thread.Sleep(1);
            }
            this.IsPlaying = false;
            pcmChorusPlayer?.Dispose();
        }
    }
}
