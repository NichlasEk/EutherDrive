using System;
using static EutherDrive.Core.MdTracerCore.md_m68k;
namespace EutherDrive.Core.MdTracerCore
{
    internal partial class md_m68k
    {
        private void analyse_JSR()
        {
            g_clock += 14;
            uint w_pc = g_reg_PC;
            g_reg_PC += 2;
            adressing_func_address(g_op3, g_op4, 2);
            stack_push32(g_reg_PC);
            md_main.g_form_code_trace.CPU_Trace_push(Form_Code_Trace.STACK_LIST_TYPE.JSR, w_pc, g_analyze_address, g_reg_PC, g_reg_addr[7].l);
            if (g_analyze_address < 0x200 && !_lowPcJsrWatchFired)
            {
                _lowPcJsrWatchFired = true;
                Console.WriteLine($"[JSR] pc=0x{w_pc:X6} target=0x{g_analyze_address:X6} mode={g_op3} reg={g_op4} A0=0x{g_reg_addr[0].l:X8}");
                DumpPcWindowRange(w_pc, 16, 32);
            }
            g_reg_PC = g_analyze_address;
        }
   }
}
