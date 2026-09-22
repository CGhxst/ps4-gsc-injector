using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using libdebug;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PS4GSCInjector.GameProfiles;
using TreyarchCompiler;

namespace PS4GSCInjector.Tests
{
    // Exercises the real profiles and packet transport against a local ps4debug peer.
    // It verifies memory layout and protocol behavior, not the game's script VM.
    [TestClass]
    public class InjectionProtocolTests
    {
        [TestMethod]
        public void Bo3_InjectsAndPreservesInputAndOldCode()
        {
            using (var peer = new DebugPeer())
            {
                var profile = new Bo3GameProfile();
                byte[] input = Compiler.Compile("init() { level.value = 1; }").CompiledScript;
                byte[] original = (byte[])input.Clone();
                BitConverter.GetBytes(0x12345678u).CopyTo(original, 8);
                peer.Store(0x2000, original);
                ulong record = profile.Versions[0].TargetScriptAddress;
                peer.Store(record + 0x10, BitConverter.GetBytes(0x2000UL));
                peer.Connect();
                profile.InjectCompiledScript(peer.Client, new Process("eboot.bin", 1), profile.Versions[0], input);
                ulong address = BitConverter.ToUInt64(peer.Read(record + 0x10, 8), 0);
                Assert.AreNotEqual(0x2000UL, address);
                Assert.AreEqual(0x12345678u, BitConverter.ToUInt32(peer.Read(address + 8, 4), 0));
                Assert.AreNotEqual(0x12345678u, BitConverter.ToUInt32(input, 8));
                profile.InjectCompiledScript(peer.Client, new Process("eboot.bin", 1), profile.Versions[0], input);
                Assert.AreEqual(0, peer.Freed.Count);
                CollectionAssert.AreEqual(original, peer.Read(0x2000, original.Length));
            }
        }

        [TestMethod]
        public void Bo4_UpdatesIdentitySizeAndIncludeWithoutCorruptingNextSection()
        {
            using (var peer = new DebugPeer())
            {
                var profile = new Bo4GameProfile();
                const ulong targetHash = 0x124CECFF7280BE52;
                byte[] input = Compiler.CompileT8("init() { level.value = 1; }").CompiledScript;
                BitConverter.GetBytes(0x777UL).CopyTo(input, 0x10);
                byte[] target = (byte[])input.Clone();
                BitConverter.GetBytes(targetHash).CopyTo(target, 0x10);
                BitConverter.GetBytes(0x12345678u).CopyTo(target, 8);
                byte[] hook = new byte[0x100];
                target.Take(8).ToArray().CopyTo(hook, 0);
                ulong hookHash = T8ScriptHash.Hash64(profile.Versions[0].ScriptHookPath);
                BitConverter.GetBytes(hookHash).CopyTo(hook, 0x10);
                BitConverter.GetBytes(0x60u).CopyTo(hook, 0x18);
                BitConverter.GetBytes((ushort)1).CopyTo(hook, 0x58);
                BitConverter.GetBytes(1UL).CopyTo(hook, 0x60);
                BitConverter.GetBytes(0x80u).CopyTo(hook, 0x30);
                hook[0x80] = 0xAA;
                peer.Store(0x2000, hook);
                peer.Store(0x2400, target);
                peer.Store(0x1100, T8Record(hookHash, 0x2000, hook.Length));
                peer.Store(0x1120, T8Record(targetHash, 0x2400, target.Length));
                peer.Connect();
                profile.InjectCompiledScript(peer.Client, new Process("eboot.bin", 1), profile.Versions[0], input);
                ulong address = BitConverter.ToUInt64(peer.Read(0x1130, 8), 0);
                Assert.AreEqual(0UL, address % 16);
                Assert.AreEqual(targetHash, BitConverter.ToUInt64(peer.Read(address + 0x10, 8), 0));
                Assert.AreEqual(0x12345678u, BitConverter.ToUInt32(peer.Read(address + 8, 4), 0));
                Assert.AreEqual(input.Length, BitConverter.ToInt32(peer.Read(0x1138, 4), 0));
                Assert.AreEqual(targetHash, BitConverter.ToUInt64(peer.Read(0x2068, 8), 0));
                Assert.AreEqual((ushort)2, BitConverter.ToUInt16(peer.Read(0x2058, 2), 0));
                Assert.AreEqual((byte)0xAA, peer.Read(0x2080, 1)[0]);
                Assert.AreEqual(0x777UL, BitConverter.ToUInt64(input, 0x10));
            }
        }

        [TestMethod]
        public void Bo2_BothModesUse24ByteRecordAndExpectedRpcPath()
        {
            foreach (var version in new Bo2GameProfile().Versions)
            using (var peer = new DebugPeer { Protection = 5 })
            {
                byte[] code = new byte[0x80];
                code[0x10] = 0xBF; code[0x11] = 49;
                code[0x20] = 0xE8;
                BitConverter.GetBytes(0x1500 - (0x1400 + 0x20 + 5)).CopyTo(code, 0x21);
                new byte[] { 0x48, 0x8B, 0x40, 0x10 }.CopyTo(code, 0x28);
                peer.Store(0x1400, code);
                peer.Store(0x1500, new byte[] { 0x55, 0x48, 0x89, 0xE5 });
                byte[] script = new byte[0x100];
                new byte[] { 0x80, 0x47, 0x53, 0x43, 13, 10, 0, 6 }.CopyTo(script, 0);
                foreach (int field in new[] { 0x0C, 0x10, 0x14, 0x18, 0x1C, 0x20, 0x24, 0x28 })
                    BitConverter.GetBytes(0x40u).CopyTo(script, field);
                BitConverter.GetBytes((ushort)0x60).CopyTo(script, 0x30);
                Encoding.ASCII.GetBytes(version.ScriptHookPath + "\0").CopyTo(script, 0x60);
                byte[] original = (byte[])script.Clone();
                BitConverter.GetBytes(0x12345678u).CopyTo(original, 8);
                peer.Store(0x2400, original);
                peer.Store(0x2200, Encoding.ASCII.GetBytes(version.ScriptHookPath + "\0"));
                peer.Store(0x1600, BitConverter.GetBytes(0x2200UL));
                peer.Store(0x1608, BitConverter.GetBytes(original.Length));
                peer.Store(0x1610, BitConverter.GetBytes(0x2400UL));
                peer.Store(0x1618, BitConverter.GetBytes(0xDEADBEEFCAFEBABEUL));
                peer.Connect();
                new Bo2GameProfile().InjectCompiledScript(peer.Client, new Process(version.ProcessName, 1), version, script);
                ulong address = BitConverter.ToUInt64(peer.Read(0x1610, 8), 0);
                Assert.AreEqual(0x12345678u, BitConverter.ToUInt32(peer.Read(address + 8, 4), 0));
                Assert.AreEqual(0xDEADBEEFCAFEBABEUL, BitConverter.ToUInt64(peer.Read(0x1618, 8), 0));
                Assert.AreEqual(version.ScriptHookPath, peer.LastRpcPath);
                Assert.AreEqual(0u, BitConverter.ToUInt32(script, 8));
            }
        }

        private static byte[] T8Record(ulong name, ulong buffer, int size)
        {
            byte[] record = new byte[32];
            BitConverter.GetBytes(name).CopyTo(record, 0);
            BitConverter.GetBytes(buffer).CopyTo(record, 16);
            BitConverter.GetBytes(size).CopyTo(record, 24);
            return record;
        }

        private sealed class DebugPeer : IDisposable
        {
            private readonly Dictionary<ulong, byte> bytes = new Dictionary<ulong, byte>();
            private readonly Socket listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            private Task serving;
            internal PS4DBG Client;
            internal ushort Protection = 3;
            internal string LastRpcPath;
            internal readonly List<ulong> Freed = new List<ulong>();
            private ulong nextAllocation = 0x10001;

            internal void Store(ulong address, byte[] data)
            {
                lock (bytes)
                    for (int i = 0; i < data.Length; i++) bytes[address + (ulong)i] = data[i];
            }

            internal byte[] Read(ulong address, int length)
            {
                byte[] result = new byte[length];
                lock (bytes)
                    for (int i = 0; i < length; i++) bytes.TryGetValue(address + (ulong)i, out result[i]);
                return result;
            }

            internal void Connect()
            {
                listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                listener.Listen(1);
                serving = Task.Run(() =>
                {
                    using (Socket socket = listener.Accept())
                    {
                        socket.SendTimeout = socket.ReceiveTimeout = 10000;
                        while (true)
                        {
                            byte[] header = PS4DBG.ReceiveExact(socket, 12);
                            Assert.AreEqual(0xFFAABBCCu, BitConverter.ToUInt32(header, 0));
                            var command = (PS4DBG.CMDS)BitConverter.ToUInt32(header, 4);
                            byte[] data = PS4DBG.ReceiveExact(socket, BitConverter.ToInt32(header, 8));
                            if (command == PS4DBG.CMDS.CMD_CONSOLE_END) return;
                            Send(socket, BitConverter.GetBytes(0x80000000u));
                            switch (command)
                            {
                                case PS4DBG.CMDS.CMD_PROC_READ:
                                    Send(socket, Read(BitConverter.ToUInt64(data, 4), BitConverter.ToInt32(data, 12))); break;
                                case PS4DBG.CMDS.CMD_PROC_WRITE:
                                    Store(BitConverter.ToUInt64(data, 4), PS4DBG.ReceiveExact(socket, BitConverter.ToInt32(data, 12)));
                                    Send(socket, BitConverter.GetBytes(0x80000000u)); break;
                                case PS4DBG.CMDS.CMD_PROC_ALLOC:
                                    Send(socket, BitConverter.GetBytes(nextAllocation));
                                    nextAllocation += (ulong)BitConverter.ToInt32(data, 4) + 0x100; break;
                                case PS4DBG.CMDS.CMD_PROC_FREE:
                                    Freed.Add(BitConverter.ToUInt64(data, 4)); break;
                                case PS4DBG.CMDS.CMD_PROC_MAPS:
                                    Send(socket, BitConverter.GetBytes(1));
                                    byte[] map = new byte[58];
                                    BitConverter.GetBytes(0x1000UL).CopyTo(map, 32);
                                    BitConverter.GetBytes(0x4000UL).CopyTo(map, 40);
                                    BitConverter.GetBytes(Protection).CopyTo(map, 56);
                                    Send(socket, map); break;
                                case PS4DBG.CMDS.CMD_PROC_INTALL:
                                    Send(socket, BitConverter.GetBytes(0x9000UL)); break;
                                case PS4DBG.CMDS.CMD_PROC_CALL:
                                    Assert.AreEqual(49UL, BitConverter.ToUInt64(data, 20));
                                    Assert.AreEqual(0x1500UL, BitConverter.ToUInt64(data, 12));
                                    LastRpcPath = Encoding.ASCII.GetString(Read(BitConverter.ToUInt64(data, 28), 256)).Split('\0')[0];
                                    byte[] reply = new byte[12];
                                    BitConverter.GetBytes(0x1600UL).CopyTo(reply, 4);
                                    Send(socket, reply); break;
                                default: throw new InvalidDataException("Unexpected command: " + command);
                            }
                        }
                    }
                });
                Client = new PS4DBG((IPEndPoint)listener.LocalEndPoint);
                Client.Connect();
            }

            private static void Send(Socket socket, byte[] data) { PS4DBG.SendExact(socket, data, data.Length); }

            public void Dispose()
            {
                try { Client?.Disconnect(); }
                finally
                {
                    listener.Dispose();
                    if (serving != null) Assert.IsTrue(serving.Wait(10000), "Protocol peer did not stop.");
                }
            }
        }
    }
}
