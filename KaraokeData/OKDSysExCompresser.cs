using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// SysExデータ圧縮結果
/// </summary>
public enum CompressionResult
{
    SuccessNoOutput = 1,
    SuccessOutputGenerated = 2,
    Error = 3,
    InvalidMessage = 0
}

/// <summary>
/// SysExデータ圧縮クラス
/// </summary>
public class OKDSysExCompresser
{
    private class CompressionState
    {
        public byte PrevModelId;
        public byte PrevDeviceId;
        public byte PrevAddrH;
        public byte PrevAddrM;
        public byte PrevAddrL;
        public byte PrevFirstAddrL;
        public byte BufferSelector;
        public uint WorkBufferCount;
    }

    private class CompressionBuffer
    {
        public readonly byte[][] WorkBuffers = new byte[2][];
        public CompressionBuffer()
        {
            WorkBuffers[0] = new byte[256];
            WorkBuffers[1] = new byte[256];
        }
    }

    private enum ModelId : byte
    {
        None = 0,
        Type31 = 0x31, //TGMode0
        Type51 = 0x51, //TGMode1
        Type71 = 0x71  //?
    }

    private enum CommandType : byte
    {
        Type31 = 0x01,
        Type51 = 0x02,
        Type71 = 0x03
    }

    private readonly CompressionState _state;
    private readonly CompressionBuffer _buffer;

    public OKDSysExCompresser()
    {
        _state = new CompressionState();
        _buffer = new CompressionBuffer();
        InitState();
    }

    /// <summary>
    /// SysExデータを圧縮
    /// </summary>
    /// <param name="inMsg"></param>
    /// <param name="compressedData"></param>
    /// <returns></returns>
    public CompressionResult CompressMidiData(byte[] inMsg, out byte[] compressedData)
    {
        compressedData = null;

        byte exclStat = 0xFF;
        byte addrH = 0xFF;
        byte addrM = 0xFF;
        byte addrL = 0xFF;
        byte deviceId = 0xFF;
        var dataPayload = new List<byte>(256);

        byte[] currentWorkBuffer = _buffer.WorkBuffers[_state.BufferSelector];
        ModelId modelId = GetModelId(inMsg);

        if (modelId == ModelId.None)
        {
            CheckAndFlushBuffer(out compressedData);
            InitState();
            return CompressionResult.InvalidMessage;
        }

        switch (modelId)
        {
            case ModelId.Type31:
            case ModelId.Type51:
                exclStat = inMsg[0];
                deviceId = (byte)(inMsg[2] & 0x0F);
                addrH = inMsg[4];
                addrM = inMsg[5];
                addrL = inMsg[6];
                for (int i = 7; i < inMsg.Length - 2; i++)
                {
                    if ((sbyte)inMsg[i] > -1) dataPayload.Add(inMsg[i]);
                }
                break;
            case ModelId.Type71:
                exclStat = inMsg[0];
                deviceId = (byte)(inMsg[4] & 0x0F);
                addrH = inMsg[5];
                addrM = inMsg[6];
                addrL = inMsg[7];
                for (int i = 8; i < inMsg.Length - 2; i++)
                {
                    if ((sbyte)inMsg[i] > -1) dataPayload.Add(inMsg[i]);
                }
                break;
        }

        CommandType commandType;
        switch (modelId)
        {
            case ModelId.Type51: commandType = CommandType.Type51; break;
            case ModelId.Type71: commandType = CommandType.Type71; break;
            case ModelId.Type31: commandType = CommandType.Type31; break;
            default: return CompressionResult.Error;
        }

        if (addrH == 0x00 && addrM == 0x00 && addrL > 0x6F)
        {
            CheckAndFlushBuffer(out compressedData);
            _state.WorkBufferCount = 0;
            return CompressionResult.InvalidMessage;
        }

        CompressionResult result = CompressionResult.SuccessNoOutput;

        bool isSameBlock = (_state.PrevDeviceId == deviceId) &&
                           (_state.PrevModelId == (byte)modelId) &&
                           (_state.PrevAddrH == addrH) &&
                           (_state.PrevAddrM == addrM);

        if (!isSameBlock)
        {
            if (_state.WorkBufferCount > 0)
            {
                result = CompressionResult.SuccessOutputGenerated;
                SetOutputBuffer(out compressedData);

                _state.WorkBufferCount = 0;
                _state.BufferSelector = (byte)(1 - _state.BufferSelector);
                currentWorkBuffer = _buffer.WorkBuffers[_state.BufferSelector];
            }

            currentWorkBuffer[_state.WorkBufferCount++] = exclStat;
            currentWorkBuffer[_state.WorkBufferCount++] = (byte)(deviceId + (byte)commandType * 0x10);
            currentWorkBuffer[_state.WorkBufferCount++] = addrH;
            currentWorkBuffer[_state.WorkBufferCount++] = addrM;
        }

        for (int i = 0; i < dataPayload.Count; i++)
        {
            currentWorkBuffer[_state.WorkBufferCount++] = (byte)(addrL + i);
            currentWorkBuffer[_state.WorkBufferCount++] = dataPayload[i];
            _state.PrevAddrL = (byte)(addrL + i);
        }

        //現在ステータス保存
        _state.PrevAddrH = addrH;
        _state.PrevAddrM = addrM;
        _state.PrevModelId = (byte)modelId;
        _state.PrevDeviceId = deviceId;

        if (result == CompressionResult.SuccessNoOutput)
        {
            compressedData = null;
        }

        return result;
    }

    private ModelId GetModelId(byte[] inMsg)
    {
        if (inMsg.Length < 10) return ModelId.None;
        if (inMsg[1] == 0x43)
        {
            switch (inMsg[3])
            {
                case 0x51: return ModelId.Type51;
                case 0x72: return (inMsg[2] == 0x75) ? ModelId.Type71 : ModelId.None;
                case 0x31: return ModelId.Type31;
                default: return ModelId.None;
            }
        }
        return ModelId.None;
    }

    private void InitState()
    {
        _state.PrevModelId = 0xFF;
        _state.PrevDeviceId = 0xFF;
        _state.PrevAddrH = 0xFF;
        _state.PrevAddrM = 0xFF;
        _state.PrevAddrL = 0xFF;
        _state.WorkBufferCount = 0;
        _state.BufferSelector = 0;
    }

    private void SetOutputBuffer(out byte[] output)
    {
        uint finalSize = _state.WorkBufferCount + 1;
        output = new byte[finalSize];
        Array.Copy(_buffer.WorkBuffers[_state.BufferSelector], output, _state.WorkBufferCount);
        output[_state.WorkBufferCount] = 0xF7;
    }

    private void CheckAndFlushBuffer(out byte[] output)
    {
        if (_state.WorkBufferCount > 0)
        {
            SetOutputBuffer(out output);
        }
        else
        {
            output = null;
        }
    }
}