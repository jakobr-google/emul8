//
// Copyright (c) Antmicro
// Copyright (c) Realtime Embedded
//
// This file is part of the Emul8 project.
// Full license details are defined in the 'LICENSE' file.
//
using System;
using Emul8.Core;
using System.Linq;
using Emul8.Exceptions;

namespace Emul8.Peripherals.Bus
{
    public static class RedirectorExtensions
    {
        public static void Redirect(this SystemBus sysbus, long from, long to, long size)
        {
            var redirector = new Redirector(sysbus.Machine, to);
            var rangePoint = new BusRangeRegistration(from.By(size));
            sysbus.Register(redirector, rangePoint);
        }
    }

    public sealed class Redirector : IBytePeripheral, IWordPeripheral, IDoubleWordPeripheral, IMultibyteWritePeripheral
    {
        public Redirector(Machine machine, long redirectedAddress)
        {
            this.redirectedAddress = redirectedAddress;
            systemBus = machine.SystemBus;
        }

        public byte ReadByte(long offset)
        {
            return systemBus.ReadByte(redirectedAddress + offset);
        }

        public void WriteByte(long offset, byte value)
        {
            systemBus.WriteByte(redirectedAddress + offset, value);
        }

        public ushort ReadWord(long offset)
        {
            return systemBus.ReadWord(redirectedAddress + offset);
        }

        public void WriteWord(long offset, ushort value)
        {
            systemBus.WriteWord(offset, value);
        }

        public uint ReadDoubleWord(long offset)
        {
            return systemBus.ReadDoubleWord(redirectedAddress + offset);
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            systemBus.WriteDoubleWord(redirectedAddress + offset, value);
        }

        public long TranslateAbsolute(long address)
        {
            foreach(var range in systemBus.GetRegistrationPoints(this).Select(x => x.Range))
            {
                if(range.Contains(address))
                {
                    return address - range.StartAddress + redirectedAddress;
                }
            }
            throw new RecoverableException("Cannot translate address that does not lay in redirector.");
        }

        public byte[] ReadBytes(long offset, int count)
        {
            return systemBus.ReadBytes(redirectedAddress + offset, count);
        }

        public void WriteBytes(long offset, byte[] array, int startingIndex, int count)
        {
            systemBus.WriteBytes(array, redirectedAddress + offset, count);
        }

        public void Reset()
        {

        }

        private readonly long redirectedAddress;
        private readonly SystemBus systemBus;
    }
}

