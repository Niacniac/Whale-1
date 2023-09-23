using System.Runtime.InteropServices;


namespace Whale_1.src.Core
{
    public class NnueLibraryLoader
    {
        private IntPtr hmod;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void NNUE_INIT(string evalFile);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NNUE_EVALUATE_FEN(string fen);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NNUE_EVALUATE(int player, int[] pieces, int[] squares);

        private NNUE_INIT nnue_init;
        private NNUE_EVALUATE_FEN nnue_evaluate_fen;
        private NNUE_EVALUATE nnue_evaluate;

        public int LoadNnueLibrary()
        {
            string path = "libnnueprobe64.dll";

            if (hmod != IntPtr.Zero)
            {
                FreeLibrary(hmod);
            }

            hmod = LoadLibrary(path);
            if (hmod != IntPtr.Zero)
            {
                nnue_init = (NNUE_INIT)Marshal.GetDelegateForFunctionPointer(GetProcAddress(hmod, "nnue_init"), typeof(NNUE_INIT));
                nnue_evaluate_fen = (NNUE_EVALUATE_FEN)Marshal.GetDelegateForFunctionPointer(GetProcAddress(hmod, "nnue_evaluate_fen"), typeof(NNUE_EVALUATE_FEN));
                nnue_evaluate = (NNUE_EVALUATE)Marshal.GetDelegateForFunctionPointer(GetProcAddress(hmod, "nnue_evaluate"), typeof(NNUE_EVALUATE));
                return 1;
            }
            else
            {
                Console.WriteLine("LibNNUE not Loaded!");
                return 0;
            }
        }

        public void NnueInit(string evalFile)
        {
            nnue_init(evalFile);
        }

        public int NnueEvaluateFen(string fen)
        {
            return nnue_evaluate_fen(fen);
        }

        public int NnueEvaluate(int player, int[] pieces, int[] squares)
        {
            return nnue_evaluate(player, pieces, squares);
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr LoadLibrary(string dllToLoad);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procedureName);

        [DllImport("kernel32.dll")]
        private static extern bool FreeLibrary(IntPtr hModule);

    }
}
