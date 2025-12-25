using System.Diagnostics;
using System.Text;
using System.Xml.Linq;

namespace EutherDrive.Core.MdTracerCore
{
    internal partial class md_vdp
    {
        private int g_dma_mode;
        private uint g_dma_src_addr;
        private int g_dma_leng;
        private bool g_dma_fill_req;
        private ushort g_dma_fill_data;

        private struct DmaAddressValue
        {
            public uint Addr;
            public ushort Val;
        }

        private class DmaTraceLog
        {
            private const int CollectLimit = 4;
            public readonly DmaAddressValue[] FirstWrites = new DmaAddressValue[CollectLimit];
            public int FirstWriteCount;
            public readonly DmaAddressValue[] FirstReads = new DmaAddressValue[CollectLimit];
            public int FirstReadCount;
            public readonly DmaAddressValue[] LastWrites = new DmaAddressValue[CollectLimit];
            public int LastWriteCount;
            public int LastWriteIndex;
            public int WriteCount;

            public void RecordWrite(uint addr, ushort val)
            {
                if (FirstWriteCount < CollectLimit)
                {
                    FirstWrites[FirstWriteCount].Addr = addr;
                    FirstWrites[FirstWriteCount].Val = val;
                    FirstWriteCount++;
                }
                LastWrites[LastWriteIndex].Addr = addr;
                LastWrites[LastWriteIndex].Val = val;
                LastWriteIndex = (LastWriteIndex + 1) % CollectLimit;
                LastWriteCount = Math.Min(CollectLimit, LastWriteCount + 1);
                WriteCount++;
            }

            public void RecordRead(uint addr, ushort val)
            {
                if (FirstReadCount < CollectLimit)
                {
                    FirstReads[FirstReadCount].Addr = addr;
                    FirstReads[FirstReadCount].Val = val;
                    FirstReadCount++;
                }
            }
        }


        public int dma_status_update()
        {
            int w_clock = 0;
            int w_tran = 0;
            if(0 < g_dma_leng)
            {
                switch (g_dma_mode)
                {
                    case 1:
                        w_tran = (g_vdp_status_3_vbrank == 0) ? 18 : 205;
                        w_clock = 488;
                        break;
                    case 2:
                        w_tran = (g_vdp_status_3_vbrank == 0) ? 17 : 204;
                        break;
                    case 3:
                        w_tran = (g_vdp_status_3_vbrank == 0) ? 9 : 102;
                        break;
                }
                g_dma_leng -= w_tran;
                if (g_dma_leng <= 0)
                {
                    g_dma_mode = 0;
                    g_dma_leng = 0;
                    g_vdp_status_1_dma = 0;
                    g_vdp_status_8_full = 0;
                    write_dma_leng();
                    switch (g_dma_mode)
                    {
                        case 1:
                            write_dma_src_addr(g_dma_src_addr >> 1);
                            break;
                        case 3:
                            write_dma_src_addr(g_dma_src_addr);
                            break;
                    }
                }
            }
            return w_clock;
        }

        private void dma_run_memory_req()
        {
            g_dma_src_addr = read_dma_src_addr() << 1;
            g_dma_leng = read_dma_leng();
            g_dma_mode = 1;
            g_vdp_status_1_dma = 1;
            g_vdp_status_8_full = 1;
            int w_loop_cnt = g_dma_leng;
            int w_loop_total = w_loop_cnt;
            var trace = new DmaTraceLog();
            LogDmaStart(trace, "memory", md_m68k.g_reg_PC, g_dma_mode, g_dma_src_addr, g_vdp_reg_dest_address, w_loop_total, g_vdp_reg_15_autoinc);
            switch (g_vdp_reg_code & 0x07)
            {
                case 1:
                    do
                    {
                        ushort w_val = md_m68k.read16(g_dma_src_addr);
                        vram_write_w(g_vdp_reg_dest_address, w_val);
                        pattern_chk(g_vdp_reg_dest_address, (byte)(w_val >> 8));
                        pattern_chk(g_vdp_reg_dest_address + 1, (byte)(w_val & 0xff));
                        trace.RecordRead(g_dma_src_addr, w_val);
                        trace.RecordWrite(g_vdp_reg_dest_address, w_val);
                        g_dma_src_addr += 2;
                        g_vdp_reg_dest_address = (ushort)(g_vdp_reg_dest_address + g_vdp_reg_15_autoinc);
                    } while (--w_loop_cnt > 0);
                    _mdVramWritesThisFrame += w_loop_total;
                    _mdVramWritesTotal += w_loop_total;
                break;
                case 3:
                    do
                    {
                        ushort w_val = md_m68k.read16(g_dma_src_addr);
                        int wcol_num = (int)((g_vdp_reg_dest_address >> 1) & 0x3f);
                        cram_set(wcol_num, w_val);
                        trace.RecordRead(g_dma_src_addr, w_val);
                        trace.RecordWrite(g_vdp_reg_dest_address, w_val);
                        g_dma_src_addr += 2;
                        g_vdp_reg_dest_address = (ushort)(g_vdp_reg_dest_address + g_vdp_reg_15_autoinc);
                    } while (--w_loop_cnt > 0);
                    _mdCramWritesThisFrame += w_loop_total;
                    _mdCramWritesTotal += w_loop_total;
                    break;
                case 5:
                    do
                    {
                        ushort w_val = md_m68k.read16(g_dma_src_addr);
                        int wcol_num = (int)((g_vdp_reg_dest_address >> 1) % 40);
                        g_vsram[wcol_num] = w_val; g_dma_src_addr += 2;
                        g_vdp_reg_dest_address = (ushort)(g_vdp_reg_dest_address + g_vdp_reg_15_autoinc);
                    } while (--w_loop_cnt > 0);
                    break;
            }
            LogDmaResult(trace, w_loop_total);

        }
        private void dma_run_fill_req(ushort in_data)
        {
            g_dma_leng = read_dma_leng();
            g_dma_fill_data = in_data;
            g_dma_mode = 2;
            g_vdp_status_1_dma = 1;
            g_vdp_status_8_full = 1;
            int w_loop_cnt = g_dma_leng;
            int w_loop_total = w_loop_cnt;
            var trace = new DmaTraceLog();
            LogDmaStart(trace, "fill", md_m68k.g_reg_PC, g_dma_mode, g_dma_src_addr, g_vdp_reg_dest_address, w_loop_total, g_vdp_reg_15_autoinc);
            switch (g_vdp_reg_code & 0x07)
            {
                case 1:
                    byte w_data = (byte)(g_dma_fill_data & 0x00ff);
                    g_vram[g_vdp_reg_dest_address] = w_data;
                    pattern_chk(g_vdp_reg_dest_address, w_data);
                    trace.RecordWrite(g_vdp_reg_dest_address, (ushort)w_data);
                    if (TraceVram)
                    {
                        _lastVramWriteAddr = (uint)(g_vdp_reg_dest_address & 0xFFFF);
                        _lastVramWriteValue = g_dma_fill_data;
                    }
                    w_data = (byte)((g_dma_fill_data >> 8) & 0x00ff);
                    do
                    {
                    g_vram[g_vdp_reg_dest_address ^ 1] = w_data;
                        pattern_chk((g_vdp_reg_dest_address ^ 1), w_data);
                        if (TraceVram)
                        {
                            _lastVramWriteAddr = (uint)((g_vdp_reg_dest_address ^ 1) & 0xFFFF);
                            _lastVramWriteValue = g_dma_fill_data;
                        }
                        trace.RecordWrite((uint)(g_vdp_reg_dest_address ^ 1), (ushort)w_data);
                        g_vdp_reg_dest_address = (ushort)(g_vdp_reg_dest_address + g_vdp_reg_15_autoinc);
                    } while (--w_loop_cnt > 0);
                    _mdVramWritesThisFrame += w_loop_total;
                    _mdVramWritesTotal += w_loop_total;
                    break;
                case 3:
                    do
                    {
                        int wcol_num = (int)((g_vdp_reg_dest_address >> 1) & 0x3f);
                        cram_set(wcol_num, g_dma_fill_data);
                        g_vdp_reg_dest_address = (ushort)((g_vdp_reg_dest_address + g_vdp_reg_15_autoinc) & 0xffff);
                    } while (--w_loop_cnt > 0);
                    _mdCramWritesThisFrame += w_loop_total;
                    _mdCramWritesTotal += w_loop_total;
                    break;
                case 5:
                    do
                    {
                        g_vsram[(g_vdp_reg_dest_address >> 1) % 40] = g_dma_fill_data;
                        g_dma_src_addr += 1;
                        g_vdp_reg_dest_address = (ushort)(g_vdp_reg_dest_address + g_vdp_reg_15_autoinc);
                    } while (--w_loop_cnt > 0);
                    break;
            }
            // immediate completion: clear DMA busy flags so the CPU can continue without polling stale status bits.
            g_dma_leng = 0;
            g_dma_mode = 0;
            g_vdp_status_1_dma = 0;
            g_vdp_status_8_full = 0;
            write_dma_leng();
            LogDmaResult(trace, w_loop_total);
        }
        private void dma_run_copy_req()
        {
            g_dma_src_addr = read_dma_src_addr() & 0xffff;
            g_dma_leng = read_dma_leng();
            g_dma_mode = 3;
            g_vdp_status_1_dma = 1;
            g_vdp_status_8_full = 1;
            int w_loop_cnt = g_dma_leng;
            int w_loop_total = w_loop_cnt;
            var trace = new DmaTraceLog();
            LogDmaStart(trace, "copy", md_m68k.g_reg_PC, g_dma_mode, g_dma_src_addr, g_vdp_reg_dest_address, w_loop_total, g_vdp_reg_15_autoinc);
            switch (g_vdp_reg_code & 0x07)
            {
                case 1:
                    do
                    {
                        byte w_val = g_vram[g_dma_src_addr];
                        g_vram[g_vdp_reg_dest_address] = w_val;
                        pattern_chk(g_vdp_reg_dest_address, w_val);
                        if (TraceVram)
                        {
                            _lastVramWriteAddr = (uint)(g_vdp_reg_dest_address & 0xFFFF);
                            _lastVramWriteValue = vram_read_w(g_vdp_reg_dest_address & 0xfffe);
                        }
                        trace.RecordRead(g_dma_src_addr, (ushort)w_val);
                        trace.RecordWrite(g_vdp_reg_dest_address, (ushort)w_val);
                        g_dma_src_addr = (g_dma_src_addr + 1) & 0xffff;
                        g_vdp_reg_dest_address = (ushort)((g_vdp_reg_dest_address + g_vdp_reg_15_autoinc) & 0xffff);
                    } while (--w_loop_cnt > 0);
                    _mdVramWritesThisFrame += w_loop_total;
                    _mdVramWritesTotal += w_loop_total;
                    break;
                case 3:
                    MessageBox.Show("md_vdp.dma_run_copy", "error");
                    break;
                case 5:
                    MessageBox.Show("md_vdp.dma_run_copy", "error");
                    break;
            }
            LogDmaResult(trace, w_loop_total);
        }
        //--------------------------------------------------
        private uint read_dma_src_addr()
        {
            return (uint)(g_vdp_reg_21_dma_source_low
                        + (g_vdp_reg_22_dma_source_mid << 8)
                        + (g_vdp_reg_23_5_dma_high << 16));
        }
        private void write_dma_src_addr(uint in_addr)
        {
            g_vdp_reg_21_dma_source_low = (byte)(in_addr & 0x00ff);
            g_vdp_reg_22_dma_source_mid = (byte)(in_addr >> 8);
            g_vdp_reg_23_5_dma_high = (byte)(in_addr >> 16);
        }
        private int read_dma_leng()
        {
            int out_ling = (g_vdp_reg_19_dma_counter_low
                    + (g_vdp_reg_20_dma_counter_high << 8));
            if (out_ling == 0) out_ling = 0xffff;
            return out_ling;
        }
        private void write_dma_leng()
        {
            g_vdp_reg_19_dma_counter_low = (byte)(g_dma_leng & 0x00ff);
            g_vdp_reg_20_dma_counter_high = (byte)(g_dma_leng >> 8);
        }

        private void LogDmaStart(DmaTraceLog trace, string modeName, uint pc, int mode, uint src, ushort dest, int length, int autoinc)
        {
            if (!MdTracerCore.MdLog.Enabled)
                return;
            string target = GetDataTargetName(g_vdp_reg_code & 0x07);
            MdTracerCore.MdLog.WriteLine(
                $"[VDPDMA] start pc=0x{pc:X6} type={modeName} mode={mode} target={target} src=0x{src:X6} dest=0x{dest:X4} len={length} autoinc={autoinc}");
        }

        private void LogDmaResult(DmaTraceLog trace, int expectedWords)
        {
            if (!MdTracerCore.MdLog.Enabled)
                return;
            string firstWrites = FormatEntries(trace.FirstWrites, trace.FirstWriteCount);
            string lastWrites = FormatCircularEntries(trace.LastWrites, trace.LastWriteCount, trace.LastWriteIndex);
            string reads = FormatEntries(trace.FirstReads, trace.FirstReadCount);
            MdTracerCore.MdLog.WriteLine(
                $"[VDPDMA] writes={trace.WriteCount}/{expectedWords} firstWrites={firstWrites} lastWrites={lastWrites} firstReads={reads}");
        }

        private static string FormatEntries(DmaAddressValue[] entries, int count)
        {
            if (count == 0)
                return "[]";
            var sb = new StringBuilder();
            sb.Append('[');
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append($"0x{entries[i].Addr:X4}=0x{entries[i].Val:X4}");
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string FormatCircularEntries(DmaAddressValue[] entries, int count, int nextIndex)
        {
            if (count == 0)
                return "[]";
            var sb = new StringBuilder();
            sb.Append('[');
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(',');
                int idx = (nextIndex + i) % entries.Length;
                sb.Append($"0x{entries[idx].Addr:X4}=0x{entries[idx].Val:X4}");
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string GetDataTargetName(int code)
        {
            return code switch
            {
                1 => "VRAM",
                3 => "CRAM",
                5 => "VSRAM",
                _ => $"code{code}",
            };
        }

        private static string GetDmaTypeName(int mode)
        {
            return mode switch
            {
                0 => "memory",
                1 => "memory",
                2 => "fill",
                3 => "copy",
                _ => $"mode{mode}",
            };
        }
    }
}
