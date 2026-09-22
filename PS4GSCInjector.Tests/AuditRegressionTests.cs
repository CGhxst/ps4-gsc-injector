using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using libdebug;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PS4GSCInjector.GameProfiles;
using TreyarchCompiler;

namespace PS4GSCInjector.Tests
{
    [TestClass]
    public class AuditRegressionTests
    {
        [TestMethod]
        public void Transaction_RestoresWriteThatSucceededBeforeThrowing()
        {
            var memory = new FakeMemory { FailWrite = 2 };
            using (var transaction = memory.Begin())
            {
                ulong allocation = transaction.Allocate(new byte[] { 1, 2, 3 });
                Throws<IOException>(() => transaction.Patch(16, BitConverter.GetBytes(allocation)));
            }
            Assert.AreEqual(0UL, BitConverter.ToUInt64(memory.Bytes, 16));
            Assert.AreEqual(1, memory.Frees);
        }

        [TestMethod]
        public void Transaction_KeepsAllocationWhenRollbackCannotBeConfirmed()
        {
            var memory = new FakeMemory { FailWrite = 2, FailAllLaterWrites = true };
            using (var transaction = memory.Begin())
            {
                ulong allocation = transaction.Allocate(new byte[] { 1, 2, 3 });
                Throws<IOException>(() => transaction.Patch(16, BitConverter.GetBytes(allocation)));
            }
            Assert.AreEqual(0, memory.Frees);
        }

        [TestMethod]
        public void Transaction_RestoresAllFieldsAfterLaterFailure()
        {
            var memory = new FakeMemory { FailWrite = 3 };
            using (var transaction = memory.Begin())
            {
                ulong allocation = transaction.Allocate(new byte[] { 1, 2 });
                transaction.Patch(8, BitConverter.GetBytes(2));
                Throws<IOException>(() => transaction.Patch(16, BitConverter.GetBytes(allocation)));
            }
            Assert.AreEqual(0, BitConverter.ToInt32(memory.Bytes, 8));
            Assert.AreEqual(0UL, BitConverter.ToUInt64(memory.Bytes, 16));
            Assert.AreEqual(1, memory.Frees);
        }

        [TestMethod]
        public void Transaction_CommitPreservesPublishedAllocation()
        {
            var memory = new FakeMemory();
            using (var transaction = memory.Begin())
            {
                ulong allocation = transaction.Allocate(new byte[] { 1, 2, 3 }, 16);
                Assert.AreEqual(0UL, allocation % 16);
                transaction.Patch(16, BitConverter.GetBytes(allocation));
                transaction.Commit();
            }
            Assert.AreEqual(0, memory.Frees);
            Assert.AreNotEqual(0UL, BitConverter.ToUInt64(memory.Bytes, 16));
        }

        [TestMethod]
        public void Transaction_ZeroAllocationDoesNotWrite()
        {
            var memory = new FakeMemory { AllocationAddress = 0 };
            using (var transaction = memory.Begin())
                Throws<GscInjectionException>(() => transaction.Allocate(new byte[] { 1 }));
            Assert.AreEqual(0, memory.Writes);
        }

        [TestMethod]
        public void Socket_ReadHandlesSmallWritesAndMultipleChunks()
        {
            WithSockets((sender, receiver) =>
            {
                byte[] expected = Enumerable.Range(0, 20000).Select(i => (byte)i).ToArray();
                var sending = Task.Run(() =>
                {
                    for (int offset = 0; offset < expected.Length; offset += 3)
                        PS4DBG.SendExact(sender, expected.Skip(offset).Take(3).ToArray(), Math.Min(3, expected.Length - offset));
                });
                CollectionAssert.AreEqual(expected, PS4DBG.ReceiveExact(receiver, expected.Length));
                Assert.IsTrue(sending.Wait(10000));
            });
        }

        [TestMethod]
        public void Socket_TruncatedReplyThrowsInsteadOfSpinning()
        {
            WithSockets((sender, receiver) =>
            {
                PS4DBG.SendExact(sender, new byte[] { 1, 2 }, 2);
                sender.Shutdown(SocketShutdown.Send);
                Throws<EndOfStreamException>(() => PS4DBG.ReceiveExact(receiver, 4));
            });
        }

        [TestMethod]
        public void Scanner_OverlapStaysWithinMapping()
        {
            Assert.AreEqual(1025, MemoryScriptPointerLocator.GetReadSize(1025, 1024, 31));
            Assert.AreEqual(10, MemoryScriptPointerLocator.GetReadSize(10, 1024, 31));
            Assert.AreEqual(1055, MemoryScriptPointerLocator.GetReadSize(4096, 1024, 31));
        }

        [TestMethod]
        public void Bo4Include_RejectsNextSectionAndOccupiedPadding()
        {
            byte[] script = HookScript();
            BitConverter.GetBytes(0x68u).CopyTo(script, 0x30);
            Throws<GscInjectionException>(() => Bo4GameProfile.GetIncludeAppendOffset(script, 2));
            BitConverter.GetBytes(0x80u).CopyTo(script, 0x30);
            script[0x68] = 1;
            Throws<GscInjectionException>(() => Bo4GameProfile.GetIncludeAppendOffset(script, 2));
        }

        [TestMethod]
        public void Bo4Include_ReusesExistingIncludeOrVerifiedPadding()
        {
            byte[] script = HookScript();
            Assert.AreEqual(-1, Bo4GameProfile.GetIncludeAppendOffset(script, 1));
            Assert.AreEqual(0x68, Bo4GameProfile.GetIncludeAppendOffset(script, 2));
        }

        [TestMethod]
        public void Compilers_AreIndependentOfWindowsLanguage()
        {
            var saved = Thread.CurrentThread.CurrentCulture;
            try
            {
                const string source = "#namespace IDENTITY; INIT() { level.ITEM = 1; IPRINTLN(\"test\"); }";
                foreach (bool t8 in new[] { false, true })
                {
                    Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                    var english = t8 ? Compiler.CompileT8(source) : Compiler.Compile(source);
                    Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
                    var turkish = t8 ? Compiler.CompileT8(source) : Compiler.Compile(source);
                    Assert.IsTrue(string.IsNullOrEmpty(english.Error), english.Error);
                    Assert.IsTrue(string.IsNullOrEmpty(turkish.Error), turkish.Error);
                    CollectionAssert.AreEqual(english.CompiledScript, turkish.CompiledScript);
                }
            }
            finally { Thread.CurrentThread.CurrentCulture = saved; }
        }

        [TestMethod]
        public void T8StringTable_Exact255ReferencesUsesOneEntry()
        {
            foreach (int references in new[] { 1, 254, 255, 256, 510 })
            {
                string source = "init() { " + string.Concat(Enumerable.Repeat("level.value = \"same\";", references)) + " }";
                var result = Compiler.CompileT8(source);
                Assert.IsTrue(string.IsNullOrEmpty(result.Error), result.Error);
                Assert.AreEqual((ushort)((references + 254) / 255), BitConverter.ToUInt16(result.CompiledScript, 0x1C));
                Assert.IsTrue(TreyarchCompiledScriptValidator.IsValid(result.CompiledScript, CompiledScriptFormat.T8));
            }
        }

        [TestMethod]
        public void T8Includes_DoesNotTruncateCountToByte()
        {
            var script = new T89CompilerLib.T89ScriptObject(T89CompilerLib.VMREVISIONS.VM_36);
            for (ulong i = 1; i <= 256; i++) script.Includes.Add(i);
            byte[] bytes = script.Serialize();
            Assert.AreEqual((ushort)256, BitConverter.ToUInt16(bytes, 0x58));
        }

        [TestMethod]
        public void Compilers_RejectMoreThan255Locals()
        {
            string source = "init() { " + string.Concat(Enumerable.Range(0, 256).Select(i => "v" + i + " = 1;")) + " }";
            Assert.IsFalse(string.IsNullOrEmpty(Compiler.Compile(source).Error));
            Assert.IsFalse(string.IsNullOrEmpty(Compiler.CompileT8(source).Error));
        }

        [TestMethod]
        public void Compilers_RejectBranchesOutsideSigned16BitRange()
        {
            string source = "init() { if (level.enabled) { " + string.Concat(Enumerable.Repeat("level.value = 1;", 4500)) + " } }";
            Assert.IsFalse(string.IsNullOrEmpty(Compiler.Compile(source).Error));
            Assert.IsFalse(string.IsNullOrEmpty(Compiler.CompileT8(source).Error));
        }

        [TestMethod]
        public void Validators_RejectTruncatedTablesAndInvalidExportAddresses()
        {
            foreach (bool t8 in new[] { false, true })
            {
                var result = t8 ? Compiler.CompileT8("init() { level.value = 1; }") : Compiler.Compile("init() { level.value = 1; }");
                Assert.IsTrue(string.IsNullOrEmpty(result.Error), result.Error);
                var format = t8 ? CompiledScriptFormat.T8 : CompiledScriptFormat.T7;
                Assert.IsTrue(TreyarchCompiledScriptValidator.IsValid(result.CompiledScript, format));
                byte[] broken = (byte[])result.CompiledScript.Clone();
                uint exports = BitConverter.ToUInt32(broken, t8 ? 0x30 : 0x20);
                BitConverter.GetBytes(uint.MaxValue).CopyTo(broken, (int)exports + 4);
                Assert.IsFalse(TreyarchCompiledScriptValidator.IsValid(broken, format));
                broken = (byte[])result.CompiledScript.Clone();
                BitConverter.GetBytes(ushort.MaxValue).CopyTo(broken, t8 ? 0x1E : 0x3A);
                Assert.IsFalse(TreyarchCompiledScriptValidator.IsValid(broken, format));
            }
        }

        [TestMethod]
        public void ImportTables_KeepDistinctCallsWhenOldPackedKeysCollide()
        {
            var t7 = new T7CompilerLib.T7ScriptObject(true);
            var first7 = t7.Imports.AddImport(1, 0, 0, 0);
            var second7 = t7.Imports.AddImport(1, 1, 1, 0);
            Assert.AreNotSame(first7, second7);
            Assert.AreEqual((ushort)2, t7.Imports.Count());
            var t8 = new T89CompilerLib.T89ScriptObject(T89CompilerLib.VMREVISIONS.VM_36);
            var first8 = t8.Imports.AddImport(1, 0, 0, 0);
            var second8 = t8.Imports.AddImport(1, 1, 1, 0);
            Assert.AreNotSame(first8, second8);
            Assert.AreEqual((ushort)2, t8.Imports.Count());
        }

        [TestMethod]
        public void RpcPacket_ValidatesArgumentsAndPreservesIntegerBits()
        {
            Throws<ArgumentException>(() => PS4DBG.BuildRpcArguments(1, 2, 3, new object[7]));
            Throws<ArgumentException>(() => PS4DBG.BuildRpcArguments(1, 2, 3, new object[] { 1.5f }));
            byte[] packet = PS4DBG.BuildRpcArguments(1, 2, 3, new object[] { 49u, ulong.MaxValue });
            Assert.AreEqual(68, packet.Length);
            Assert.AreEqual(49UL, BitConverter.ToUInt64(packet, 20));
            Assert.AreEqual(ulong.MaxValue, BitConverter.ToUInt64(packet, 28));
            Assert.AreEqual(0UL, BitConverter.ToUInt64(packet, 60));
        }

        [TestMethod]
        public void Preprocessor_CanBeReusedAfterFailureAndRejectsDuplicateElse()
        {
            var processor = new ConditionalBlocks();
            processor.LoadConditionalTokens(new List<string> { "PS4" });
            Throws<CBSyntaxException>(() => processor.ParseSource("#ifdef PS4\nmissing endif"));
            string result = processor.ParseSource("#ifdef PS4\nkeep\n#endif");
            StringAssert.Contains(result, "keep");
            Throws<CBSyntaxException>(() => processor.ParseSource("#ifdef PS4\na\n#else\nb\n#else\nc\n#endif"));
        }

        [TestMethod]
        public void Preprocessor_EvenBackslashesAllowClosingQuote()
        {
            var processor = new ConditionalBlocks();
            string source = "\"text" + new string('\\', 2) + "\"\n#ifdef MISSING\nremove\n#endif";
            Assert.IsFalse(processor.ParseSource(source).Contains("remove"));
        }

        [TestMethod]
        public void T8Header_BytecodeRangeFitsOutput()
        {
            var result = Compiler.CompileT8("init() { level.value = 1; }");
            Assert.IsTrue(string.IsNullOrEmpty(result.Error), result.Error);
            uint start = BitConverter.ToUInt32(result.CompiledScript, 0x20);
            uint size = BitConverter.ToUInt32(result.CompiledScript, 0x54);
            Assert.IsTrue(start >= 0x60 && size > 0);
            Assert.IsTrue((ulong)start + size <= (ulong)result.CompiledScript.Length);
        }

        [TestMethod]
        public void T8NumericOperands_PreserveSignedIntegersAndFloatBits()
        {
            foreach (object value in new object[] { -65536, int.MinValue, 65536, uint.MaxValue, 1.2345678f })
            {
                var script = new T89CompilerLib.T89ScriptObject(T89CompilerLib.VMREVISIONS.VM_36);
                var export = script.Exports.Add(1, 2, 0);
                var opcode = export.AddGetNumber(value);
                byte[] bytes = script.Serialize();
                int offset = (int)opcode.GetCommitDataAddress();
                if (value is float)
                    CollectionAssert.AreEqual(BitConverter.GetBytes((float)value), bytes.Skip(offset).Take(4).ToArray());
                else
                    Assert.AreEqual(unchecked((uint)Convert.ToInt64(value)), BitConverter.ToUInt32(bytes, offset));
            }
        }

        private static byte[] HookScript()
        {
            var script = new byte[0x100];
            new byte[] { 0x80, 0x47, 0x53, 0x43, 13, 10, 0, 0x36 }.CopyTo(script, 0);
            BitConverter.GetBytes(0x60u).CopyTo(script, 0x18);
            BitConverter.GetBytes((ushort)1).CopyTo(script, 0x58);
            BitConverter.GetBytes(1UL).CopyTo(script, 0x60);
            return script;
        }

        private static void WithSockets(Action<Socket, Socket> test)
        {
            using (var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            using (var sender = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                listener.Listen(1);
                sender.Connect(listener.LocalEndPoint);
                using (Socket receiver = listener.Accept())
                {
                    sender.SendTimeout = receiver.ReceiveTimeout = 10000;
                    test(sender, receiver);
                }
            }
        }

        private static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            Assert.Fail("Expected " + typeof(T).Name);
        }

        private sealed class FakeMemory
        {
            internal readonly byte[] Bytes = new byte[2048];
            internal int Writes, Frees, FailWrite;
            internal bool FailAllLaterWrites;
            internal ulong AllocationAddress = 257;
            internal RemoteScriptTransaction Begin()
            {
                return new RemoteScriptTransaction(
                    (address, size) => Bytes.Skip((int)address).Take(size).ToArray(),
                    (address, data) =>
                    {
                        data.CopyTo(Bytes, (int)address);
                        Writes++;
                        if (Writes == FailWrite || (FailAllLaterWrites && Writes >= FailWrite))
                            throw new IOException("Simulated lost acknowledgement");
                    },
                    size => AllocationAddress,
                    (address, size) => Frees++);
            }
        }
    }
}
