using TreyarchCompiler.Games;
using TreyarchCompiler.Utilities;

namespace TreyarchCompiler
{
    public class Compiler
    {
        private static readonly object CompileLock = new object();

        public static CompiledCode Compile(string code, string path = "")
        {
            lock (CompileLock)
                return new GSCCompiler(code, path).Compile();
        }

        public static CompiledCode CompileT8(string code)
        {
            lock (CompileLock)
                return new T89Compiler(code).Compile();
        }
    }
}
