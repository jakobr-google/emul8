//
// Copyright (c) Antmicro
// Copyright (c) Realtime Embedded
//
// This file is part of the Emul8 project.
// Full license details are defined in the 'LICENSE' file.
//
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Antmicro.Migrant;
using Emul8.Logging;
using Emul8.Peripherals;
using System.Text;

namespace Emul8.Peripherals.Bus
{
    internal class PeripheralCollection
    {
        internal PeripheralCollection(SystemBus sysbus)
        {
            this.sysbus = sysbus;
            blocks = new Block[0];
            shortBlocks = new Dictionary<long, Block>();
            sync = new object();
            InvalidateLastBlock();
        }

        public IEnumerable<IBusRegistered<IBusPeripheral>> Peripherals
        {
            get
            {
                lock(sync)
                {
                    return blocks.Union(shortBlocks.Select(x => x.Value)).Select(x => x.Peripheral).Distinct();
                }
            }
        }

        public void Add(long start, long end, IBusRegistered<IBusPeripheral> peripheral, PeripheralAccessMethods accessMethods)
        {
            // the idea here is that we prefer the peripheral to go to dictionary
            // ideally it can go to dicitonary wholly, but we try to put there as much as we can
            lock(sync)
            {
                var name = string.Format("{0} @ {1}.", peripheral.Peripheral.GetType().Name, peripheral.RegistrationPoint);
                // TODO: check index (and start/stop)
                var block = new Block { Start = start, End = end, AccessMethods = accessMethods, Peripheral = peripheral };
                // let's decide whether block should go to array, dictionary or both
                var goToDictionary = true;
                // is the peripheral properly aligned?
                if((start & PageAlign) != 0)
                {
                    sysbus.NoisyLog("{0} is at not aligned address - not using dictionary.", name);
                    goToDictionary = false;
                }
                // is the peripheral small enough?
                var size = end - start;
                var numOfPages = size/PageSize;
                if(numOfPages > NumOfPagesThreshold)
                {
                    sysbus.NoisyLog("{0} is too large - not using dictionary.", name);
                    goToDictionary = false;
                }
                var goToArray = !goToDictionary; // peripheral will go to array if we couldn't put it in dictionary
                if(goToDictionary && size % PageSize != 0)
                {
                    // but it should also go to array if it isn't properly aligned on its last page
                    goToArray = true;
                }
                if(goToArray)
                {
                    blocks = blocks.Union(new [] { block }).OrderBy(x => x.Start).ToArray();
                    sysbus.NoisyLog("Added {0} to binary search array.", name);
                }
                if(!goToDictionary)
                {
                    return;
                }
                // note that truncating is in fact good thing here
                for(var i = 0; i < numOfPages; i++)
                {
                    shortBlocks.Add(start + i*PageSize, block);
                }
                sysbus.NoisyLog("Added {0} to dictionary.", name);
            }
        }

        public void Remove(IPeripheral peripheral)
        {
            lock(sync)
            {
                // list is scanned first
                blocks = blocks.Where(x => x.Peripheral.Peripheral != peripheral).ToArray();
                // then dictionary
                var toRemove = shortBlocks.Where(x => x.Value.Peripheral.Peripheral == peripheral).Select(x => x.Key).ToArray();
                foreach(var keyToRemove in toRemove)
                {
                    shortBlocks.Remove(keyToRemove);
                }
                InvalidateLastBlock();
            }
        }

        public void VisitAccessMethods(IBusPeripheral peripheral, Func<PeripheralAccessMethods, PeripheralAccessMethods> onPam)
        {
            lock(sync)
            {
                blocks = blocks.Select(block => 
                {
                    if(block.Peripheral.Peripheral != peripheral)
                    {
                        return block;
                    }
                    block.AccessMethods = onPam(block.AccessMethods);
                    return block;
                }).ToArray();
                shortBlocks = shortBlocks.Select(dEntry =>
                {
                    if(dEntry.Value.Peripheral.Peripheral != peripheral)
                    {
                        return dEntry;
                    }
                    var block = dEntry.Value;
                    block.AccessMethods = onPam(block.AccessMethods);
                    return new KeyValuePair<long, Block>(dEntry.Key, block);
                }).ToDictionary(x => x.Key, x => x.Value);
                InvalidateLastBlock();
            }
        }

        public PeripheralAccessMethods FindAccessMethods(long address, out long startAddress, out long offset, out long endAddress)
        {
            // no need to lock here yet, cause last block is in the thread local storage
            var lastBlock = lastBlockStorage.Value;
#if DEBUG
            Interlocked.Increment(ref queryCount);
#endif
            if (address >= lastBlock.Start && address < lastBlock.End)
            {
#if DEBUG
                Interlocked.Increment(ref lastPeripheralCount);
#endif
                startAddress = lastBlock.Start;
                endAddress = lastBlock.End;
                offset = lastBlock.Offset;
                return lastBlock.AccessMethods;
            }
            lock(sync)
            {
                // let's try dictionary
                Block block;
                if(!shortBlocks.TryGetValue(address & ~PageAlign, out block))
                {
                    // binary search - our last resort
                    var index = BinarySearch(address);
                    if(index == -1)
                    {
                        startAddress = 0;
                        endAddress = 0;
                        offset = 0;
                        return null;
                    }
#if DEBUG
                    Interlocked.Increment(ref binarySearchCount);
#endif
                    block = blocks[index];
                }
#if DEBUG
                else
                {
                    Interlocked.Increment(ref dictionaryCount);
                }
#endif
                startAddress = block.Start;
                offset = block.Offset;
                endAddress = block.End;
                lastBlockStorage.Value = block;
                return block.AccessMethods;
            }
        }

#if DEBUG
        public void ShowStatistics()
        {
            var misses = queryCount - lastPeripheralCount - dictionaryCount - binarySearchCount;
            var line = new StringBuilder("\n  Memory queries statistics are as follows:");
            if(queryCount > 0)
            {
                line.AppendFormat("\tConsecutive hits:   {0:00.00} ({1})\n", 100.0 * lastPeripheralCount / queryCount, lastPeripheralCount)
                    .AppendFormat("\tDictionary hits:    {0:00.00} ({1})\n", 100.0 * dictionaryCount / queryCount, dictionaryCount)
                    .AppendFormat("\tBinary search:      {0:00.00} ({1})\n", 100.0 * binarySearchCount / queryCount, binarySearchCount)
                    .AppendFormat("\tMisses:             {0:00.00} ({1})", 100.0 * misses / queryCount, misses);
            }
            else
            {
                line.AppendLine("\tNo queries");
            }
            sysbus.DebugLog(line.ToString());
        }
#endif

        private int BinarySearch(long offset)
        {
            var min = 0;
            var max = blocks.Length - 1;
            if(blocks.Length == 0)
            {
                return -1;
            }
            do
            {
                var current = (min + max) / 2;
                if (offset >= blocks[current].End)
                {
                    min = current + 1;
                }
                else if (offset < blocks[current].Start)
                {
                    max = current - 1;
                }
                else
                {
                    return current;
                }
            }
            while(min <= max);
            return -1;
        }

        private void InvalidateLastBlock()
        {
            lastBlockStorage = new ThreadLocal<Block>();
        }

        private Dictionary<long, Block> shortBlocks;
        private Block[] blocks;
        [Constructor]
        private ThreadLocal<Block> lastBlockStorage;
        private object sync;
        private readonly SystemBus sysbus;

#if DEBUG
        private long queryCount;
        private long lastPeripheralCount;
        private long dictionaryCount;
        private long binarySearchCount;
#endif

        private const long PageSize = 1 << 11;
        private const long PageAlign = PageSize - 1;
        private const long NumOfPagesThreshold = 4;

        private struct Block
        {
            public long Start;
            public long End;
            public long Offset;
            public PeripheralAccessMethods AccessMethods;
            public IBusRegistered<IBusPeripheral> Peripheral;
        }
    }
}

