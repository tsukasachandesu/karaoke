using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

public class LinuxMIDIDevice : IMIDIDevice, IDisposable
{
    private IntPtr _sequencer;
    private int _port;
    private int _destinationClientId = -1;
    private int _destinationPortId = -1;

    public LinuxMIDIDevice()
    {
        _sequencer = IntPtr.Zero;
    }

    //open alsa midi device by index
    public bool Open(int deviceIndex) { 

        if (_sequencer != IntPtr.Zero) Close();

        if (ALSA.snd_seq_open(out _sequencer, "default", ALSA.SND_SEQ_OPEN_OUTPUT, 0) < 0)
        {
            Console.Error.WriteLine("Cannot open ALSA sequencer.");
            return false;
        }
        ALSA.snd_seq_set_client_name(_sequencer, "OKDPlayer");

        _port = ALSA.snd_seq_create_simple_port(_sequencer, "MIDI Out Port",
            ALSA.SND_SEQ_PORT_CAP_WRITE | ALSA.SND_SEQ_PORT_CAP_SUBS_READ,
            ALSA.SND_SEQ_PORT_TYPE_MIDI_GENERIC | ALSA.SND_SEQ_PORT_TYPE_APPLICATION);

        if (_port < 0)
        {
            Console.Error.WriteLine("Cannot create ALSA sequencer port.");
            Close();
            return false;
        }

        bool success = ConnectByIndex(deviceIndex);

        
        if (success)
        {
            Console.WriteLine($"Opened MIDI device {_destinationClientId}:{_destinationPortId}");
            return true;
        }

        Console.Error.WriteLine($"Cannot open midi device at '{deviceIndex}'.");
        Close();
        return false;
    }

    //close
    public void Close()
    {
        if (_sequencer != IntPtr.Zero)
        {
            if (_destinationClientId != -1)
            {
                ALSA.snd_seq_disconnect_to(_sequencer, _port, _destinationClientId, _destinationPortId);
            }
            ALSA.snd_seq_delete_simple_port(_sequencer, _port);

            ALSA.snd_seq_close(_sequencer);
            _sequencer = IntPtr.Zero;
        }
    }


    public void SendShortMsg(byte status, byte[] data)
    {
        if (_sequencer == IntPtr.Zero) return;

        // status와 data를 하나의 바이트 배열로 결합
        var message = new byte[1 + data.Length];
        message[0] = status;
        Array.Copy(data, 0, message, 1, data.Length);

        // 이 메시지를 RAW32 이벤트로 전송
        SendRawData(message);
    }

    public void SendSysEx(byte[] data)
    {
        if (_sequencer == IntPtr.Zero) return;

        var midiEvent = new ALSA.snd_seq_event_t();
        //ALSA.snd_seq_ev_clear(ref midiEvent);
        midiEvent.source.port = (byte)_port;
        midiEvent.flags = ALSA.SND_SEQ_EVENT_LENGTH_VARIABLE; 
        midiEvent.type = ALSA.snd_seq_event_type.SND_SEQ_EVENT_SYSEX;

        //copy data to unmanaged memory
        IntPtr ptr = Marshal.AllocHGlobal(data.Length);
        Marshal.Copy(data, 0, ptr, data.Length);

        midiEvent.data.ext.len = (uint)data.Length;
        midiEvent.data.ext.ptr = ptr;

        SendEvent(ref midiEvent);

        Marshal.FreeHGlobal(ptr); //free
    }


    private bool ConnectByIndex(int deviceIndex)
    {
        int currentIndex = 0;
        IntPtr clientInfoPtr = IntPtr.Zero, portInfoPtr = IntPtr.Zero;
        ALSA.snd_seq_client_info_malloc(out clientInfoPtr);
        ALSA.snd_seq_port_info_malloc(out portInfoPtr);

        try
        {
            ALSA.snd_seq_client_info_set_client(clientInfoPtr, -1);
            while (ALSA.snd_seq_query_next_client(_sequencer, clientInfoPtr) == 0)
            {
                int clientId = ALSA.snd_seq_client_info_get_client(clientInfoPtr);
                ALSA.snd_seq_port_info_set_client(portInfoPtr, clientId);
                ALSA.snd_seq_port_info_set_port(portInfoPtr, -1);

                while (ALSA.snd_seq_query_next_port(_sequencer, portInfoPtr) == 0)
                {
                    uint caps = ALSA.snd_seq_port_info_get_capability(portInfoPtr);
                    if ((caps & ALSA.SND_SEQ_PORT_CAP_WRITE) != 0 && (caps & ALSA.SND_SEQ_PORT_CAP_SUBS_WRITE) != 0)
                    {
                        if (currentIndex == deviceIndex)
                        {
                            _destinationClientId = ALSA.snd_seq_port_info_get_client(portInfoPtr);
                            _destinationPortId = ALSA.snd_seq_port_info_get_port(portInfoPtr);

                            if (ALSA.snd_seq_connect_to(_sequencer, _port, _destinationClientId, _destinationPortId) == 0)
                            {
                                return true;
                            }
                            return false;
                        }
                        currentIndex++;
                    }
                }
            }
        }
        finally
        {
            ALSA.snd_seq_client_info_free(clientInfoPtr);
            ALSA.snd_seq_port_info_free(portInfoPtr);
        }

        return false;
    }


    private void SendRawData(byte[] rawData)
    {
        if (_sequencer == IntPtr.Zero || rawData == null || rawData.Length == 0) return;

        // Split data into 12-byte chunks
        for (int offset = 0; offset < rawData.Length; offset += 12)
        {
            var midiEvent = new ALSA.snd_seq_event_t();
            //ALSA.snd_seq_ev_clear(ref midiEvent);

            midiEvent.type = ALSA.snd_seq_event_type.SND_SEQ_EVENT_USR0;
            midiEvent.source.port = (byte)_port;
            midiEvent.flags = ALSA.SND_SEQ_EVENT_LENGTH_FIXED;

            // Create a 12-byte buffer for the current chunk.
            // Any bytes past the end of rawData will remain 0.
            byte[] chunk = new byte[12];
            int chunkLength = Math.Min(12, rawData.Length - offset);
            Array.Copy(rawData, offset, chunk, 0, chunkLength);

            // Use BitConverter to safely convert the byte chunks to uints.
            // This is type-safe and avoids the previous exception.
            midiEvent.data.raw32.d0 = BitConverter.ToUInt32(chunk, 0);
            midiEvent.data.raw32.d1 = BitConverter.ToUInt32(chunk, 4);
            midiEvent.data.raw32.d2 = BitConverter.ToUInt32(chunk, 8);

            SendEvent(ref midiEvent);
        }
    }

    private void SendEvent(ref ALSA.snd_seq_event_t ev)
    {
        ev.dest.client = (byte)_destinationClientId;
        ev.dest.port = (byte)_destinationPortId;

        // Send the event and wait for it to be processed
        if (ALSA.snd_seq_event_output_direct(_sequencer, ref ev) >= 0)
        {
            ALSA.snd_seq_drain_output(_sequencer);
        }
    }


    public void Dispose()
    {
        Close();
    }
}

