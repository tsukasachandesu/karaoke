using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

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
    public enum snd_seq_event_type : byte
    {
        SND_SEQ_EVENT_NOTEON = 6,
        SND_SEQ_EVENT_NOTEOFF = 7,
        SND_SEQ_EVENT_CONTROLLER = 11,
        SND_SEQ_EVENT_SYSEX = 30,
        SND_SEQ_EVENT_USR0 = 90,
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
        //seems not work
        public uint d0;
        public uint d1;
        public uint d2;
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
    public struct snd_seq_event_t
    {
        public snd_seq_event_type type;
        public byte flags;
        private byte tag;
        public byte queue;
        private uint time_stuff1;
        private uint time_stuff2;
        public snd_seq_addr_t source;
        public snd_seq_addr_t dest;
        public snd_seq_event_data data;

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
    public static extern int snd_seq_event_output_direct(IntPtr seq, ref snd_seq_event_t ev);

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
}