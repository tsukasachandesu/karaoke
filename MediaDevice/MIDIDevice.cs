using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

public interface IMIDIDevice
{
    public bool Open(int deviceId);
    public void Close();
    public void SendShortMsg(byte status, byte[] data);
    public void SendSysEx(byte[] data);
}

public class MIDIDevice
{

    [DllImport("winmm.dll")]
    internal static extern uint midiOutGetNumDevs();
    [DllImport("winmm.dll", EntryPoint = "midiOutGetDevCapsW")]
    internal static extern uint midiOutGetDevCaps(uint uDeviceID, out WinMMMidiOutCaps lpMidiOutCaps, uint uSizeOfMidiOutCaps);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    internal struct WinMMMidiOutCaps
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

    public static List<string> GetOutputDeviceNames()
    {
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var names = new List<string>();
            uint deviceCount = midiOutGetNumDevs();
            for (uint i = 0; i < deviceCount; i++)
            {
                midiOutGetDevCaps(i, out var caps, (uint)Marshal.SizeOf<WinMMMidiOutCaps>());
                names.Add(caps.szPname);
            }
            return names;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {

            var names = new List<string>();
            IntPtr sequencer;

            //open ALSA sequencer
            if (ALSA.snd_seq_open(out sequencer, "default", ALSA.SND_SEQ_OPEN_INPUT, 0) < 0)
            {
                Console.Error.WriteLine("Cannot open ALSA sequencer");
                return names;
            }

            IntPtr clientInfoPtr = IntPtr.Zero;
            IntPtr portInfoPtr = IntPtr.Zero;
            ALSA.snd_seq_client_info_malloc(out clientInfoPtr);
            ALSA.snd_seq_port_info_malloc(out portInfoPtr);

            try
            {
                ALSA.snd_seq_client_info_set_client(clientInfoPtr, -1);
                while (ALSA.snd_seq_query_next_client(sequencer, clientInfoPtr) == 0)
                {
                    int clientId = ALSA.snd_seq_client_info_get_client(clientInfoPtr);

                    ALSA.snd_seq_port_info_set_client(portInfoPtr, clientId);
                    ALSA.snd_seq_port_info_set_port(portInfoPtr, -1);
                    while (ALSA.snd_seq_query_next_port(sequencer, portInfoPtr) == 0)
                    {
                        uint caps = ALSA.snd_seq_port_info_get_capability(portInfoPtr);

                        if ((caps & ALSA.SND_SEQ_PORT_CAP_WRITE) != 0 &&
                            (caps & ALSA.SND_SEQ_PORT_CAP_SUBS_WRITE) != 0)
                        {
                            
                            IntPtr clientNamePtr = ALSA.snd_seq_client_info_get_name(clientInfoPtr);
                            IntPtr portNamePtr = ALSA.snd_seq_port_info_get_name(portInfoPtr);
                            string clientName = Marshal.PtrToStringAnsi(clientNamePtr);
                            string portName = Marshal.PtrToStringAnsi(portNamePtr);

                            //format
                            int portId = ALSA.snd_seq_port_info_get_port(portInfoPtr);
                            names.Add($"{clientName}: {portName} ({clientId}:{portId})");
                        }
                    }
                }
            }
            finally
            {
                //free allocated resources
                ALSA.snd_seq_client_info_free(clientInfoPtr);
                ALSA.snd_seq_port_info_free(portInfoPtr);
                ALSA.snd_seq_close(sequencer);
            }

            return names;
        }

        return null;

    }
}