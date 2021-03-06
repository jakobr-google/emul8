//
// Copyright (c) Antmicro
// Copyright (c) Realtime Embedded
//
// This file is part of the Emul8 project.
// Full license details are defined in the 'LICENSE' file.
//
using System;
using System.Runtime.InteropServices;
using System.IO;
using Emul8.Core;
using Emul8.Exceptions;

namespace Emul8.Peripherals.CPU.Disassembler
{
    public class DisassemblyEngine
    {  
        public DisassemblyEngine(IDisassemblable disasm, Func<uint, uint> addressTranslator)
        {
            this.cpu = disasm;
            this.AddressTranslator = addressTranslator;
        }

        public void LogSymbol(uint pc, uint size, uint flags)
        {
            if (disassembler == null || LogFile == null)
            {
                return;
            }

            using (var file = File.AppendText(LogFile))
            {
                var phy = AddressTranslator(pc);
                var symbol = cpu.SymbolLookup(pc);
                var disas = Disassemble(pc, phy, size, flags);

                if (disas == null)
                {
                    return;
                }

                file.WriteLine("-------------------------");
                if (size > 0)
                {
                    file.Write("IN: {0} ", symbol == null ? String.Empty : symbol.ToString());
                    if (phy != pc)
                    {
                        file.Write("(physical: 0x{0:x8}, virtual: 0x{1:x8})", phy, pc);
                    }
                    else
                    {
                        file.Write("(address: 0x{0:x8})", phy);
                    }
                    if(symbol != null)
                    {
                        file.WriteLine(symbol.Start == pc ? " (entry)" 
                            : symbol.Start == symbol.End ? String.Format("+0x{0:x} (guessed)", pc - symbol.Start) 
                            : String.Empty);
                    }
                    else
                    {
                        file.WriteLine();
                    }
                }
                else
                {
                    // special case when disassembling magic addresses in Cortex-M 
                    file.WriteLine("Magic PC value detected: 0x{0:x8}", flags > 0 ? pc | 1 : pc);
                }

                file.WriteLine(string.IsNullOrWhiteSpace(disas) ? string.Format("Cannot disassemble from 0x{0:x8} to 0x{1:x8}", pc, pc + size)  : disas);
                file.WriteLine(string.Empty);
            }
        }

        public void LogDisassembly(IntPtr memory, uint size)
        {
            if (disassembler == null || LogFile == null)
            {
                return;
            }

            using (var file = File.AppendText(LogFile))
            {
                var disassembled = Disassemble(memory, size);
                if (disassembled != null)
                {
                    file.WriteLine(disassembled);
                }
            }
        }

        public string Disassemble(IntPtr memory, uint size, uint pc = 0, uint flags = 0)
        {
            if (disassembler == null)
            {
                return null;
            }

            // let's assume that we have 2 byte processor commands and each is disassembled to 160 characters
            // sometimes it happens that size is equal to 0 (e.g. addresses like 0xfffffffd) - in such case wee need to add 1 to make it work
            var outputLength = (size + 1) * 160;
            var outputPtr = Marshal.AllocHGlobal((int)outputLength);
            int result = disassembler.Disassemble(pc, memory, (ulong)size, flags, outputPtr, (ulong)outputLength);
            var lines = Marshal.PtrToStringAuto(outputPtr) ?? string.Empty;
            Marshal.FreeHGlobal(outputPtr);

            return result == 0 ? null : lines;
        }

        public string LogFile
        {
            get { return logFile; }
            set 
            {
                if(value != null && disassembler == null)
                {
                    throw new RecoverableException(string.Format("Could not set log file: {0} as there is no selected disassembler.", value));
                }

                logFile = value;
                cpu.LogTranslatedBlocks = (value != null);

                if (logFile != null && File.Exists(logFile))
                {
                    // truncate the file if it already exists
                    File.WriteAllText(logFile, string.Empty);
                }
            }
        }     

        public string Disassemble(uint addr, bool isPhysical, uint size, uint flags)
        {
            var physical = isPhysical ? addr : AddressTranslator(addr);
            /*if (physical == 0xffffffff)
            {
                this.Log(LogLevel.Warning, "Couldn't disassemble address 0x{0:x8}", addr);
                return string.Empty;
            }*/

            return Disassemble(addr, physical, size, flags);
        }

        public bool SetDisassembler(IDisassembler dis)
        {
            disassembler = dis;
            return true;
        }

        public DisassemblerType CurrentDisassemblerType { get { return disassembler == null ? DisassemblerType.None : disassembler.Type; } }

        private string Disassemble(uint pc, uint physical, uint size, uint flags)
        {
            var tabPtr = Marshal.AllocHGlobal((int)size);
            var tab = cpu.Bus.ReadBytes(physical, (int)size, true);
            Marshal.Copy(tab, 0, tabPtr, (int)size);

            var result = Disassemble(tabPtr, size, pc, flags);
            Marshal.FreeHGlobal(tabPtr);
            return result;
        }

        private IDisassembler disassembler;
        protected readonly IDisassemblable cpu;
        private string logFile;
        private Func<uint, uint> AddressTranslator;
    }
}
