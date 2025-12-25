using System;
using System.Diagnostics;

namespace EutherDrive.Core.MdTracerCore
{
    internal partial class md_vdp
    {
        // Småhjälp för “headless” varningar
        private static void Warn(string msg) => Debug.WriteLine($"[VDP] {msg}");

        //VDP status register
        public byte g_vdp_status_9_empl;
        public byte g_vdp_status_8_full;
        public byte g_vdp_status_7_vinterrupt;
        public byte g_vdp_status_6_sprite;
        public byte g_vdp_status_5_collision;
        public byte g_vdp_status_4_frame;
        public byte g_vdp_status_3_vbrank;
        public byte g_vdp_status_2_hbrank;
        public byte g_vdp_status_1_dma;
        public byte g_vdp_status_0_tvmode;

        //HV Counter
        public ushort g_vdp_c00008_hvcounter;
        public bool g_vdp_c00008_hvcounter_latched;

        //VDP register
        private byte[] g_vdp_reg = Array.Empty<byte>();
        public byte g_vdp_reg_0_4_hinterrupt;
        public byte g_vdp_reg_0_1_hvcounter;
        public byte g_vdp_reg_1_6_display;
        public byte g_vdp_reg_1_5_vinterrupt;
        public byte g_vdp_reg_1_4_dma;
        public byte g_vdp_reg_1_3_cellmode;
        public int  g_vdp_reg_2_scrolla;
        public int  g_vdp_reg_3_windows;
        public int  g_vdp_reg_4_scrollb;
        public int  g_vdp_reg_5_sprite;
        public byte g_vdp_reg_7_backcolor;
        public byte g_vdp_reg_10_hint;
        public byte g_vdp_reg_11_3_ext;
        public byte g_vdp_reg_11_2_vscroll;
        public byte g_vdp_reg_11_1_hscroll;
        public byte g_vdp_reg_12_7_cellmode1;
        public byte g_vdp_reg_12_3_shadow;
        public byte g_vdp_reg_12_2_interlacemode;
        public byte g_vdp_reg_12_0_cellmode2;
        public int  g_vdp_reg_13_hscroll;
        public byte g_vdp_reg_15_autoinc;
        public int  g_vdp_reg_16_5_scrollV;
        public int  g_vdp_reg_16_1_scrollH;
        public byte g_vdp_reg_17_7_windows;
        public byte g_vdp_reg_17_4_basspointer;
        public byte g_vdp_reg_18_7_windows;
        public byte g_vdp_reg_18_4_basspointer;
        public byte g_vdp_reg_19_dma_counter_low;
        public byte g_vdp_reg_20_dma_counter_high;
        public byte g_vdp_reg_21_dma_source_low;
        public byte g_vdp_reg_22_dma_source_mid;
        public byte g_vdp_reg_23_dma_mode;
        public byte g_vdp_reg_23_5_dma_high;

        private ushort build_vdp_status_word()
        {
            ushort w_out = 0;
            w_out = g_vdp_status_9_empl;
            w_out = (ushort)((w_out << 1) | g_vdp_status_8_full);
            w_out = (ushort)((w_out << 1) | g_vdp_status_7_vinterrupt);
            w_out = (ushort)((w_out << 1) | g_vdp_status_6_sprite);
            w_out = (ushort)((w_out << 1) | g_vdp_status_5_collision);
            w_out = (ushort)((w_out << 1) | g_vdp_status_4_frame);
            w_out = (ushort)((w_out << 1) | g_vdp_status_3_vbrank);
            w_out = (ushort)((w_out << 1) | g_vdp_status_2_hbrank);
            w_out = (ushort)((w_out << 1) | g_vdp_status_1_dma);
            w_out = (ushort)((w_out << 1) | g_vdp_status_0_tvmode);
            return w_out;
        }

        private ushort get_vdp_status() => build_vdp_status_word();

        internal ushort PeekVdpStatus() => build_vdp_status_word();

        internal ushort ReadStatusWord() => get_vdp_status();

        private ushort get_vdp_hvcounter()
        {
            ushort w_out = g_vdp_c00008_hvcounter;
            if (!g_vdp_c00008_hvcounter_latched)
            {
                if (g_vdp_reg_12_2_interlacemode == 0)
                {
                    w_out = (ushort)
                    ((g_scanline << 8)
                    + ((g_display_xsize
                    * (md_m68k.g_clock_total - md_m68k.g_clock_now)
                    / md_main.VDL_LINE_RENDER_MC68_CLOCK) & 0xff));
                }
                else
                {
                    w_out = (ushort)
                    (((g_scanline << 7) & 0xff00)
                    + ((g_display_xsize
                    * (md_m68k.g_clock_total - md_m68k.g_clock_now)
                    / md_main.VDL_LINE_RENDER_MC68_CLOCK) & 0x00ff));
                }
                g_vdp_c00008_hvcounter = w_out;
            }
            return w_out;
        }

        private void set_vdp_register(uint in_num, byte in_data)
        {
            g_vdp_reg[in_num] = in_data;
            switch (in_num)
            {
                case 0:
                    g_vdp_reg_0_4_hinterrupt = (byte)((in_data >> 4) & 0x01);
                    g_vdp_reg_0_1_hvcounter  = (byte)((in_data >> 1) & 0x01);
                    break;

                case 1:
                    byte prevDisplay = g_vdp_reg_1_6_display;
                    g_vdp_reg_1_6_display  = (byte)((in_data >> 6) & 0x01);
                    g_vdp_reg_1_5_vinterrupt = (byte)((in_data >> 5) & 0x01);
                    g_vdp_reg_1_4_dma      = (byte)((in_data >> 4) & 0x01);
                    g_vdp_reg_1_3_cellmode = (byte)((in_data >> 3) & 0x01);
                    if (MdTracerCore.MdLog.Enabled && prevDisplay != g_vdp_reg_1_6_display)
                    {
                        MdTracerCore.MdLog.WriteLine($"[VDP] reg1 display {prevDisplay} -> {g_vdp_reg_1_6_display} data=0x{in_data:X2}");
                    }
                    if (MdTracerCore.MdLog.Enabled && prevDisplay == 0 && g_vdp_reg_1_6_display == 1)
                    {
                        MdTracerCore.MdLog.WriteLine("[VDP] display enabled (reg1 bit6)");
                    }
                    if (g_vdp_reg_1_3_cellmode == 0)
                    {
                        g_display_ysize    = 224;
                        g_display_ycell    = 28;
                        g_vertical_line_max = 262;
                    }
                    else
                    {
                        g_display_ysize    = 240;
                        g_display_ycell    = 30;
                        g_vertical_line_max = 312;
                    }
                    LogVdpRegisterGeneral(1, in_data,
                        $"display={g_vdp_reg_1_6_display} vint={g_vdp_reg_1_5_vinterrupt} dma={g_vdp_reg_1_4_dma} cell={g_vdp_reg_1_3_cellmode}");
                    break;

                case 2:
                    g_vdp_reg_2_scrolla = (ushort)(in_data << 10);
                    LogVdpRegisterGeneral(2, in_data, $"scrollA=0x{g_vdp_reg_2_scrolla:X4}");
                    break;

                case 3:
                    if (g_vdp_reg_12_7_cellmode1 == 0)
                        g_vdp_reg_3_windows = (ushort)((in_data & 0x3e) << 10);
                else
                    g_vdp_reg_3_windows = (ushort)((in_data & 0x3c) << 10);
                    LogVdpRegisterGeneral(3, in_data, $"windowBase=0x{g_vdp_reg_3_windows:X4}");
                    break;

                case 4:
                    g_vdp_reg_4_scrollb = (ushort)(in_data << 13);
                    LogVdpRegisterGeneral(4, in_data, $"scrollB=0x{g_vdp_reg_4_scrollb:X4}");
                    break;

                case 5:
                    if (g_vdp_reg_12_7_cellmode1 == 0)
                        g_vdp_reg_5_sprite = (ushort)((in_data & 0x7f) << 9);
                else
                    g_vdp_reg_5_sprite = (ushort)((in_data & 0x7e) << 9);
                break;

                case 7:
                    g_vdp_reg_7_backcolor = (byte)(in_data & 0x3f);
                    break;

                case 10:
                    g_vdp_reg_10_hint = in_data;
                    break;

                case 11:
                    g_vdp_reg_11_3_ext     = (byte)((in_data >> 3) & 0x01);
                    g_vdp_reg_11_2_vscroll = (byte)((in_data >> 2) & 0x01);
                    g_vdp_reg_11_1_hscroll = (byte)(in_data & 0x03);
                    break;

                case 12:
                    g_vdp_reg_12_7_cellmode1     = (byte)((in_data >> 7) & 0x01);
                    g_vdp_reg_12_3_shadow        = (byte)((in_data >> 3) & 0x01);
                    g_vdp_reg_12_2_interlacemode = (byte)((in_data >> 1) & 0x03);

                    if (g_vdp_reg_12_2_interlacemode != 0)
                        Warn("Interlace-läge ej implementerat – kör ändå (kan rita fel).");

                g_sprite_vmask = (g_vdp_reg_12_2_interlacemode == 0) ? 0x1ff : 0x3ff;

                g_vdp_reg_12_0_cellmode2 = (byte)(in_data & 0x01);

                if (g_vdp_reg_12_7_cellmode1 == 0)
                {
                    g_display_xsize = 256;
                    g_display_xcell = 32;
                    g_max_sprite_num  = 64;
                    g_max_sprite_line = 16;
                    g_max_sprite_cell = 32;
                }
                else
                {
                    g_display_xsize = 320;
                    g_display_xcell = 40;
                    g_max_sprite_num  = 80;
                    g_max_sprite_line = 20;
                    g_max_sprite_cell = 40;
                }

                // Uppdatera beroende register (3/5) enligt cellmode1
                g_vdp_reg_3_windows = (ushort)(((g_vdp_reg_12_7_cellmode1 == 0 ? (g_vdp_reg[3] & 0x3e) : (g_vdp_reg[3] & 0x3c)) << 10));
                g_vdp_reg_5_sprite  = (ushort)(((g_vdp_reg_12_7_cellmode1 == 0 ? (g_vdp_reg[5] & 0x7f) : (g_vdp_reg[5] & 0x7e)) << 9));
                break;

                case 13:
                    g_vdp_reg_13_hscroll = (ushort)(in_data << 10);
                    LogVdpRegisterGeneral(13, in_data, $"hScroll=0x{g_vdp_reg_13_hscroll:X4}");
                    break;

                case 15:
                    g_vdp_reg_15_autoinc = in_data;
                    LogVdpRegisterGeneral(15, in_data, $"autoinc={g_vdp_reg_15_autoinc}");
                    break;

                case 16:
                    g_vdp_reg_16_5_scrollV = (in_data >> 4) & 0x03;
                    g_vdp_reg_16_1_scrollH = in_data & 0x03;

                    g_scroll_ycell      = 32 * (g_vdp_reg_16_5_scrollV + 1);
                    g_scroll_ysize      = g_scroll_ycell << 3;
                    g_scroll_ysize_mask = g_scroll_ysize - 1;

                    g_scroll_xcell      = 32 * (g_vdp_reg_16_1_scrollH + 1);
                    g_scroll_xsize      = g_scroll_xcell << 3;
                    g_scroll_xsize_mask = g_scroll_xsize - 1;
                    LogVdpRegisterGeneral(16, in_data,
                        $"scrollV={(g_vdp_reg_16_5_scrollV + 1)} scrollH={(g_vdp_reg_16_1_scrollH + 1)}");
                    break;

                case 17:
                {
                    int w_pos = (in_data & 0x1f) << 4;
                    if ((in_data & 0x80) == 0)
                    {
                        if (w_pos < g_display_xsize)
                        {
                            g_screenA_left_x  = w_pos;
                            g_screenA_right_x = g_display_xsize - 1;
                        }
                        else
                        {
                            g_screenA_left_x = 0;
                            g_screenA_right_x = 0;
                        }
                    }
                    else
                    {
                        if (w_pos == 0)
                        {
                            g_screenA_left_x = 0;
                            g_screenA_right_x = 0;
                        }
                        else if (w_pos < g_display_xsize)
                        {
                            g_screenA_left_x  = 0;
                            g_screenA_right_x = w_pos - 1;
                        }
                        else
                        {
                            g_screenA_left_x  = 0;
                            g_screenA_right_x = g_display_xsize - 1;
                        }
                    }
                    LogVdpRegisterGeneral(17, in_data,
                        $"windowLeft={g_screenA_left_x} windowRight={g_screenA_right_x}");
                    break;
                }

                case 18:
                {
                    int w_pos = (in_data & 0x1f) << 3;
                    if ((in_data & 0x80) == 0)
                    {
                        if (w_pos < g_display_ysize)
                        {
                            g_screenA_top_y    = w_pos;
                            g_screenA_bottom_y = g_display_ysize - 1;
                        }
                        else
                        {
                            g_screenA_top_y = 0;
                            g_screenA_bottom_y = 0;
                        }
                    }
                    else
                    {
                        if (w_pos == 0)
                        {
                            g_screenA_top_y = 0;
                            g_screenA_bottom_y = 0;
                        }
                        else if (w_pos < g_display_ysize)
                        {
                            g_screenA_top_y    = 0;
                            g_screenA_bottom_y = w_pos - 1;
                        }
                        else
                        {
                            g_screenA_top_y    = 0;
                            g_screenA_bottom_y = g_display_ysize - 1;
                        }
                    }
                    LogVdpRegisterGeneral(18, in_data,
                        $"windowTop={g_screenA_top_y} windowBottom={g_screenA_bottom_y}");
                    break;
                }

                case 19:
                    g_vdp_reg_19_dma_counter_low = in_data;
                    LogVdpRegisterGeneral(19, in_data, $"dmaLenLow=0x{g_vdp_reg_19_dma_counter_low:X2}");
                    LogDmaRegisterSnapshot("reg19 set");
                    break;

                case 20:
                    g_vdp_reg_20_dma_counter_high = in_data;
                    LogVdpRegisterGeneral(20, in_data, $"dmaLenHigh=0x{g_vdp_reg_20_dma_counter_high:X2}");
                    LogDmaRegisterSnapshot("reg20 set");
                    break;

                case 21:
                    g_vdp_reg_21_dma_source_low = in_data;
                    LogVdpRegisterGeneral(21, in_data, $"dmaSrcLow=0x{g_vdp_reg_21_dma_source_low:X2}");
                    LogDmaRegisterSnapshot("reg21 set");
                    break;

                case 22:
                    g_vdp_reg_22_dma_source_mid = in_data;
                    LogVdpRegisterGeneral(22, in_data, $"dmaSrcMid=0x{g_vdp_reg_22_dma_source_mid:X2}");
                    LogDmaRegisterSnapshot("reg22 set");
                    break;

                case 23:
                    g_vdp_reg_23_dma_mode = (byte)((in_data >> 6) & 0x03);
                    g_vdp_reg_23_5_dma_high = (byte)((in_data & 0x80) == 0 ? (in_data & 0x7f) : (in_data & 0x3f));
                    LogVdpRegisterGeneral(23, in_data,
                        $"dmaMode={g_vdp_reg_23_dma_mode} dmaHigh=0x{g_vdp_reg_23_5_dma_high:X2}");
                    LogDmaRegisterSnapshot("reg23 set");
                    break;
            }
        }

        private void LogVdpRegisterGeneral(int reg, byte data, string extra)
        {
            if (!MdTracerCore.MdLog.Enabled)
                return;
            MdTracerCore.MdLog.WriteLine($"[VDPREG] reg{reg}=0x{data:X2} {extra}");
        }

        private void LogDmaRegisterSnapshot(string context)
        {
            if (!MdTracerCore.MdLog.Enabled)
                return;
            uint src = read_dma_src_addr();
            int len = read_dma_leng();
            string type = GetDmaTypeName(g_vdp_reg_23_dma_mode);
            ushort dest = g_vdp_reg_dest_address;
            MdTracerCore.MdLog.WriteLine(
                $"[VDPREG] {context} dmaType={type} mode={g_vdp_reg_23_dma_mode} src=0x{src:X6} len={len} dest=0x{dest:X4} fill=0x{g_dma_fill_data:X4}");
        }
    }
}
