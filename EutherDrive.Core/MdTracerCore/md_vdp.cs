using System;

namespace EutherDrive.Core.MdTracerCore
{
    //----------------------------------------------------------------
    // VDP : chips:315-5313
    //----------------------------------------------------------------
    internal partial class md_vdp
    {
        internal const ushort VDP_STATUS_VBLANK_MASK = 0x0080;
        internal const ushort VDP_STATUS_DMA_MASK = 0x0002;
        private static readonly bool TraceMdVdp =
            string.Equals(Environment.GetEnvironmentVariable("EUTHERDRIVE_TRACE_MD_VDP"), "1", StringComparison.Ordinal);
        public int g_scanline;
        private int g_hinterrupt_counter;
        private bool _smsCommandPending;
        private byte _smsCommandLow;
        private int _smsVdpCode;
        private int _smsVdpAddr;
        private byte[] _smsVram = new byte[0x4000];
        private byte[] _smsRegs = new byte[16];
        private int _smsCommandLogCount;
        private const int SmsCommandLogLimit = 200;
        private bool _smsDisplayOnLogged;
        private bool _smsDataIgnoredLogged;
        private bool _smsCramWriteLogged;
        private bool _smsFirstLineRendered;
        private int _smsFrameHashCounter;
        private uint _smsLastFrameHash;
        private long _smsVramWritesTotal;
        private long _smsCramWritesTotal;
        private long _smsVramWritesAtLastSummary;
        private long _smsCramWritesAtLastSummary;
        private int _smsBeWritesThisFrame;
        private int _smsBeWritesLastFrame;
        private int _smsBfWritesThisFrame;
        private int _smsBfWritesLastFrame;
        private long _frameCounter;
        private int _mdCtrlWritesThisFrame;
        private int _mdDataWritesThisFrame;
        private int _mdVramWritesThisFrame;
        private int _mdCramWritesThisFrame;
        private long _mdCtrlWritesTotal;
        private long _mdDataWritesTotal;
        private long _mdVramWritesTotal;
        private long _mdCramWritesTotal;
        private int _mdNoWriteFrames;
        private bool _mdDataPortLogged;
        private bool _mdCtrlPortLogged;
        private bool _vblankActive;
        private bool _forceVBlankLogged;
        private long _lastForcedVBlankFrame = -1;
        private bool _forceMdVBlankLogged;
        private long _lastForcedMdVBlankFrame = -1;
        private long _lastTriggerVBlankLogFrame = -1;
        private uint _lastVramWriteAddr = 0xFFFFFFFF;
        private ushort _lastVramWriteValue;
        private static readonly bool TraceVdpTiming =
            string.Equals(Environment.GetEnvironmentVariable("EUTHERDRIVE_TRACE_VDP_TIMING"), "1", StringComparison.Ordinal);
        private static readonly bool TraceVdpFb =
            string.Equals(Environment.GetEnvironmentVariable("EUTHERDRIVE_TRACE_VDP_FB"), "1", StringComparison.Ordinal);
        private static readonly bool TraceVram =
            string.Equals(Environment.GetEnvironmentVariable("EUTHERDRIVE_TRACE_VRAM"), "1", StringComparison.Ordinal);
        private static readonly bool TraceCram =
            string.Equals(Environment.GetEnvironmentVariable("EUTHERDRIVE_TRACE_CRAM"), "1", StringComparison.Ordinal);
        private const int CramLogLimit = 32;
        private int _cramLogCount;
        private bool _cramZeroLogged;
        private int _cramNonZeroCount;
        private int _cramZeroWriteLogCount;
        private bool _cramLastNonZeroValid;
        private int _cramLastNonZeroIdx;
        private ushort _cramLastNonZeroValue;
        private uint _cramLastNonZeroPc;
        private ushort _cramLastNonZeroOp;
        private static readonly System.Diagnostics.Stopwatch _timingStopwatch = System.Diagnostics.Stopwatch.StartNew();
        private long _lastTimingLogFrame = -1;
        private long _lastTimingLogMs;
        private bool _vblankRiseLogged;
        private bool _vblankFallLogged;
        private static readonly bool ForceSmsVBlank =
            string.Equals(Environment.GetEnvironmentVariable("EUTHERDRIVE_FORCE_SMS_VBLANK"), "1", StringComparison.Ordinal);
        private static readonly bool ForceMdVBlank =
            string.Equals(Environment.GetEnvironmentVariable("EUTHERDRIVE_FORCE_MD_VBLANK"), "1", StringComparison.Ordinal);

        // --- UI-agnostiska inparametrar (matas från Avalonia) ---
        public bool MouseClickInterrupt { get; set; }
        public int MouseClickPosX { get; set; }
        public int MouseClickPosY { get; set; }

        // --- Minimal framebuffer (RGBA32) för Avalonia ---
        private const int MaxFrameWidth = 320;
        private const int MaxFrameHeight = 240;
        public int FrameWidth  { get; private set; } = 320;  // MD standard 320x224 (NTSC)
        public int FrameHeight { get; private set; } = 224;
        public int Pitch => FrameWidth * 4;                  // bytes per rad
        public byte[] RgbaFrame { get; private set; } = Array.Empty<byte>();

        public md_vdp()
        {
            initialize();
            SyncFrameSizeFromVdp();
            dx_rendering_initialize(); // no-op stub just nu
        }

        private void SyncFrameSizeFromVdp()
        {
            FrameWidth = g_display_xsize;
            FrameHeight = g_display_ysize;
            EnsureFrameBuffer();
        }

        private void EnsureFrameBuffer()
        {
            if (RgbaFrame.Length != 0)
                return;

            int allocW = Math.Max(FrameWidth, MaxFrameWidth);
            int allocH = Math.Max(FrameHeight, MaxFrameHeight);
            RgbaFrame = new byte[allocW * allocH * 4];
        }

        internal long FrameCounter => _frameCounter;

        /// <summary>Byt upplösning (valfritt att kalla om du vill synka till VDP-registret senare).</summary>
        public void SetFrameSize(int width, int height)
        {
            if (width <= 0 || height <= 0) return;
            FrameWidth = width;
            FrameHeight = height;
            EnsureFrameBuffer();
        }

        public void run(int in_vline)
        {

            g_scanline = in_vline;

            if (g_scanline == 0)
            {
                _smsBeWritesThisFrame = 0;
                _smsBfWritesThisFrame = 0;
                ClearVBlank();
                rendering_line();
                set_hinterrupt();
                interrupt_check();
            }
            else if (g_scanline < g_display_ysize)   // g_display_ysize finns i dina VDP-filer
            {
                rendering_line();
                interrupt_check();
            }
            else if (g_scanline == g_display_ysize)
            {
                _smsBeWritesLastFrame = _smsBeWritesThisFrame;
                _smsBfWritesLastFrame = _smsBfWritesThisFrame;
                rendering_frame();
                interrupt_check();

                TriggerVBlank();
            }
            else if (g_scanline == g_vertical_line_max - 1) // också definierad i VDP
            {
            // Keep VBlank flag until the next frame even when clearing per-line stats.
            g_vdp_status_4_frame = (byte)((g_vdp_status_4_frame == 0) ? 1 : 0);
                g_vdp_status_5_collision = 0;
                g_vdp_status_6_sprite = 0;
            }
        }

        private void set_hvcounter()
        {
            if (g_vdp_reg_12_2_interlacemode == 0)
            {
                g_vdp_c00008_hvcounter = (ushort)(((MouseClickPosX >> 1) & 0x00ff)
                + (MouseClickPosY << 8));
            }
            else
            {
                g_vdp_c00008_hvcounter = (ushort)(((MouseClickPosX >> 1) & 0x00ff)
                + ((MouseClickPosY << 8) & 0xfe00)
                + (MouseClickPosY & 0x0100));
            }
        }

        private static void DrawCrosshairArgb(int cx, int cy, byte r, byte g, byte b)
        {
            uint c = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
            DrawRectArgb(cx - 4, cy - 1, 9, 3, c);
            DrawRectArgb(cx - 1, cy - 4, 3, 9, c);
        }

        private static void DrawRectArgb(int x0, int y0, int w, int h, uint argb)
        {
            int x1 = Math.Min(Width, x0 + w);
            int y1 = Math.Min(Height, y0 + h);
            x0 = Math.Max(0, x0);
            y0 = Math.Max(0, y0);

            for (int y = y0; y < y1; y++)
            {
                int row = y * Width;
                for (int x = x0; x < x1; x++)
                    _frame[row + x] = argb;
            }
        }


        private void set_hinterrupt()
        {
            g_hinterrupt_counter = g_vdp_reg_10_hint;
        }

        private void interrupt_check()
        {
            // H-INT
            g_hinterrupt_counter -= 1;
            if (g_hinterrupt_counter < 0)
            {
                md_m68k.g_interrupt_H_req = true;
                set_hinterrupt();
            }

            // EXT (mus-klick)
            if (MouseClickInterrupt)
            {
                if (g_vdp_reg_11_3_ext == 1)
                {
                    md_m68k.g_interrupt_EXT_req = true;

                    if ((g_vdp_reg_0_1_hvcounter == 1) && (g_vdp_c00008_hvcounter_latched == false))
                    {
                        set_hvcounter();
                        g_vdp_c00008_hvcounter_latched = true;
                    }
                    else
                    {
                        g_vdp_c00008_hvcounter_latched = false;
                    }
                }
                else
                {
                    MouseClickInterrupt = false;
                }
            }
        }

        // --------- Render init ----------
        private void dx_rendering_initialize() { /* no-op i headless */ }

        private void UpdateRgbaFrameFromGameScreen()
        {
            SyncFrameSizeFromVdp();
            int pixels = FrameWidth * FrameHeight;
            if (g_game_screen == null || g_game_screen.Length < pixels)
                return;

            int di = 0;
            for (int i = 0; i < pixels; i++)
            {
                uint argb = g_game_screen[i];
                RgbaFrame[di + 0] = (byte)(argb >> 16); // R
                RgbaFrame[di + 1] = (byte)(argb >> 8);  // G
                RgbaFrame[di + 2] = (byte)argb;         // B
                RgbaFrame[di + 3] = (byte)(argb >> 24); // A
                di += 4;
            }
        }

        private void MaybeLogVdpFramebuffer()
        {
            if (!TraceVdpFb || !MdTracerCore.MdLog.Enabled)
                return;

            if ((_frameCounter % 60) != 0)
                return;

            ushort c0 = 0, c1 = 0, c2 = 0, c3 = 0;
            if (g_cram != null && g_cram.Length >= 4)
            {
                c0 = g_cram[0];
                c1 = g_cram[1];
                c2 = g_cram[2];
                c3 = g_cram[3];
            }

            uint p0 = 0, p1 = 0, p2 = 0, p3 = 0;
            if (g_game_screen != null && g_game_screen.Length >= 4)
            {
                p0 = g_game_screen[0];
                p1 = g_game_screen[1];
                p2 = g_game_screen[2];
                p3 = g_game_screen[3];
            }

            byte r0 = 0, g0 = 0, b0 = 0, a0 = 0;
            if (RgbaFrame != null && RgbaFrame.Length >= 4)
            {
                r0 = RgbaFrame[0];
                g0 = RgbaFrame[1];
                b0 = RgbaFrame[2];
                a0 = RgbaFrame[3];
            }

            int nonBlackSamples = 0;
            int sampleCount = 0;
            if (g_game_screen != null && g_game_screen.Length > 0)
            {
                int step = Math.Max(1, g_game_screen.Length / 1024);
                for (int i = 0; i < g_game_screen.Length; i += step)
                {
                    uint p = g_game_screen[i];
                    if (p != 0 && p != 0xFF000000)
                        nonBlackSamples++;
                    sampleCount++;
                }
            }
            int cmapNonZero = 0;
            int cmapSamples = 0;
            if (g_game_cmap != null && g_game_cmap.Length > 0)
            {
                int step = Math.Max(1, g_game_cmap.Length / 1024);
                for (int i = 0; i < g_game_cmap.Length; i += step)
                {
                    if (g_game_cmap[i] != 0)
                        cmapNonZero++;
                    cmapSamples++;
                }
            }

            MdTracerCore.MdLog.WriteLine(
                $"[VDPFB] frame={_frameCounter} cram=0x{c0:X4},0x{c1:X4},0x{c2:X4},0x{c3:X4} " +
                $"screen=0x{p0:X8},0x{p1:X8},0x{p2:X8},0x{p3:X8} " +
                $"rgba0={r0:X2}{g0:X2}{b0:X2}{a0:X2} back=0x{g_vdp_reg_7_backcolor:X2}");
            MdTracerCore.MdLog.WriteLine(
                $"[VDPFB] samples nonBlack={nonBlackSamples} total={sampleCount} cmapNonZero={cmapNonZero} cmapTotal={cmapSamples}");
            ushort a0w = VramPeekWord(g_vdp_reg_2_scrolla);
            ushort b0w = VramPeekWord(g_vdp_reg_4_scrollb);
            ushort w0 = VramPeekWord(g_vdp_reg_3_windows);
            ushort h0 = VramPeekWord(g_vdp_reg_13_hscroll);
            MdTracerCore.MdLog.WriteLine(
                $"[VDPREG] reg2=0x{g_vdp_reg_2_scrolla:X4} wA0=0x{a0w:X4} " +
                $"reg4=0x{g_vdp_reg_4_scrollb:X4} wB0=0x{b0w:X4} " +
                $"reg3=0x{g_vdp_reg_3_windows:X4} wW0=0x{w0:X4} " +
                $"reg13=0x{g_vdp_reg_13_hscroll:X4} wH0=0x{h0:X4} " +
                $"reg11=0x{g_vdp_reg_11_1_hscroll:X2} reg16=V{g_vdp_reg_16_5_scrollV} H{g_vdp_reg_16_1_scrollH}");
        }

        private ushort VramPeekWord(int addr)
        {
            if (g_vram == null || g_vram.Length == 0)
                return 0;
            int a = addr & 0xFFFF;
            int b = (a + 1) & 0xFFFF;
            return (ushort)((g_vram[a] << 8) | g_vram[b]);
        }

        private void MaybeLogVramState()
        {
            if (!TraceVram || !MdTracerCore.MdLog.Enabled)
                return;

            if ((_frameCounter % 60) != 0)
                return;

            uint addr = _lastVramWriteAddr & 0xFFFF;
            int raddr = (int)((addr >> 1) & (VRAM_DATASIZE - 1));
            uint v0 = 0, v1 = 0, v2 = 0, v3 = 0;
            uint rv0 = 0, rv1 = 0, rv2 = 0, rv3 = 0;

            if (g_vram != null && g_vram.Length >= 0x62)
            {
                v0 = (uint)((g_vram[0x00] << 8) | g_vram[0x01]);
                v1 = (uint)((g_vram[0x20] << 8) | g_vram[0x21]);
                v2 = (uint)((g_vram[0x40] << 8) | g_vram[0x41]);
                v3 = (uint)((g_vram[0x60] << 8) | g_vram[0x61]);
            }
            if (g_renderer_vram != null && g_renderer_vram.Length > 0)
            {
                int max = g_renderer_vram.Length - 1;
                rv0 = g_renderer_vram[Math.Min(raddr, max)];
                rv1 = g_renderer_vram[Math.Min(raddr + 1, max)];
                rv2 = g_renderer_vram[Math.Min(raddr + 2, max)];
                rv3 = g_renderer_vram[Math.Min(raddr + 3, max)];
            }

            MdTracerCore.MdLog.WriteLine(
                $"[VRAM] frame={_frameCounter} last=0x{addr:X4} val=0x{_lastVramWriteValue:X4} " +
                $"vram=0x{v0:X4},0x{v1:X4},0x{v2:X4},0x{v3:X4} " +
                $"rend=0x{rv0:X8},0x{rv1:X8},0x{rv2:X8},0x{rv3:X8}");
        }

        private void SmsLogFrameHash()
        {
            if (!md_main.g_masterSystemMode || !MdTracerCore.MdLog.Enabled)
                return;

            _smsFrameHashCounter++;
            if ((_smsFrameHashCounter % 60) != 0)
                return;

            if (g_game_screen == null || g_game_screen.Length == 0)
                return;

            int step = Math.Max(1, g_game_screen.Length / 64);
            uint hash = 0;
            for (int i = 0; i < g_game_screen.Length; i += step)
            {
                hash ^= g_game_screen[i];
                hash = (hash << 3) | (hash >> 29);
            }

            if (hash != _smsLastFrameHash)
            {
                _smsLastFrameHash = hash;
                MdTracerCore.MdLog.WriteLine($"[SMS VDP] framebuffer hash=0x{hash:X8}");
            }

            SmsLogFrameSummary(hash);
        }

        private void SmsLogFrameSummary(uint hash)
        {
            ushort pc = md_main.g_md_z80?.DebugPc ?? 0;
            ushort bc = md_main.g_md_z80?.DebugBc ?? 0;
            long vramDelta = _smsVramWritesTotal - _smsVramWritesAtLastSummary;
            long cramDelta = _smsCramWritesTotal - _smsCramWritesAtLastSummary;
            _smsVramWritesAtLastSummary = _smsVramWritesTotal;
            _smsCramWritesAtLastSummary = _smsCramWritesTotal;
            bool displayEnabled = g_vdp_reg_1_6_display != 0;
            bool pcLeftBootRangeSinceLastSummary = md_main.g_md_z80?.ConsumeBootRangeExitFlag() ?? false;

            MdTracerCore.MdLog.WriteLine(
                $"[SMS VDP] summary PC=0x{pc:X4} pcLeftBootRangeSinceLastSummary={(pcLeftBootRangeSinceLastSummary ? 1 : 0)} BC=0x{bc:X4} smsVdpCode={_smsVdpCode} vramWritesDelta={vramDelta} cramWritesDelta={cramDelta} fbHash=0x{hash:X8} reg1.display={(displayEnabled ? 1 : 0)} BEWritesPerFrame={_smsBeWritesLastFrame} BFWritesPerFrame={_smsBfWritesLastFrame}");
        }

        internal void RecordSmsBeWrite()
        {
            _smsBeWritesThisFrame++;
        }

        internal void RecordSmsBfWrite()
        {
            _smsBfWritesThisFrame++;
        }

        private void TriggerVBlank()
        {
            if (_vblankActive)
                return;

            _vblankActive = true;
            g_vdp_status_3_vbrank = 1;
            if (g_vdp_status_7_vinterrupt == 0)
            {
                g_vdp_status_7_vinterrupt = 1;
                md_m68k.g_interrupt_V_req = true;
                md_main.g_md_z80?.irq_request(true);
            }
            LogTriggerVBlank();
            LogVBlankEdge(1);
        }

        private void ClearVBlank()
        {
            if (!_vblankActive)
                return;

            _vblankActive = false;
            g_vdp_status_3_vbrank = 0;
            g_vdp_status_7_vinterrupt = 0;
            md_m68k.g_interrupt_V_req = false;
            md_main.g_md_z80?.irq_request(false);
            LogVBlankEdge(0);
        }

        private void ForceVBlankForTest()
        {
            if (!ForceSmsVBlank)
                return;

            if (_lastForcedVBlankFrame == _frameCounter)
                return;

            g_vdp_status_7_vinterrupt = 1;

            if (!_forceVBlankLogged)
            {
                _forceVBlankLogged = true;
                MdTracerCore.MdLog.WriteLine("[SMS VDP] forced VBlank/status7 for test");
            }

            _lastForcedVBlankFrame = _frameCounter;
        }

        private void ForceMdVBlankForTest()
        {
            if (!ForceMdVBlank || md_main.g_masterSystemMode)
                return;

            if (_lastForcedMdVBlankFrame == _frameCounter)
                return;

            TriggerVBlank();

            if (!_forceMdVBlankLogged)
            {
                _forceMdVBlankLogged = true;
                MdTracerCore.MdLog.WriteLine("[MD VDP] forced VBlank/IRQ6 for test");
            }

            _lastForcedMdVBlankFrame = _frameCounter;
            LogForcedVBlank();
        }

        private void LogTriggerVBlank()
        {
            if (!TraceMdVdp)
                return;
            if (_lastTriggerVBlankLogFrame == _frameCounter)
                return;
            _lastTriggerVBlankLogFrame = _frameCounter;
            bool irq = md_m68k.g_interrupt_V_req;
            Console.WriteLine($"[VDP] TriggerVBlank frame={_frameCounter} line={g_scanline} setVBlankFlag=1 irq6Requested={(irq ? 1 : 0)}");
        }

        private void LogVBlankEdge(int state)
        {
            if (!TraceVdpTiming)
                return;
            if (state == 1)
            {
                if (_vblankRiseLogged)
                    return;
                _vblankRiseLogged = true;
                Console.WriteLine($"[VDP] VBlank edge 0->1 frame={_frameCounter} line={g_scanline}");
            }
            else
            {
                if (_vblankFallLogged)
                    return;
                _vblankFallLogged = true;
                Console.WriteLine($"[VDP] VBlank edge 1->0 frame={_frameCounter} line={g_scanline}");
            }
        }

        private void MaybeLogVdpTiming()
        {
            if (!TraceVdpTiming)
                return;
            long nowMs = _timingStopwatch.ElapsedMilliseconds;
            if (_frameCounter - _lastTimingLogFrame < 60 && nowMs - _lastTimingLogMs < 1000)
                return;
            _lastTimingLogFrame = _frameCounter;
            _lastTimingLogMs = nowMs;

            ushort hv = get_vdp_hvcounter();
            ushort status = PeekVdpStatus();
            int vblank = ((status & VDP_STATUS_VBLANK_MASK) != 0) ? 1 : 0;
            int vintPending = md_m68k.g_interrupt_V_req ? 1 : 0;
            int vintEnabled = g_vdp_reg_1_5_vinterrupt;

            Console.WriteLine(
                $"[VDP] timing frame={_frameCounter} line={g_scanline} hv=0x{hv:X4} status=0x{status:X4} vblank={vblank} vintPending={vintPending} reg1.vint={vintEnabled}");
        }

        private void LogStatusRead(ushort preStatus, ushort postStatus)
        {
            if (TraceMdVdp)
            {
                int vblankPre = ((preStatus & VDP_STATUS_VBLANK_MASK) != 0) ? 1 : 0;
                Console.WriteLine($"[VDP] StatusRead frame={_frameCounter} pre=0x{preStatus:X4} post=0x{postStatus:X4} vblankPre={vblankPre}");
            }
            if (MdTracerCore.MdLog.Enabled)
            {
                int vblank = (preStatus & VDP_STATUS_VBLANK_MASK) != 0 ? 1 : 0;
                int vint = md_m68k.g_interrupt_V_req ? 1 : 0;
                int dma = (preStatus & VDP_STATUS_DMA_MASK) != 0 ? 1 : 0;
                MdTracerCore.MdLog.WriteLine(
                    $"[VDPSTATUS] frame={_frameCounter} status=0x{preStatus:X4} vblank={vblank} vinterrupt={vint} dma={dma}");
            }
        }

        private void LogForcedVBlank()
        {
            if (!TraceMdVdp)
                return;
            Console.WriteLine($"[VDP] Forced VBlank applied frame={_frameCounter} line={g_scanline}");
        }

        private void LogMdWriteSummary()
        {
            if (!MdTracerCore.MdLog.Enabled)
                return;

            if (_mdCtrlWritesThisFrame == 0 && _mdDataWritesThisFrame == 0 && _mdVramWritesThisFrame == 0 && _mdCramWritesThisFrame == 0)
                _mdNoWriteFrames++;
            else
                _mdNoWriteFrames = 0;

            bool log = (_frameCounter % 60) == 0 || _mdNoWriteFrames == 120;
            if (log)
            {
                MdTracerCore.MdLog.WriteLine($"[VDP] md writes frame={_frameCounter} ctrl={_mdCtrlWritesThisFrame} data={_mdDataWritesThisFrame} vram={_mdVramWritesThisFrame} cram={_mdCramWritesThisFrame} zeroFrames={_mdNoWriteFrames} totalCtrl={_mdCtrlWritesTotal} totalData={_mdDataWritesTotal} totalVram={_mdVramWritesTotal} totalCram={_mdCramWritesTotal}");
            }

            _mdCtrlWritesThisFrame = 0;
            _mdDataWritesThisFrame = 0;
            _mdVramWritesThisFrame = 0;
            _mdCramWritesThisFrame = 0;
        }

    }
}
