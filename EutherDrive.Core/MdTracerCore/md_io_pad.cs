using System;

namespace EutherDrive.Core.MdTracerCore;

internal enum MdPadType
{
    ThreeButton,
    SixButton
}

internal struct MdPadState
{
    public bool Up, Down, Left, Right;
    public bool A, B, C, Start;
    public bool X, Y, Z, Mode;
}

internal sealed class MdPad
{
    private const int TraceLogLimit = 64;
    private static readonly bool TracePad =
        string.Equals(Environment.GetEnvironmentVariable("EUTHERDRIVE_TRACE_PAD"), "1", StringComparison.Ordinal);
    private static readonly bool SelfTestEnabled =
        string.Equals(Environment.GetEnvironmentVariable("EUTHERDRIVE_PAD_SELFTEST"), "1", StringComparison.Ordinal);

    public MdPadType PadType { get; set; } = MdPadType.SixButton;
    public MdPadState State;

    private readonly int _port;
    private bool _thHigh = true;
    private int _thLowEdges;
    private bool _extraPhase;
    private bool _toggleThisFrame;
    private int _traceReadRemaining = TraceLogLimit;
    private int _traceToggleRemaining = TraceLogLimit;
    private long _lastSelfTestMs;

    public MdPad(int port)
    {
        _port = port;
    }

    public bool ThHigh => _thHigh;

    public void NewFrame()
    {
        if (!_toggleThisFrame)
            ResetHandshake();
        _toggleThisFrame = false;

        if (SelfTestEnabled)
            MaybeSelfTest();
    }

    public void WriteControl(byte data)
    {
        bool newThHigh = (data & 0x40) != 0;
        if (newThHigh == _thHigh)
            return;

        _thHigh = newThHigh;
        _toggleThisFrame = true;

        if (!_thHigh)
        {
            _thLowEdges++;
            if (PadType == MdPadType.SixButton && _thLowEdges >= 3)
                _extraPhase = true;
        }

        if (TracePad && _traceToggleRemaining-- > 0)
        {
            Console.WriteLine($"[PAD{_port}] TH={(newThHigh ? 1 : 0)} lowEdges={_thLowEdges} extra={(_extraPhase ? 1 : 0)}");
        }
    }

    public byte ReadData()
    {
        bool extra = PadType == MdPadType.SixButton && _extraPhase && !_thHigh;
        byte v = BuildData(_thHigh, extra);

        if (extra)
            _extraPhase = false;

        if (TracePad && _traceReadRemaining-- > 0)
        {
            Console.WriteLine($"[PAD{_port}] Read TH={(_thHigh ? 1 : 0)} extra={(extra ? 1 : 0)} data=0x{v:X2}");
        }

        return v;
    }

    private byte BuildData(bool thHigh, bool extraPhase)
    {
        byte v = 0xFF; // active-low

        if (extraPhase)
        {
            if (State.Z) v &= 0xFE;
            if (State.Y) v &= 0xFD;
            if (State.X) v &= 0xFB;
            if (State.Mode) v &= 0xF7;

            if (State.Start) v &= 0xEF;
            if (State.A) v &= 0xDF;
        }
        else
        {
            if (State.Up) v &= 0xFE;
            if (State.Down) v &= 0xFD;
            if (State.Left) v &= 0xFB;
            if (State.Right) v &= 0xF7;

            if (thHigh)
            {
                if (State.B) v &= 0xEF;
                if (State.C) v &= 0xDF;
            }
            else
            {
                if (State.Start) v &= 0xEF;
                if (State.A) v &= 0xDF;
            }
        }

        if (thHigh)
            v |= 0x40;
        else
            v &= 0xBF;

        return v;
    }

    private void ResetHandshake()
    {
        _thLowEdges = 0;
        _extraPhase = false;
    }

    private void MaybeSelfTest()
    {
        if (PadType != MdPadType.ThreeButton)
            return;

        if (!(State.A && State.B && State.C && State.Start))
            return;

        long now = Environment.TickCount64;
        if (now - _lastSelfTestMs < 1000)
            return;

        _lastSelfTestMs = now;

        byte thHigh = BuildData(true, false);
        byte thLow = BuildData(false, false);
        Console.WriteLine($"[PAD{_port}] selftest TH=1 data=0x{thHigh:X2} TH=0 data=0x{thLow:X2}");
    }
}

