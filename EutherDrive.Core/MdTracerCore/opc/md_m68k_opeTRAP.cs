using System;
using static EutherDrive.Core.MdTracerCore.md_m68k;
namespace EutherDrive.Core.MdTracerCore
{
    internal partial class md_m68k
    {
        private static readonly bool TraceTrap =
            string.Equals(Environment.GetEnvironmentVariable("EUTHERDRIVE_TRACE_TRAP"), "1", StringComparison.Ordinal);
        private static int _trapLogRemaining = 8;

        private void analyse_TRAP()
        {
            g_clock += 37;
            uint w_pc = g_reg_PC;
            g_reg_PC += 2;
            uint w_start_address = md_main.g_md_bus.read32((uint)(0x0080 + ((g_opcode & 0x0f) << 2)));
            if (TraceTrap && _trapLogRemaining > 0)
            {
                _trapLogRemaining--;
                Console.WriteLine(
                    $"[TRAP] pc=0x{w_pc:X6} op=0x{g_opcode:X4} vec=0x{(0x0080 + ((g_opcode & 0x0f) << 2)):X4} " +
                    $"target=0x{w_start_address:X6} D0=0x{g_reg_data[0].l:X8} D1=0x{g_reg_data[1].l:X8} " +
                    $"A0=0x{g_reg_addr[0].l:X8} A1=0x{g_reg_addr[1].l:X8}");
            }
            stack_push32(g_reg_PC);
            md_main.g_form_code_trace.CPU_Trace_push(Form_Code_Trace.STACK_LIST_TYPE.TRAP, w_pc, w_start_address, g_reg_PC, g_reg_addr[7].l);
            stack_push16(g_reg_SR);
            g_reg_PC = w_start_address;
        }
   }
}
