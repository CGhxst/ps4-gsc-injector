using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PS4GSCInjector.GameProfiles;

namespace PS4GSCInjector.Tests
{
    [TestClass]
    public class GameProfileTests
    {
        [TestMethod]
        public void Registry_IncludesBo3ProfileWithKnownVersions()
        {
            var bo3 = GameProfileRegistry.All.Single(profile => profile.Id == "bo3");

            Assert.AreEqual("Black Ops 3", bo3.DisplayName);
            Assert.AreEqual("eboot.bin", bo3.ProcessName);
            Assert.AreEqual(2, bo3.Versions.Count);
            Assert.AreEqual(0x547EEF0UL, bo3.Versions[0].TargetScriptAddress);
            Assert.AreEqual(0x6B9CFD0UL, bo3.Versions[1].TargetScriptAddress);
        }

        [TestMethod]
        public void GameTargetOption_ToString_IncludesGameAndVersion()
        {
            var bo3 = GameProfileRegistry.All.Single(profile => profile.Id == "bo3");
            var option = new GameTargetOption(bo3, bo3.Versions[0]);

            Assert.AreEqual("Black Ops 3 1.33", option.ToString());
            Assert.AreEqual("bo3:bo3-1.33", option.Key);
        }

        [TestMethod]
        public void Bo3Profile_ExposesCompilerSymbols()
        {
            var bo3 = GameProfileRegistry.All.Single(profile => profile.Id == "bo3");

            CollectionAssert.Contains(bo3.ConditionalSymbols.ToList(), "BO3");
            CollectionAssert.Contains(bo3.ConditionalSymbols.ToList(), "T7");
            CollectionAssert.Contains(bo3.ConditionalSymbols.ToList(), "PS4");
        }

        [TestMethod]
        public void Registry_IncludesBo4ProfileWithModeHooks()
        {
            var bo4 = GameProfileRegistry.All.Single(profile => profile.Id == "bo4");

            Assert.AreEqual("Black Ops 4", bo4.DisplayName);
            Assert.IsTrue(bo4.CanCompile);
            Assert.AreEqual(3, bo4.Versions.Count);
            Assert.AreEqual(@"scripts\zm_common\load.gsc", bo4.Versions[0].ScriptHookPath);
            Assert.AreEqual(@"scripts\mp_common\bb.gsc", bo4.Versions[1].ScriptHookPath);
            Assert.AreEqual(@"scripts\core_common\load_shared.gsc", bo4.Versions[2].ScriptHookPath);
        }

        [TestMethod]
        public void T8ScriptHash_MatchesKnownScriptHash()
        {
            Assert.AreEqual(0x124CECFF7280BE52UL, T8ScriptHash.Hash64(@"scripts/core_common/clientids_shared.gsc"));
        }

        [TestMethod]
        public void Registry_IncludesBo2ProfileWithModeProcesses()
        {
            var bo2 = GameProfileRegistry.All.Single(profile => profile.Id == "bo2");

            Assert.AreEqual("Black Ops 2", bo2.DisplayName);
            Assert.IsFalse(bo2.CanCompile);
            Assert.AreEqual(2, bo2.Versions.Count);
            Assert.AreEqual("codmp.elf", bo2.Versions[0].ProcessName);
            Assert.AreEqual("maps/mp/gametypes/_clientids.gsc", bo2.Versions[0].ScriptHookPath);
            Assert.AreEqual("codzm.elf", bo2.Versions[1].ProcessName);
            Assert.AreEqual("maps/mp/gametypes_zm/_clientids.gsc", bo2.Versions[1].ScriptHookPath);
        }

        [TestMethod]
        public void Bo2Profile_ValidatesRevisionSixObjectLayout()
        {
            var bo2 = GameProfileRegistry.All.Single(profile => profile.Id == "bo2");
            var script = CreateT6Script(0x100);

            Assert.IsTrue(bo2.IsValidCompiledScript(script));

            script[7] = 5;
            Assert.IsFalse(bo2.IsValidCompiledScript(script));
        }

        [TestMethod]
        public void Bo2Profile_RejectsSectionOutsideObject()
        {
            var bo2 = GameProfileRegistry.All.Single(profile => profile.Id == "bo2");
            var script = CreateT6Script(0x100);
            BitConverter.GetBytes(0x101u).CopyTo(script, 0x20);

            Assert.IsFalse(bo2.IsValidCompiledScript(script));
        }

        [TestMethod]
        public void Bo2AssetSignature_ResolvesRelativeCallTarget()
        {
            const ulong baseAddress = 0x400000;
            const ulong expectedTarget = 0x5BE980;
            var memory = new byte[0x80];
            memory[0x10] = 0xBF;
            memory[0x11] = 0x31;
            memory[0x20] = 0xE8;
            long relative = (long)expectedTarget - ((long)baseAddress + 0x20 + 5);
            BitConverter.GetBytes(checked((int)relative)).CopyTo(memory, 0x21);
            new byte[] { 0x48, 0x8B, 0x40, 0x10 }.CopyTo(memory, 0x28);

            var candidates = Bo2GameProfile.FindDbFindXAssetHeaderCandidates(memory, baseAddress).ToList();

            CollectionAssert.AreEqual(new[] { expectedTarget }, candidates);
        }

        [TestMethod]
        public void Bo2ScriptRecord_ParsesNameLengthAndBuffer()
        {
            var bytes = new byte[0x18];
            BitConverter.GetBytes(0x4000792826UL).CopyTo(bytes, 0);
            BitConverter.GetBytes(0x127u).CopyTo(bytes, 8);
            BitConverter.GetBytes(0x4000792860UL).CopyTo(bytes, 0x10);

            var record = Bo2GameProfile.ParseRecord(bytes);

            Assert.AreEqual(0x4000792826UL, record.NameAddress);
            Assert.AreEqual(0x127UL, record.Size);
            Assert.AreEqual(0x4000792860UL, record.BufferAddress);
        }

        [TestMethod]
        public void Bo2ScriptRecord_RejectsTruncatedData()
        {
            AssertThrows<ArgumentException>(() => Bo2GameProfile.ParseRecord(new byte[0x17]));
        }

        [TestMethod]
        public void Bo2ScriptObject_ReadsEmbeddedModePath()
        {
            var script = CreateT6Script(0x100);

            Assert.AreEqual(
                "maps/mp/gametypes/_clientids.gsc",
                Bo2GameProfile.GetEmbeddedScriptPath(script));
        }

        [TestMethod]
        public void Bo4ScriptAllocation_AlignsToSixteenBytes()
        {
            Assert.AreEqual(0x1000UL, Bo4GameProfile.AlignUp(0x1000, 16));
            Assert.AreEqual(0x1010UL, Bo4GameProfile.AlignUp(0x1001, 16));
            Assert.AreEqual(0x1010UL, Bo4GameProfile.AlignUp(0x100F, 16));
        }

        [TestMethod]
        public void Bo4ScriptAllocation_RejectsInvalidAlignment()
        {
            AssertThrows<ArgumentOutOfRangeException>(() => Bo4GameProfile.AlignUp(0x1000, 0));
            AssertThrows<ArgumentOutOfRangeException>(() => Bo4GameProfile.AlignUp(0x1000, 3));
            AssertThrows<OverflowException>(() => Bo4GameProfile.AlignUp(ulong.MaxValue, 16));
        }

        [TestMethod]
        public void MemoryScriptPointerLocator_FindsT8ScriptParseTreeEntries()
        {
            var surrogateHash = T8ScriptHash.Hash64(@"scripts\zm_common\load.gsc");
            const ulong targetHash = 0x124CECFF7280BE52UL;
            var memory = new byte[0x80];
            ulong baseAddress = 0x300000;
            ulong surrogateEntryAddress = baseAddress + 0x20;
            ulong targetEntryAddress = baseAddress + 0x40;

            BitConverter.GetBytes(surrogateHash).CopyTo(memory, 0x20);
            BitConverter.GetBytes(0x500000UL).CopyTo(memory, 0x30);
            BitConverter.GetBytes(0x1000).CopyTo(memory, 0x38);
            BitConverter.GetBytes(targetHash).CopyTo(memory, 0x40);
            BitConverter.GetBytes(0x600000UL).CopyTo(memory, 0x50);
            BitConverter.GetBytes(0x1000).CopyTo(memory, 0x58);

            var entries = MemoryScriptPointerLocator.FindT8ScriptParseTreeEntries(memory, baseAddress, surrogateHash, targetHash).ToList();

            Assert.AreEqual(2, entries.Count);
            Assert.AreEqual(surrogateEntryAddress, entries[0].EntryAddress);
            Assert.AreEqual(targetEntryAddress, entries[1].EntryAddress);
        }

        private static void AssertThrows<TException>(Action action)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            catch (Exception exception)
            {
                Assert.Fail("Expected " + typeof(TException).Name + " but received " + exception.GetType().Name + ".");
            }

            Assert.Fail("Expected " + typeof(TException).Name + " but no exception was thrown.");
        }

        private static byte[] CreateT6Script(int length)
        {
            var script = new byte[length];
            new byte[] { 0x80, 0x47, 0x53, 0x43, 0x0D, 0x0A, 0x00, 0x06 }.CopyTo(script, 0);
            foreach (int field in new[] { 0x0C, 0x10, 0x14, 0x18, 0x1C, 0x20, 0x24, 0x28 })
                BitConverter.GetBytes(0x40u).CopyTo(script, field);
            BitConverter.GetBytes(0x20u).CopyTo(script, 0x2C);
            BitConverter.GetBytes((ushort)0x60).CopyTo(script, 0x30);
            var name = System.Text.Encoding.ASCII.GetBytes("maps/mp/gametypes/_clientids.gsc\0");
            name.CopyTo(script, 0x60);
            return script;
        }
    }
}
