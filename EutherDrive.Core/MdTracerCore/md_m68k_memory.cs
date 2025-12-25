namespace EutherDrive.Core.MdTracerCore
{
    internal partial class md_m68k
    {
        // Låt den börja som null så vi ser tydligt att init behövs.
        public static byte[]? g_memory;

        private const int MemorySize = 0x1000000; // 16 MiB
        private const uint WramTableStart = 0xFF0540;
        private const uint WramTableEnd = 0xFF0580;
        private const uint WramTableSlot0 = 0xFF0556;
        private const uint WramTableSlot1 = 0xFF055A;
        private static readonly bool TraceWramAny =
            string.Equals(System.Environment.GetEnvironmentVariable("EUTHERDRIVE_TRACE_WRAM"), "1", System.StringComparison.Ordinal);
        private static readonly bool TraceWramTable =
            string.Equals(System.Environment.GetEnvironmentVariable("EUTHERDRIVE_TRACE_WRAMTABLE"), "1", System.StringComparison.Ordinal);
        private static readonly bool TraceWramTableSlots =
            string.Equals(System.Environment.GetEnvironmentVariable("EUTHERDRIVE_TRACE_WRAMSLOTS"), "1", System.StringComparison.Ordinal);
        private static int _wramAnyLogRemaining = 8;
        private static long _wramAnyTotal;
        private static bool _wramAnySummaryLogged;
        private static int _wramTableWriteLogRemaining = 16;
        private static long _wramTableWriteTotal;
        private static bool _wramTableWriteSummaryLogged;
        private static bool _wramTableWriteDumped;
        private static int _wramTableReadLogRemaining = 8;
        private static long _wramTableReadTotal;
        private static bool _wramTableReadSummaryLogged;
        private static int _wramTableSlotLogRemaining = 16;

        internal static void ResetWramTraceCounters()
        {
            _wramAnyLogRemaining = 8;
            _wramAnyTotal = 0;
            _wramAnySummaryLogged = false;
            _wramTableWriteLogRemaining = 16;
            _wramTableWriteTotal = 0;
            _wramTableWriteSummaryLogged = false;
            _wramTableWriteDumped = false;
            _wramTableReadLogRemaining = 8;
            _wramTableReadTotal = 0;
            _wramTableReadSummaryLogged = false;
            _wramTableSlotLogRemaining = 16;
        }

        /// <summary>
        /// Säkerställ att RAM/ROM-address-space är allokerad.
        /// Safe att kalla många gånger.
        /// </summary>
        public static void InitMemoryIfNeeded()
        {
            g_memory ??= new byte[MemorySize];
        }

        /// <summary>
        /// Optional: Nolla minnet snabbt (kräver att minnet finns).
        /// </summary>
        public static void ClearMemory()
        {
            InitMemoryIfNeeded();
            System.Array.Clear(g_memory!, 0, g_memory!.Length);
        }

        private static uint NormalizeAddr(uint in_address)
        {
            in_address &= 0x00FF_FFFF;
            if (in_address >= 0x00E0_0000)
                in_address = (in_address & 0x0000_FFFF) | 0x00FF_0000;
            return in_address;
        }

        //----------------------------------------------------------------
        // read
        //----------------------------------------------------------------
        public static byte read8(uint in_address)
        {
            InitMemoryIfNeeded();
            var mem = g_memory!;

            var addr = NormalizeAddr(in_address);
            byte value = mem[addr];
            RecordMemoryAccess(addr, 1, false, value);
            return value;
        }

        public static ushort read16(uint in_address)
        {
            InitMemoryIfNeeded();
            var mem = g_memory!;

            var addr = NormalizeAddr(in_address);

            byte hi = mem[addr];
            byte lo = mem[addr + 1];
            ushort value = (ushort)((hi << 8) | lo);
            RecordMemoryAccess(addr, 2, false, value);
            return value;
        }

        public static uint read32(uint in_address)
        {
            InitMemoryIfNeeded();
            var mem = g_memory!;

            var addr = NormalizeAddr(in_address);

            uint b3 = mem[addr];
            uint b2 = mem[addr + 1];
            uint b1 = mem[addr + 2];
            uint b0 = mem[addr + 3];

            uint value = (b3 << 24) | (b2 << 16) | (b1 << 8) | b0;
            if (TraceWramTable && addr >= WramTableStart && addr <= WramTableEnd)
            {
                _wramTableReadTotal++;
                if (_wramTableReadLogRemaining > 0)
                {
                    _wramTableReadLogRemaining--;
                    System.Console.WriteLine($"[WRAMTAB] R32 addr=0x{addr:X6} val=0x{value:X8} pc=0x{g_reg_PC:X6} op=0x{g_opcode:X4} total={_wramTableReadTotal}");
                    if (_wramTableReadLogRemaining == 0 && !_wramTableReadSummaryLogged)
                    {
                        _wramTableReadSummaryLogged = true;
                        System.Console.WriteLine($"[WRAMTAB] read summary total={_wramTableReadTotal} range=0x{WramTableStart:X6}-0x{WramTableEnd:X6}");
                    }
                }
            }
            RecordMemoryAccess(addr, 4, false, value);
            return value;
        }

        //----------------------------------------------------------------
        // write
        //----------------------------------------------------------------
        public static void write8(uint in_address, byte in_data)
        {
            InitMemoryIfNeeded();
            var mem = g_memory!;

            var addr = NormalizeAddr(in_address);
            mem[addr] = in_data;
            if (addr >= 0xFF0000)
                MaybeLogWramWrite(addr, in_data);
            RecordMemoryAccess(addr, 1, true, in_data);
        }

        public static void write16(uint in_address, ushort in_data)
        {
            InitMemoryIfNeeded();
            var mem = g_memory!;

            var addr = NormalizeAddr(in_address);

            mem[addr]     = (byte)(in_data >> 8);
            mem[addr + 1] = (byte)(in_data & 0x00FF);
            if (addr >= 0xFF0000)
                MaybeLogWramWrite16(addr, in_data);
            RecordMemoryAccess(addr, 2, true, in_data);
        }

        public static void write32(uint in_address, uint in_data)
        {
            InitMemoryIfNeeded();
            var mem = g_memory!;

            var addr = NormalizeAddr(in_address);

            mem[addr]     = (byte)(in_data >> 24);
            mem[addr + 1] = (byte)((in_data >> 16) & 0x00FF);
            mem[addr + 2] = (byte)((in_data >> 8) & 0x00FF);
            mem[addr + 3] = (byte)(in_data & 0x00FF);
            if (addr >= 0xFF0000)
                MaybeLogWramWrite32(addr, in_data);
            RecordMemoryAccess(addr, 4, true, in_data);
        }

        private static void MaybeLogWramWrite(uint addr, byte value)
        {
            if (TraceWramTableSlots && addr >= WramTableSlot0 && addr <= (WramTableSlot1 + 3))
            {
                if (_wramTableSlotLogRemaining > 0)
                {
                    _wramTableSlotLogRemaining--;
                    System.Console.WriteLine($"[WRAMSLOT] W8 addr=0x{addr:X6} val=0x{value:X2} pc=0x{g_reg_PC:X6} op=0x{g_opcode:X4}");
                }
            }
            if (TraceWramAny)
            {
                _wramAnyTotal++;
                if (_wramAnyLogRemaining > 0)
                {
                    _wramAnyLogRemaining--;
                    System.Console.WriteLine($"[WRAM] W8 addr=0x{addr:X6} val=0x{value:X2} total={_wramAnyTotal}");
                    if (_wramAnyLogRemaining == 0 && !_wramAnySummaryLogged)
                    {
                        _wramAnySummaryLogged = true;
                        System.Console.WriteLine($"[WRAM] summary total={_wramAnyTotal}");
                    }
                }
            }
            if (TraceWramTable && addr >= WramTableStart && addr <= WramTableEnd)
            {
                _wramTableWriteTotal++;
                if (_wramTableWriteLogRemaining > 0)
                {
                    _wramTableWriteLogRemaining--;
                    System.Console.WriteLine($"[WRAMTAB] W8 addr=0x{addr:X6} val=0x{value:X2} pc=0x{g_reg_PC:X6} op=0x{g_opcode:X4} total={_wramTableWriteTotal}");
                    if (_wramTableWriteLogRemaining == 0 && !_wramTableWriteSummaryLogged)
                    {
                        _wramTableWriteSummaryLogged = true;
                        System.Console.WriteLine($"[WRAMTAB] write summary total={_wramTableWriteTotal} range=0x{WramTableStart:X6}-0x{WramTableEnd:X6}");
                    }
                }
            }
        }

        private static void MaybeLogWramWrite16(uint addr, ushort value)
        {
            if (TraceWramTableSlots && addr >= WramTableSlot0 && addr <= (WramTableSlot1 + 3))
            {
                if (_wramTableSlotLogRemaining > 0)
                {
                    _wramTableSlotLogRemaining--;
                    System.Console.WriteLine($"[WRAMSLOT] W16 addr=0x{addr:X6} val=0x{value:X4} pc=0x{g_reg_PC:X6} op=0x{g_opcode:X4}");
                }
            }
            if (TraceWramTable && addr >= WramTableStart && addr <= WramTableEnd)
            {
                _wramTableWriteTotal++;
                if (_wramTableWriteLogRemaining > 0)
                {
                    _wramTableWriteLogRemaining--;
                    System.Console.WriteLine($"[WRAMTAB] W16 addr=0x{addr:X6} val=0x{value:X4} pc=0x{g_reg_PC:X6} op=0x{g_opcode:X4} total={_wramTableWriteTotal}");
                    if (_wramTableWriteLogRemaining == 0 && !_wramTableWriteSummaryLogged)
                    {
                        _wramTableWriteSummaryLogged = true;
                        System.Console.WriteLine($"[WRAMTAB] write summary total={_wramTableWriteTotal} range=0x{WramTableStart:X6}-0x{WramTableEnd:X6}");
                    }
                }
            }
        }

        private static void MaybeLogWramWrite32(uint addr, uint value)
        {
            if (TraceWramTableSlots && addr >= WramTableSlot0 && addr <= (WramTableSlot1 + 3))
            {
                if (_wramTableSlotLogRemaining > 0)
                {
                    _wramTableSlotLogRemaining--;
                    System.Console.WriteLine($"[WRAMSLOT] W32 addr=0x{addr:X6} val=0x{value:X8} pc=0x{g_reg_PC:X6} op=0x{g_opcode:X4}");
                }
            }
            if (TraceWramTable && addr >= WramTableStart && addr <= WramTableEnd)
            {
                _wramTableWriteTotal++;
                if (_wramTableWriteLogRemaining > 0)
                {
                    _wramTableWriteLogRemaining--;
                    System.Console.WriteLine($"[WRAMTAB] W32 addr=0x{addr:X6} val=0x{value:X8} pc=0x{g_reg_PC:X6} op=0x{g_opcode:X4} D0=0x{g_reg_data[0].l:X8} total={_wramTableWriteTotal}");
                    if (!_wramTableWriteDumped)
                    {
                        _wramTableWriteDumped = true;
                        DumpPcWindowRange(g_reg_PC, 16, 32);
                    }
                    if (_wramTableWriteLogRemaining == 0 && !_wramTableWriteSummaryLogged)
                    {
                        _wramTableWriteSummaryLogged = true;
                        System.Console.WriteLine($"[WRAMTAB] write summary total={_wramTableWriteTotal} range=0x{WramTableStart:X6}-0x{WramTableEnd:X6}");
                    }
                }
            }
        }
    }
}
