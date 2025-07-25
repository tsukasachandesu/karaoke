using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class OKDMIDIDevice
{
    public byte Port { get; set; }
    public IMIDIDevice Device { get; set; }

    public void ResetTG(byte id, byte mode)
    {
        if (mode > 1)
        {
            throw new ArgumentException("Invalid mode.");
        }

        Device.SendSysEx(new byte[] { 0xf0, 0x43, 0x10, id, 0x00, 0x00, 0x7F, mode, 0x00, 0xF7 });
    }

    public void Transpose(byte id, int key)
    {
        int clampedKey = Math.Clamp(key, -24, 24);

        //transpose value: 0x28~0x58, default 0x40
        byte transposeValue = (byte)(0x40 + clampedKey);

        byte[] transposeSysEx = new byte[] { 0xf0, 0x43, 0x10, id, 00, 00, 0x05, transposeValue, 00, 0xF7 };
        Device.SendSysEx(transposeSysEx);

    }

    public void SetTGVolume(uint volume)
    {
        //max 16383 (14bit)
        if (volume > 16383)
        {
            volume = 16383;
        }

        byte ll = (byte)(volume & 0x7F);  //lsb
        byte mm = (byte)((volume >> 7) & 0x7F); //msb

        byte[] sysexMessage = new byte[]
        {
            0xF0,
            0x7F, //Universal Real-Time ID
            0x7F, //Device ID (All)
            0x04, //Device Control
            0x01, //Master Volume
            ll,   //Volume LSB
            mm,   //Volume MSB
            0xF7
        };

        Device.SendSysEx(sysexMessage);
    }

    public void StopAllSound()
    {
        for (byte channel = 0; channel < 16; channel++)
        {
            Device.SendShortMsg((byte)(0xB0 | channel), new byte[] { 123, 0 }); //all notes off
        }
    }

    //public void d
}