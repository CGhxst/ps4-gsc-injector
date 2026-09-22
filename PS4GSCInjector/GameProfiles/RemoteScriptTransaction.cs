using System;
using System.Collections.Generic;
using System.Linq;
using libdebug;

namespace PS4GSCInjector.GameProfiles
{
    // A failed write may already have reached the console. Keep its original bytes
    // before sending it, and retain allocations if restoration cannot be verified.
    internal sealed class RemoteScriptTransaction : IDisposable
    {
        private readonly Func<ulong, int, byte[]> read;
        private readonly Action<ulong, byte[]> write;
        private readonly Func<int, ulong> allocate;
        private readonly Action<ulong, int> free;
        private readonly List<KeyValuePair<ulong, byte[]>> originals = new List<KeyValuePair<ulong, byte[]>>();
        private readonly List<KeyValuePair<ulong, int>> allocations = new List<KeyValuePair<ulong, int>>();
        private bool committed;

        internal RemoteScriptTransaction(PS4DBG ps4, int pid)
            : this((address, length) => ps4.ReadMemory(pid, address, length),
                (address, bytes) => ps4.WriteMemory(pid, address, bytes),
                length => ps4.AllocateMemory(pid, length),
                (address, length) => ps4.FreeMemory(pid, address, length)) { }

        internal RemoteScriptTransaction(Func<ulong, int, byte[]> read, Action<ulong, byte[]> write,
            Func<int, ulong> allocate, Action<ulong, int> free)
        {
            this.read = read;
            this.write = write;
            this.allocate = allocate;
            this.free = free;
        }

        internal ulong Allocate(byte[] bytes, int alignment = 1)
        {
            int length = checked(bytes.Length + alignment - 1);
            ulong address = allocate(length);
            if (address == 0)
                throw new GscInjectionException("Failed to allocate script memory.");
            allocations.Add(new KeyValuePair<ulong, int>(address, length));
            ulong aligned = Bo4GameProfile.AlignUp(address, alignment);
            write(aligned, bytes);
            if (!read(aligned, bytes.Length).SequenceEqual(bytes))
                throw new GscInjectionException("Script memory verification failed.");
            return aligned;
        }

        internal void Patch(ulong address, byte[] bytes)
        {
            byte[] original = read(address, bytes.Length);
            if (original.Length != bytes.Length)
                throw new GscInjectionException("Could not read the original script record.");
            originals.Add(new KeyValuePair<ulong, byte[]>(address, original));
            write(address, bytes);
            if (!read(address, bytes.Length).SequenceEqual(bytes))
                throw new GscInjectionException("Script record verification failed.");
        }

        internal void Commit() { committed = true; }

        public void Dispose()
        {
            if (committed) return;
            bool restored = true;
            for (int i = originals.Count - 1; i >= 0; i--)
            {
                try
                {
                    write(originals[i].Key, originals[i].Value);
                    restored &= read(originals[i].Key, originals[i].Value.Length).SequenceEqual(originals[i].Value);
                }
                catch { restored = false; }
            }
            if (!restored) return;
            foreach (var allocation in allocations)
            {
                try { free(allocation.Key, allocation.Value); }
                catch { /* Preserve the injection error if the console disconnected. */ }
            }
        }
    }
}
