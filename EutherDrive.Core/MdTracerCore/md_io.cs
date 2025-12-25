using System;

namespace EutherDrive.Core.MdTracerCore
{
    // OBS: måste vara samma "shape" som övriga md_io- partials:
    // - INTE static
    // - partial så den kan fortsätta ligga i flera filer (md_io_pad.cs osv)
    internal partial class md_io
    {
        private readonly MdPad _pad1 = new MdPad(1) { PadType = ParsePadType() };
        private readonly MdPad _pad2 = new MdPad(2) { PadType = ParsePadType() };

        // Global pekare (som md_bus.Current)
        public static md_io? Current { get; set; }

        // Om overlay / debugkod vill läsa kontroller statiskt:
        // md_io.Pad1 / md_io.Pad2
        public static MdPadState Pad1 => Current?._pad1.State ?? default;
        public static MdPadState Pad2 => Current?._pad2.State ?? default;

        // Interna states (fylls typiskt i md_io_pad.cs)
        public MdPadType Pad1Type { get => _pad1.PadType; set => _pad1.PadType = value; }
        public MdPadType Pad2Type { get => _pad2.PadType; set => _pad2.PadType = value; }
        internal MdPad Pad1Instance => _pad1;
        internal MdPad Pad2Instance => _pad2;

        // ------------------------------------------------------------
        // READ
        // ------------------------------------------------------------
        public byte read8(uint in_address)
        {
            uint addr = in_address & 0xFFFFFF;
            switch (addr)
            {
                case 0xA10000:
                    return 0x00;
                case 0xA10001:
                    return 0xA0; // Version register (NTSC, rev 0)
                case 0xA10002:
                case 0xA10003:
                    return _pad1.ReadData();
                case 0xA10004:
                case 0xA10005:
                    return _pad2.ReadData();
                case 0xA10008:
                case 0xA10009:
                    return (byte)(_pad1.ThHigh ? 0x40 : 0x00);
                case 0xA1000A:
                case 0xA1000B:
                    return (byte)(_pad2.ThHigh ? 0x40 : 0x00);
                default:
                    return 0x00;
            }
        }

        public ushort read16(uint in_address)
        {
            // Big-endian 16-bit read via två 8-bit (om du vill hålla det enkelt)
            uint addr = in_address & 0xFFFFFF;
            switch (addr)
            {
                case 0xA10002:
                case 0xA10003:
                    return (ushort)(0xFF00 | _pad1.ReadData());
                case 0xA10004:
                case 0xA10005:
                    return (ushort)(0xFF00 | _pad2.ReadData());
                case 0xA10008:
                case 0xA10009:
                    return (ushort)(0xFF00 | (_pad1.ThHigh ? 0x40 : 0x00));
                case 0xA1000A:
                case 0xA1000B:
                    return (ushort)(0xFF00 | (_pad2.ThHigh ? 0x40 : 0x00));
                default:
                {
                    byte hi = read8(in_address);
                    byte lo = read8(in_address + 1);
                    return (ushort)((hi << 8) | lo);
                }
            }
        }

        public uint read32(uint in_address)
        {
            ushort hi = read16(in_address);
            ushort lo = read16(in_address + 2);
            return ((uint)hi << 16) | lo;
        }

        // ------------------------------------------------------------
        // WRITE
        // ------------------------------------------------------------
        public void write8(uint in_address, byte in_val)
        {
            uint addr = in_address & 0xFFFFFF;
            switch (addr)
            {
                case 0xA10003:
                case 0xA10008:
                case 0xA10009:
                    _pad1.WriteControl(in_val);
                    break;
                case 0xA10005:
                case 0xA1000A:
                case 0xA1000B:
                    _pad2.WriteControl(in_val);
                    break;
                default:
                    break;
            }
        }

        public void write16(uint in_address, ushort in_val)
        {
            // Big-endian split
            write8(in_address, (byte)(in_val >> 8));
            write8(in_address + 1, (byte)(in_val & 0xFF));
        }

        public void write32(uint in_address, uint in_val)
        {
            write16(in_address, (ushort)(in_val >> 16));
            write16(in_address + 2, (ushort)(in_val & 0xFFFF));
        }

        public void NewFrame()
        {
            _pad1.NewFrame();
            _pad2.NewFrame();
        }

        private static MdPadType ParsePadType()
        {
            string? raw = Environment.GetEnvironmentVariable("EUTHERDRIVE_PAD_TYPE");
            if (string.IsNullOrWhiteSpace(raw))
                return MdPadType.SixButton;

            if (raw.Equals("3", StringComparison.OrdinalIgnoreCase) ||
                raw.Equals("three", StringComparison.OrdinalIgnoreCase) ||
                raw.Equals("threebutton", StringComparison.OrdinalIgnoreCase))
            {
                return MdPadType.ThreeButton;
            }

            return MdPadType.SixButton;
        }
    }
}
