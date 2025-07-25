using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class MasterClock
{
    private readonly Stopwatch _stopwatch = new Stopwatch();
    private readonly object _lock = new object();

    private double _speedRatio = 1.0;
    public bool IsPlaybackPaused { get; private set; } = false;
    private CancellationTokenSource _cancellationTokenSource;

    private long _cumulativeVirtualTime = 0;
    private long _lastActualElapsedTime = 0;

    //소수점 정밀도 버그땜에 double추가
    private double _preciseCumulativeVirtualTime = 0.0;

    //도대체왜 volitile는 long안됨??
    private long _currentVirtualTime;

    //대신 interlocked 사용
    public long CurrentVirtualTime
    {
        get => Interlocked.Read(ref _currentVirtualTime);
        private set => Interlocked.Exchange(ref _currentVirtualTime, value);
    }

    public double SpeedRatio
    {
        get { lock (_lock) return _speedRatio; }
        set { lock (_lock) { if (value > 0) _speedRatio = value; } }
    }

    public void Start()
    {
        if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested) return;

        _cancellationTokenSource = new CancellationTokenSource();
        _stopwatch.Restart();
        _lastActualElapsedTime = 0;
        _cumulativeVirtualTime = 0;
        CurrentVirtualTime = 0;

        Task.Run(() => ProcessingLoop(_cancellationTokenSource.Token));
    }

    public void Stop()
    {
        _cancellationTokenSource?.Cancel();
        _stopwatch.Stop();
    }

    public void Pause()
    {
        lock (_lock) IsPlaybackPaused = true;
    }

    public void Resume()
    {
        lock (_lock) IsPlaybackPaused = false;
    }


    public void Seek(long millisecondsToJump)
    {
        lock (_lock)
        {
            //Seek
            _preciseCumulativeVirtualTime += millisecondsToJump;

            if (_preciseCumulativeVirtualTime < 0)
            {
                _preciseCumulativeVirtualTime = 0;
            }

            _lastActualElapsedTime = _stopwatch.ElapsedMilliseconds;
            CurrentVirtualTime = (long)_preciseCumulativeVirtualTime;
        }
    }

    private void ProcessingLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            //Pause
            lock (_lock)
            {
                if (IsPlaybackPaused)
                {
                    _lastActualElapsedTime = _stopwatch.ElapsedMilliseconds;
                    Thread.Sleep(100);
                    continue;
                }
            }

            long currentActualElapsedTime = _stopwatch.ElapsedMilliseconds;
            long actualDelta = currentActualElapsedTime - _lastActualElapsedTime;

            double currentSpeedRatio;
            lock (_lock) currentSpeedRatio = _speedRatio;

            //calc as double to avoid precision issues
            double virtualDelta = actualDelta * currentSpeedRatio;
            _preciseCumulativeVirtualTime += virtualDelta;

            _lastActualElapsedTime = currentActualElapsedTime;

            CurrentVirtualTime = (long)_preciseCumulativeVirtualTime;

            Thread.Sleep(1);
        }
    }
}
