using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;


public class WinMIDIDevice : IDisposable, IMIDIDevice
{
    private static class WinMM
    {
        //import from winmm.dll
        [DllImport("winmm.dll")]
        internal static extern uint midiOutGetNumDevs();

        [DllImport("winmm.dll")]
        internal static extern uint midiOutOpen(out IntPtr lphMidiOut, uint uDeviceID, IntPtr dwCallback, IntPtr dwInstance, uint fdwOpen);

        [DllImport("winmm.dll")]
        internal static extern uint midiOutClose(IntPtr hMidiOut);

        [DllImport("winmm.dll")]
        internal static extern uint midiOutShortMsg(IntPtr hMidiOut, uint dwMsg);

        [DllImport("winmm.dll", EntryPoint = "midiOutGetDevCapsW")]
        internal static extern uint midiOutGetDevCaps(uint uDeviceID, out MidiOutCaps lpMidiOutCaps, uint uSizeOfMidiOutCaps);

        [DllImport("winmm.dll")]
        internal static extern uint midiOutPrepareHeader(IntPtr hMidiOut, IntPtr lpMidiOutHdr, uint uSizeOfMidiOutHdr);

        [DllImport("winmm.dll")]
        internal static extern uint midiOutUnprepareHeader(IntPtr hMidiOut, IntPtr lpMidiOutHdr, uint uSizeOfMidiOutHdr);

        [DllImport("winmm.dll")]
        internal static extern uint midiOutLongMsg(IntPtr hMidiOut, IntPtr lpMidiOutHdr, uint uSizeOfMidiOutHdr);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        internal struct MidiOutCaps
        {
            public ushort wMid;
            public ushort wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szPname;
            public ushort wTechnology;
            public ushort wVoices;
            public ushort wNotes;
            public ushort wChannelMask;
            public uint dwSupport;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MidiHdr
        {
            public IntPtr lpData;
            public uint dwBufferLength;
            public uint dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags;
            public IntPtr lpNext;
            public IntPtr reserved;
            public uint dwOffset;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public IntPtr[] dwReserved;
        }

        public const uint MMSYSERR_NOERROR = 0;
        public const uint MHDR_DONE = 0x00000001;
        public const uint MHDR_PREPARED = 0x00000002;
    }

    private IntPtr _handle;
    private bool _isOpen = false;
    private bool _disposed = false;

    public bool Open(int deviceId)
    {
        if (_isOpen) Close();
        uint result = WinMM.midiOutOpen(out _handle, (uint)deviceId, IntPtr.Zero, IntPtr.Zero, 0);
        if (result == WinMM.MMSYSERR_NOERROR)
        {
            _isOpen = true;
            return true;
        }
        return false;
    }

    public void Close()
    {
        if (_isOpen)
        {
            WinMM.midiOutClose(_handle);
            _isOpen = false;
            _handle = IntPtr.Zero;
        }
    }

    public void SendShortMsg(byte status, byte[] data)
    {
        if (!_isOpen) return;
        uint message = (uint)(status | (data[0] << 8) | (data[1] << 16));
        WinMM.midiOutShortMsg(_handle, message);
    }

    public void SendSysEx(byte[] data)
    {
        if (!_isOpen || data == null || data.Length == 0) return;

        //allocate memory
        IntPtr lpData = Marshal.AllocHGlobal(data.Length);
        Marshal.Copy(data, 0, lpData, data.Length);

        var header = new WinMM.MidiHdr
        {
            lpData = lpData,
            dwBufferLength = (uint)data.Length,
            dwFlags = 0
        };
        IntPtr lpHeader = Marshal.AllocHGlobal(Marshal.SizeOf(header));
        Marshal.StructureToPtr(header, lpHeader, false);

        try
        {
            if (WinMM.midiOutPrepareHeader(_handle, lpHeader, (uint)Marshal.SizeOf<WinMM.MidiHdr>()) != WinMM.MMSYSERR_NOERROR) return;
            if (WinMM.midiOutLongMsg(_handle, lpHeader, (uint)Marshal.SizeOf<WinMM.MidiHdr>()) != WinMM.MMSYSERR_NOERROR) return;

            //wait for completion
            var h = (WinMM.MidiHdr)Marshal.PtrToStructure(lpHeader, typeof(WinMM.MidiHdr));
            while ((h.dwFlags & WinMM.MHDR_DONE) == 0)
            {
                Thread.Sleep(1);
                h = (WinMM.MidiHdr)Marshal.PtrToStructure(lpHeader, typeof(WinMM.MidiHdr));
            }
        }
        finally
        {
            //free memory
            WinMM.midiOutUnprepareHeader(_handle, lpHeader, (uint)Marshal.SizeOf<WinMM.MidiHdr>());
            Marshal.FreeHGlobal(lpData);
            Marshal.FreeHGlobal(lpHeader);
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            Close();
            _disposed = true;
        }
    }

    ~WinMIDIDevice()
    {
        Dispose(false);
    }
}