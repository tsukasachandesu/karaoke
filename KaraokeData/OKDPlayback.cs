using System.Diagnostics;

namespace OKDPlayer
{
    public class OKDPlayback
    {
        public bool IsPlaying { get; set; }
        public OKDMIDIDevice MidiDevice { get; private set; }
        public byte[] ChannelVolumes { get; private set; } = new byte[16];

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

        public OKDPlayback(OKDPTrack ptrack, OKDMIDIDevice device)
        {
            this.ptrack = ptrack ?? throw new ArgumentNullException(nameof(ptrack), "PTrack cannot be null.");
            MidiDevice = device ?? throw new ArgumentNullException(nameof(device), "MIDI device cannot be null.");

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

        public void Mute()
        {
            if (IsPlaying)
            {

            }
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
                if (timeDelta < 0)
                {
                    //reset current index to 0
                    _currentIndex = 0;
                }

                //determine if this is normal playback or forward seek
                bool isNormalPlayback = timeDelta >= 0 && timeDelta < NORMAL_PLAYBACK_THRESHOLD_MS;

                
                while (_currentIndex < this.ptrack.PTrackAbsoluteEvents.Count && this.ptrack.PTrackAbsoluteEvents[_currentIndex].AbsoluteTime <= currentVirtualTime)
                {
                    var currentEvent = this.ptrack.PTrackAbsoluteEvents[_currentIndex];

                    //play only if this is normal playback
                    if (isNormalPlayback)
                    {
                        //Play only when the event is between the last checked time and the current time
                        if (currentEvent.AbsoluteTime > lastVirtualTime)
                        {
                            //Console.WriteLine($"vT: {currentVirtualTime:D5}ms --- eveent: {currentEvent.Data}");
                            byte status = this.ptrack.PTrackAbsoluteEvents[_currentIndex].Status;
                            if (status == 0xF0)
                            {
                                //check master volume SysEx
                                if (this.ptrack.PTrackAbsoluteEvents[_currentIndex].FullSysExData.Length > 1 && this.ptrack.PTrackAbsoluteEvents[_currentIndex].FullSysExData[1] == 0x7F)
                                {
                                    //ignore master volume SysEx
                                    Console.WriteLine($"vT: {currentVirtualTime:D5}ms --- Ignore MasterVol SysEx: {BitConverter.ToString(this.ptrack.PTrackAbsoluteEvents[_currentIndex].FullSysExData)}");
                                }
                                else
                                    MidiDevice.Device.SendSysEx(this.ptrack.PTrackAbsoluteEvents[_currentIndex].FullSysExData);
                                //Console.WriteLine($"vT: {currentVirtualTime:D5}ms --- SysEx event: {BitConverter.ToString(this.ptrack.PTrackAbsoluteEvents[_currentIndex].FullSysExData)}");
                            }
                            else if (status == 0xFA) { } //TODO for adpcm
                            else if (status == 0xFD) { } //skip FD
                            else
                            {
                                MidiDevice.Device.SendShortMsg(
                                    this.ptrack.PTrackAbsoluteEvents[_currentIndex].Status,
                                    this.ptrack.PTrackAbsoluteEvents[_currentIndex].Data);
                            }

                        }
                    }

                    _currentIndex++;
                }



                //update for next loop
                lastVirtualTime = currentVirtualTime;
                if(_totalPlayTime > 0 && currentVirtualTime >= _totalPlayTime)
                    break; //break loop if playback is finished

                Thread.Sleep(1);
            }
            this.IsPlaying = false;
        }
    }
}
