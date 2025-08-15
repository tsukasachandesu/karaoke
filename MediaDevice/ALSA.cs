using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static ALSA;

internal static class ALSA
{
    private const string LibName = "libasound.so.2";

    #region Constants
    public const int SND_SEQ_OPEN_OUTPUT = 1;
    public const int SND_SEQ_OPEN_INPUT = 2;

    public const uint SND_SEQ_PORT_CAP_READ = 1 << 0;
    public const uint SND_SEQ_PORT_CAP_WRITE = 1 << 1;
    public const uint SND_SEQ_PORT_CAP_SUBS_READ = 1 << 5;
    public const uint SND_SEQ_PORT_CAP_SUBS_WRITE = 1 << 6;

    public const uint SND_SEQ_PORT_TYPE_MIDI_GENERIC = 1 << 1;
    public const uint SND_SEQ_PORT_TYPE_APPLICATION = 1 << 20;

    public const byte SND_SEQ_EVENT_LENGTH_FIXED = 0;
    public const byte SND_SEQ_EVENT_LENGTH_VARIABLE = 1;

    public const byte SND_SEQ_QUEUE_DIRECT = 253;

    public const byte AddressUnknown = 253;
    public const byte AddressSubscribers = 254;
    public const byte AddressBroadcast = 255;

    public enum snd_seq_event_type : byte
    {
        SND_SEQ_EVENT_SYSTEM = 0,
        SND_SEQ_EVENT_RESULT = 1,
        SND_SEQ_EVENT_NOTE = 5,
        SND_SEQ_EVENT_NOTEON = 6,
        SND_SEQ_EVENT_NOTEOFF = 7,
        SND_SEQ_EVENT_KEYPRESS = 8,
        SND_SEQ_EVENT_CONTROLLER = 10,
        SND_SEQ_EVENT_PGMCHANGE = 11,
        SND_SEQ_EVENT_CHANPRESS = 12,
        SND_SEQ_EVENT_PITCHBEND = 13,
        SND_SEQ_EVENT_CONTROL14 = 14,
        SND_SEQ_EVENT_NONREGPARAM = 15,
        SND_SEQ_EVENT_REGPARAM = 16,
        SND_SEQ_EVENT_SONGPOS = 20,
        SND_SEQ_EVENT_SONGSEL = 21,
        SND_SEQ_EVENT_QFRAME = 22,
        SND_SEQ_EVENT_TIMESIGN = 23,
        SND_SEQ_EVENT_KEYSIGN = 24,
        SND_SEQ_EVENT_START = 30,
        SND_SEQ_EVENT_CONTINUE = 31,
        SND_SEQ_EVENT_STOP = 32,
        SND_SEQ_EVENT_SETPOS_TICK = 33,
        SND_SEQ_EVENT_SETPOS_TIME = 34,
        SND_SEQ_EVENT_TEMPO = 35,
        SND_SEQ_EVENT_CLOCK = 36,
        SND_SEQ_EVENT_TICK = 37,
        SND_SEQ_EVENT_QUEUE_SKEW = 38,
        SND_SEQ_EVENT_SYNC_POS = 39,
        SND_SEQ_EVENT_TUNE_REQUEST = 40,
        SND_SEQ_EVENT_RESET = 41,
        SND_SEQ_EVENT_SENSING = 42,
        SND_SEQ_EVENT_ECHO = 50,
        SND_SEQ_EVENT_OSS = 51,
        SND_SEQ_EVENT_CLIENT_START = 60,
        SND_SEQ_EVENT_CLIENT_EXIT = 61,
        SND_SEQ_EVENT_CLIENT_CHANGE = 62,
        SND_SEQ_EVENT_PORT_START = 63,
        SND_SEQ_EVENT_PORT_EXIT = 64,
        SND_SEQ_EVENT_PORT_CHANGE = 65,
        SND_SEQ_EVENT_PORT_SUBSCRIBED = 66,
        SND_SEQ_EVENT_PORT_UNSUBSCRIBED = 67,
        SND_SEQ_EVENT_USR0 = 90,
        SND_SEQ_EVENT_USR1 = 91,
        SND_SEQ_EVENT_USR2 = 92,
        SND_SEQ_EVENT_USR3 = 93,
        SND_SEQ_EVENT_USR4 = 94,
        SND_SEQ_EVENT_USR5 = 95,
        SND_SEQ_EVENT_USR6 = 96,
        SND_SEQ_EVENT_USR7 = 97,
        SND_SEQ_EVENT_USR8 = 98,
        SND_SEQ_EVENT_USR9 = 99,
        SND_SEQ_EVENT_SYSEX = 130,
        SND_SEQ_EVENT_BOUNCE = 131,
        SND_SEQ_EVENT_USR_VAR0 = 135,
        SND_SEQ_EVENT_USR_VAR1 = 136,
        SND_SEQ_EVENT_USR_VAR2 = 137,
        SND_SEQ_EVENT_USR_VAR3 = 138,
        SND_SEQ_EVENT_USR_VAR4 = 139,
        SND_SEQ_EVENT_NONE = 255
    }
    #endregion


    [StructLayout(LayoutKind.Sequential)]
    public struct snd_seq_addr_t
    {
        public byte client;
        public byte port;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct snd_seq_connect_t
    {
        public snd_seq_addr_t sender;
        public snd_seq_addr_t dest;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct snd_seq_ev_note_t
    {
        public byte channel;
        public byte note;
        public byte velocity;
        byte off_velocity;
        uint duration;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct snd_seq_ev_ctrl_t
    {
        public byte channel;
        private byte unk1, unk2, unk3;
        public uint param;
        public int value;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct snd_seq_ev_raw32_t
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public IntPtr d;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct snd_seq_ev_ext_t
    {
        public uint len;
        public IntPtr ptr;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct snd_seq_event_data
    {
        [FieldOffset(0)] public snd_seq_ev_note_t note;
        [FieldOffset(0)] public snd_seq_ev_ctrl_t control;
        [FieldOffset(0)] public snd_seq_ev_ext_t ext;
        [FieldOffset(0)] public snd_seq_ev_raw32_t raw32;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct snd_seq_real_time_t // seq_event.h (191, 16)
    {
        public uint tv_sec;
        public uint tv_nsec;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct snd_seq_timestamp_t // seq_event.h (200, 15)
    {
        [FieldOffset(0)]
        public uint tick;
        [FieldOffset(0)]
        public snd_seq_real_time_t time;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct snd_seq_ev_raw8_t // seq_event.h (247, 16)
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
        public IntPtr d;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct snd_seq_ev_queue_control_t // seq_event.h (281, 16)
    {
        public byte @queue;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public IntPtr @unused;
        public anonymous_type_1 @param;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct snd_seq_queue_skew_t // seq_event.h (275, 16)
    {
        public uint @value;
        public uint @base;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct anonymous_type_1 // seq_event.h (284, 2)
    {
        [FieldOffset(0)]
        public int @value;
        [FieldOffset(0)]
        public snd_seq_timestamp_t @time;
        [FieldOffset(0)]
        public uint @position;
        [FieldOffset(0)]
        public snd_seq_queue_skew_t @skew;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        [FieldOffset(0)]
        public IntPtr @d32;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        [FieldOffset(0)]
        public IntPtr @d8;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct snd_seq_result_t // seq_event.h (269, 16)
    {
        public int @event;
        public int @result;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct anonymous_type_3 // seq_event.h (307, 2)
    {
        [FieldOffset(0)]
        public snd_seq_ev_note_t @note;
        [FieldOffset(0)]
        public snd_seq_ev_ctrl_t @control;
        [FieldOffset(0)]
        public snd_seq_ev_raw8_t @raw8;
        [FieldOffset(0)]
        public snd_seq_ev_raw32_t @raw32;
        [FieldOffset(0)]
        public snd_seq_ev_ext_t @ext;
        [FieldOffset(0)]
        public snd_seq_ev_queue_control_t @queue;
        [FieldOffset(0)]
        public snd_seq_timestamp_t @time;
        [FieldOffset(0)]
        public snd_seq_addr_t @addr;
        [FieldOffset(0)]
        public snd_seq_connect_t @connect;
        [FieldOffset(0)]
        public snd_seq_result_t @result;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct snd_seq_event_t
    {
        public snd_seq_event_type type;
        public byte flags;
        private byte tag;
        public byte queue;
        public snd_seq_timestamp_t time;
        public snd_seq_addr_t source;
        public snd_seq_addr_t dest;
        public anonymous_type_3 @data;

        public void set_subs()
        {
            dest.client = 254; //SND_SEQ_ADDRESS_SUBSCRIBERS
        }
    }


    //dll imports
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_seq_open(out IntPtr handle, string name, int streams, int mode);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_seq_close(IntPtr handle);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_seq_set_client_name(IntPtr seq, string name);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_seq_create_simple_port(IntPtr seq, string name, uint caps, uint type);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_seq_delete_simple_port(IntPtr seq, int port);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_seq_connect_to(IntPtr seq, int our_port, int dest_client, int dest_port);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_seq_disconnect_to(IntPtr seq, int our_port, int dest_client, int dest_port);

    //[DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    //public static extern void snd_seq_ev_clear(ref snd_seq_event_t ev);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_seq_event_output_direct(IntPtr seq, IntPtr ev);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_seq_drain_output(IntPtr seq);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_seq_client_info_malloc(out IntPtr ptr);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void snd_seq_client_info_free(IntPtr ptr);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_seq_client_info_get_client(IntPtr info);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void snd_seq_client_info_set_client(IntPtr info, int client);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_seq_query_next_client(IntPtr handle, IntPtr info);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_seq_port_info_malloc(out IntPtr ptr);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void snd_seq_port_info_free(IntPtr ptr);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_seq_port_info_get_client(IntPtr info);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_seq_port_info_get_port(IntPtr info);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint snd_seq_port_info_get_capability(IntPtr info);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint snd_seq_port_info_get_type(IntPtr info);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void snd_seq_port_info_set_client(IntPtr info, int client);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void snd_seq_port_info_set_port(IntPtr info, int port);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_seq_query_next_port(IntPtr handle, IntPtr info);
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr snd_seq_client_info_get_name(IntPtr info);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr snd_seq_port_info_get_name(IntPtr info);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_midi_event_encode_byte(IntPtr dev, int c, IntPtr ev);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_midi_event_new(uint bufsize, out IntPtr rdev);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void snd_midi_event_reset_encode(IntPtr dev);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void snd_midi_event_init(IntPtr dev);

}