using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public static class Helpers
{
    // Note this MODIFIES THE GIVEN ARRAY then returns a reference to the modified array.
    public static byte[] Reverse(this byte[] b)
    {
        Array.Reverse(b);
        return b;
    }

    public static UInt16 ReadUInt16BE(this BinaryReader binRdr)
    {
        return BitConverter.ToUInt16(binRdr.ReadBytesRequired(sizeof(UInt16)).Reverse(), 0);
    }

    public static Int16 ReadInt16BE(this BinaryReader binRdr)
    {
        return BitConverter.ToInt16(binRdr.ReadBytesRequired(sizeof(Int16)).Reverse(), 0);
    }

    public static UInt32 ReadUInt32BE(this BinaryReader binRdr)
    {
        return BitConverter.ToUInt32(binRdr.ReadBytesRequired(sizeof(UInt32)).Reverse(), 0);
    }

    public static Int32 ReadInt32BE(this BinaryReader binRdr)
    {
        return BitConverter.ToInt32(binRdr.ReadBytesRequired(sizeof(Int32)).Reverse(), 0);
    }
    public static UInt64 ReadUInt64BE(this BinaryReader binRdr)
    {
        return BitConverter.ToUInt64(binRdr.ReadBytesRequired(sizeof(UInt64)).Reverse(), 0);
    }

    public static Int64 ReadInt64BE(this BinaryReader binRdr)
    {
        return BitConverter.ToInt64(binRdr.ReadBytesRequired(sizeof(Int64)).Reverse(), 0);
    }

    public static byte[] ReadBytesRequired(this BinaryReader binRdr, int byteCount)
    {
        var result = binRdr.ReadBytes(byteCount);

        if (result.Length != byteCount)
            throw new EndOfStreamException(string.Format("{0} bytes required from stream, but only {1} returned.", byteCount, result.Length));

        return result;
    }

    public static byte? PeekDataByte(this BinaryReader binRdr)
    {
        byte read = binRdr.ReadByte();
        binRdr.BaseStream.Seek(-1, SeekOrigin.Current);
        if ((read & 0x80) == 0x80)
        {
            return null;
        }
        return read;
    }

    public static byte ReadDataByte(this BinaryReader binRdr)
    {
        byte read = binRdr.ReadByte();
        if ((read & 0x80) == 0x80)
            throw new InvalidDataException("Invalid variable-length integer format: first byte should not have the continuation bit set.");
        return read;
    }

    public static int ReadVariableInt32(this BinaryReader reader)
    {
        int value = 0;
        for(int i = 0; i < 3; i++)
        {
            byte read = reader.ReadDataByte();
            value += read << (i * 6);
            if ((read & 0x40) != 0x40)
                return value; 
        }

        throw new EndOfStreamException($"Invalid byte sequence at offset {reader.BaseStream.Position}");

    }

    public static int ReadVariableInt32Ex(this BinaryReader reader)
    {
        int value = 0;
        while (true)
        {
            byte? b = reader.PeekDataByte();
            if (b == null)
                break;
            if (b == 0)
                return value; //EOT

            value = value + reader.ReadVariableInt32();
        }
        return value;
    }
}
