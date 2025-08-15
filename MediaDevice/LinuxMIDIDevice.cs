using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static ALSA;

public class LinuxMIDIDevice : IMIDIDevice, IDisposable
{
    private IntPtr _sequencer;
    private int _port;
    private int _destinationClientId = -1;
    private int _destinationPortId = -1;
    private const int midi_event_buffer_size = 256;
    private readonly byte[] event_buffer_output = new byte[midi_event_buffer_size];
    private IntPtr midi_event_parser_output;
    private static readonly int seq_evt_size = Marshal.SizeOf(typeof(snd_seq_event_t));
    private static readonly int seq_evt_off_source_port = (int)Marshal.OffsetOf(typeof(snd_seq_event_t), "source") + (int)Marshal.OffsetOf(typeof(snd_seq_addr_t), "port");
    private static readonly int seq_evt_off_dest_client = (int)Marshal.OffsetOf(typeof(snd_seq_event_t), "dest") + (int)Marshal.OffsetOf(typeof(snd_seq_addr_t), "client");
    private static readonly int seq_evt_off_dest_port = (int)Marshal.OffsetOf(typeof(snd_seq_event_t), "dest") + (int)Marshal.OffsetOf(typeof(snd_seq_addr_t), "port");
    private static readonly int seq_evt_off_queue = (int)Marshal.OffsetOf(typeof(snd_seq_event_t), "queue");

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

        Send(_port, message, 0, message.Length);
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

        //SendEvent(ref midiEvent);

        Marshal.FreeHGlobal(ptr); //free

        midiEvent.dest.client = (byte)_destinationClientId;
        midiEvent.dest.port = (byte)_destinationPortId;

        int bufferSize = Marshal.SizeOf(midiEvent);
        IntPtr ev = Marshal.AllocHGlobal(bufferSize);
        Marshal.StructureToPtr(midiEvent, ev, false);

        // Send the event and wait for it to be processed
        if (ALSA.snd_seq_event_output_direct(_sequencer,  ev) >= 0)
        {
            ALSA.snd_seq_drain_output(_sequencer);
        }

        Marshal.FreeHGlobal(ev);

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

    public void Send(int port, byte[] data, int index, int count)
    {
        //init
        if (midi_event_parser_output == IntPtr.Zero)
        {
            int err = ALSA.snd_midi_event_new(
                midi_event_buffer_size,
                out midi_event_parser_output
            );
            if (err < 0)
                throw new ArgumentException("snd_midi_event_new() returned " + err.ToString());
        }

        //pin
        var handle = GCHandle.Alloc(event_buffer_output, GCHandleType.Pinned);
        try
        {
            IntPtr evPtr = handle.AddrOfPinnedObject();

            for (int i = index; i < index + count; i++)
            {
                //encode byte into midi event
                int ret = ALSA.snd_midi_event_encode_byte(
                    midi_event_parser_output,
                    data[i],
                    evPtr
                );
                if (ret < 0)
                    throw new ArgumentException("snd_midi_event_encode_byte() returned " + ret.ToString());

                //send event if successfully encoded
                if (ret > 0)
                {
                    Marshal.WriteByte(evPtr, seq_evt_off_source_port, (byte)port);
                    Marshal.WriteByte(evPtr, seq_evt_off_dest_client, AddressSubscribers);
                    Marshal.WriteByte(evPtr, seq_evt_off_dest_port, AddressUnknown);
                    Marshal.WriteByte(evPtr, seq_evt_off_queue, SND_SEQ_QUEUE_DIRECT);
                    ALSA.snd_seq_event_output_direct(_sequencer, evPtr);

                    //ALSA.snd_midi_event_reset_encode(midi_event_parser_output);
                    ALSA.snd_midi_event_init(midi_event_parser_output);
                }
            }
        }
        finally
        {
            handle.Free();
        }
    }




    public void Dispose()
    {
        Close();
    }
}

