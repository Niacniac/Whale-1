﻿using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Versioning;

namespace Whale_1.src.Core.AI_Scripts
{
    public class NNUE
    {
        // Constants
        public const string netDirectory = "Whale_1.resources.nn-62ef826d1a6d.nnue";
        // Register size of AVX2
        public const int Register_SIZE = 256;
        // Register width for int16
        public const int REGISTER_WIDTH = 256 / 16;
        //Log2 of weigtscale
        const byte log2WeightScale = 6;

        // The neural network has the format FeatureSet[41600] --> 256x2 -->  32  -->  32  --> 1
        // Based on SFNNv1 architecture (Stockfish 12)         L_0  C_0  L_1  C_1 L_2  C_2 L_3

        const int KHALFDIMENSION = 256;

        //input into the feature transformer
        public List<int>[] features = new List<int>[2];

        //feature transformer
        readonly FeatureTransformer transformer = new(41024, KHALFDIMENSION);

        //output of the Feature transformers
        public Accumulator acc = new(KHALFDIMENSION);

        // Hidden layer 1
        readonly LinearLayer HiddenLayer1 = new(512, 32);

        // Hidden layer 2
        readonly LinearLayer HiddenLayer2 = new(32, 32);

        // Output layer
        readonly LinearLayer OutputLayer = new(32, 1);

        const string TestFEN = "r1bqkbnr/pP3ppp/2n5/4p3/8/3P4/PPP2PPP/RNBQKBNR w KQkq - 1 6";

        public NNUE() 
        {
            InitNet();
            Board testBoard = new Board();
            testBoard.LoadPosition(FenUtility.PositionFromFen(TestFEN));
            features[0] = HalfKP.CreateActiveIndices(testBoard, 0);
            features[1] = HalfKP.CreateActiveIndices(testBoard, 1);
            RefreshAccumulator(transformer, acc, features[0], 0);
            RefreshAccumulator(transformer, acc, features[1], 1);

            Move testMove = new Move(49, 57, Move.PromoteToQueenFlag);

            for (int i = 0; i < 2; i++)
            {
                (List<int> addedFeatures, List<int> removedFeatures) = HalfKP.CreateChangedIndices(testMove, testBoard, i);
                acc = UpdateAccumulator(transformer, acc, addedFeatures, removedFeatures, i);
            }

            int testscore = EvaluateNNUE(0);
            int testscore1 = EvaluateNNUE(1);
            Console.WriteLine($" white : {testscore}, black : {testscore1}");
        }

        void InitNet()
        {
            string architecture = string.Empty;
            uint hashvalue;

            ReadEvalFile.ReadNetFile(netDirectory, out hashvalue, out architecture, transformer, HiddenLayer1, HiddenLayer2, OutputLayer);

            Console.WriteLine($"Architecture : {architecture} \n hashValue : {hashvalue}");
        }


        // bug test : crelu32 & 16 work, need to create a special function for the output layer
        public int EvaluateNNUE(int sideToMove)
        {
            short[] input = new short[512];
            int notSideToMove = 1 - sideToMove;


            for (int i = 0; i < 256; i++)
            {
                input[i] = acc.accu[sideToMove][i];
                input[i + 256] = acc.accu[notSideToMove][i];
            }
            sbyte[] reluOut0 = new sbyte[2 * 256];
            reluOut0 = Crelu16(input.Length, reluOut0, input);

            int[] linearOut1 = new int[HiddenLayer1.Output_size];
            linearOut1 = DenseLinear(HiddenLayer1, linearOut1, reluOut0);

            sbyte[] reluOut1 = new sbyte[HiddenLayer1.Output_size];
            reluOut1 = Crelu32(linearOut1.Length, reluOut1, linearOut1);

            int[] linearOut2 = new int[HiddenLayer2.Output_size];
            linearOut2 = DenseLinear(HiddenLayer2, linearOut2, reluOut1); 

            sbyte[] reluOut2 = new sbyte[HiddenLayer2.Output_size];
            reluOut2 = Crelu32(linearOut2.Length, reluOut2, linearOut2);

            int RawOutput = LinearOutput(OutputLayer, reluOut2) / 16;

            int netOutput = sideToMove == 0 ? RawOutput : -RawOutput;

            return netOutput;
        }

        unsafe void RefreshAccumulator(FeatureTransformer transformer, Accumulator acc, List<int> activeFeatures, int perspective)
        {
            // Size of the Feature Transformer  : Output size divided by the register width
            const int NUM_CHUNKS = 256 / REGISTER_WIDTH;
            // Generate the avx2 registers
            Vector256<short>[] registers = new Vector256<short>[NUM_CHUNKS];

            // Load bias to register
            for (int i = 0; i < NUM_CHUNKS; i++)
            {
                fixed (short* currentAddress = &transformer.bias[i * REGISTER_WIDTH])
                {
                    registers[i] = Avx2.LoadVector256(currentAddress);
                }
            }

            //Add the weights
            foreach (int a in activeFeatures)
            {
                int offset = a * KHALFDIMENSION;
                for (int i = 0; i < NUM_CHUNKS; i++)
                {
                    fixed (short* currentAddress = &transformer.weight[offset + i * REGISTER_WIDTH])
                    {
                        registers[i] = Avx2.Add(registers[i], Avx2.LoadVector256(currentAddress));
                    }
                }
            }

            // Store the registers into the accumulator
            for (int i= 0; i < NUM_CHUNKS; i++)
            {
                fixed (short* currentAddress = &acc.accu[perspective][i * REGISTER_WIDTH])
                {
                    Avx2.Store(currentAddress, registers[i]);
                }
            }
        }

        unsafe Accumulator UpdateAccumulator(FeatureTransformer transformer, Accumulator prevAcc, List<int> addedFeatures, List<int> removedFeatures, int perspective)
        {
            // Size of the Feature Transformer  : Output size divided by the register width
            const int NUM_CHUNKS = 256 / REGISTER_WIDTH;
            // Generate the avx2 registers
            Vector256<short>[] registers = new Vector256<short>[NUM_CHUNKS];
            Accumulator newAcc = new Accumulator(KHALFDIMENSION);


            // Load the previous values to registers
            for (int i = 0; i < NUM_CHUNKS; i++)
            {
                fixed (short* currentAddress = &prevAcc.accu[perspective][i * REGISTER_WIDTH])
                {
                    registers[i] = Avx2.LoadVector256(currentAddress);
                }
            }

            // Substract the weights of the removed features
            foreach (int r in removedFeatures)
            {
                int offset = r * KHALFDIMENSION;
                for (int i = 0; i < NUM_CHUNKS; i++)
                {
                    fixed (short* currentAddress = &transformer.weight[offset + i * REGISTER_WIDTH])
                    {
                        registers[i] = Avx2.Subtract(registers[i], Avx2.LoadVector256(currentAddress));
                    }
                }
            }

            // Add the weight of the added features
            foreach (int a in addedFeatures)
            {
                int offset = a * KHALFDIMENSION;
                for (int i = 0; i < NUM_CHUNKS; i++)
                {
                    fixed (short* currentAddress = &transformer.weight[offset + i * REGISTER_WIDTH])
                    {
                        registers[i] = Avx2.Add(registers[i], Avx2.LoadVector256(currentAddress));
                    }
                }
            }

            // Store the registers into the accumulator
            for (int i = 0; i < NUM_CHUNKS; i++)
            {
                fixed (short* currentAddress = &newAcc.accu[perspective][i * REGISTER_WIDTH])
                {
                    Avx2.Store(currentAddress, registers[i]);
                }
            }

            // copy the accumulator from the other side
            newAcc.accu[1 - perspective] = prevAcc.accu[1 - perspective];

            return newAcc;
        } 

        // ClippedReLU from the output of the accumulutor in int16 
        unsafe sbyte[] Crelu16(int size, sbyte[] output, short[] input)
        {
            const int IN_REGISTER_WIDTH = 256 / 16;
            const int OUT_REGISTER_WIDTH = 256 / 8;

            int NUM_OUT_CHUNKS = size / OUT_REGISTER_WIDTH;

            const int control = 0b11011000;

            Vector256<sbyte> zero = Vector256<sbyte>.Zero;

            for (int i = 0; i < NUM_OUT_CHUNKS; i++)
            {
                Vector256<sbyte> result;

                fixed (short* pointer_0 = &input[(i * 2 + 0) * IN_REGISTER_WIDTH], pointer_1 = &input[(i * 2 + 1) * IN_REGISTER_WIDTH])
                {
                    Vector256<short> in0 = Avx2.LoadVector256(pointer_0);
                    Vector256<short> in1 = Avx2.LoadVector256(pointer_1);
                    result = Avx2.Permute4x64(Avx2.Max(Avx2.PackSignedSaturate(in0, in1).AsSByte(), zero).AsInt64(), control).AsSByte();
                }



                fixed (sbyte* currentAdress = &output[i * OUT_REGISTER_WIDTH])
                {
                    Avx2.Store(currentAdress, result);
                }              
            }

            return output;
        }

        unsafe sbyte[] Crelu32(int size, sbyte[] output, int[] input)
        {
            const int IN_REGISTER_WIDTH = 256 / 32;
            const int OUT_REGISTER_WIDTH = 256 / 8;

            int NUM_OUT_CHUNKS = size / OUT_REGISTER_WIDTH;

            Vector256<int> control = Vector256.Create(0,4,1,5,2,6,3,7);
            Vector256<sbyte> zero = Vector256<sbyte>.Zero;

            for (int i = 0; i < NUM_OUT_CHUNKS; i++)
            {
                Vector256<sbyte> result;

                fixed (int* pointer_0 = &input[(i * 4 + 0) * IN_REGISTER_WIDTH], pointer_1 = &input[(i * 4 + 1) * IN_REGISTER_WIDTH],
                    pointer_2 = &input[(i * 4 + 2) * IN_REGISTER_WIDTH], pointer_3 = &input[(i * 4 + 3) * IN_REGISTER_WIDTH])
                {
                    Vector256<short> in0 = Avx2.PackSignedSaturate(Avx2.LoadVector256(pointer_0), Avx2.LoadVector256(pointer_1));
                    Vector256<short> in1 = Avx2.PackSignedSaturate(Avx2.LoadVector256(pointer_2), Avx2.LoadVector256(pointer_3));
                    result = Avx2.PermuteVar8x32(Avx2.Max(Avx2.PackSignedSaturate(in0, in1), zero).AsInt32(), control).AsSByte();

                    fixed (sbyte* currentAddress = &output[i * OUT_REGISTER_WIDTH])
                    {
                        Avx2.Store(currentAddress, result);
                    }
                }
            }

            return output;
        }

        unsafe int[] DenseLinear(LinearLayer layer, int[] output, sbyte[] input)
        {
            const int REGISTER_WIDTH = 256 / 8;
            int numInChunks = layer.Input_size / REGISTER_WIDTH;
            int numOutChunks = layer.Output_size / 4;

            for (int i = 0; i < numOutChunks; i++) 
            {
                int offset0 = (i * 4 + 0) * layer.Input_size;
                int offset1 = (i * 4 + 1) * layer.Input_size;
                int offset2 = (i * 4 + 2) * layer.Input_size;
                int offset3 = (i * 4 + 3) * layer.Input_size;

                Vector256<int> sum0 = Vector256<int>.Zero;
                Vector256<int> sum1 = Vector256<int>.Zero;
                Vector256<int> sum2 = Vector256<int>.Zero;
                Vector256<int> sum3 = Vector256<int>.Zero;

                for (int j = 0; j < numInChunks; j++)
                {
                    fixed (sbyte* currentAddress = &input[j * REGISTER_WIDTH], pointer0 = &layer.weight[offset0 + j * REGISTER_WIDTH], pointer1 = &layer.weight[offset1 + j * REGISTER_WIDTH],
                         pointer2 = &layer.weight[offset2 + j * REGISTER_WIDTH], pointer3 = &layer.weight[offset3 + j * REGISTER_WIDTH])
                    {
                        Vector256<sbyte> IN = Avx.LoadVector256(currentAddress);

                        sum0 = AvxVnni.MultiplyWideningAndAdd(sum0, IN.AsByte(), Avx.LoadVector256(pointer0));
                        sum1 = AvxVnni.MultiplyWideningAndAdd(sum1, IN.AsByte(), Avx.LoadVector256(pointer1));
                        sum2 = AvxVnni.MultiplyWideningAndAdd(sum2, IN.AsByte(), Avx.LoadVector256(pointer2));
                        sum3 = AvxVnni.MultiplyWideningAndAdd(sum3, IN.AsByte(), Avx.LoadVector256(pointer3));

                    }
                }

                Vector128<int> outval;
                fixed (int* currentAddress = &layer.bias[i * 4])
                {
                    Vector128<int> bias = Avx.LoadVector128(currentAddress);
                    sum0 = Avx2.HorizontalAdd(sum0, sum1);
                    sum2 = Avx2.HorizontalAdd(sum2, sum3);

                    sum0 = Avx2.HorizontalAdd(sum0, sum2);

                    Vector128<int> sum128lo = Avx2.ExtractVector128(sum0, 0);
                    Vector128<int> sum128hi = Avx2.ExtractVector128(sum0, 1);

                    outval = Avx2.Add(Avx2.Add(sum128lo, sum128hi), bias);
                }

                fixed(int* currentAddress = &output[i  * 4])
                {
                    outval = Avx2.ShiftRightArithmetic(outval, log2WeightScale);
                    Avx2.Store(currentAddress, outval);
                }
            }

            return output;
        }

        unsafe int LinearOutput(LinearLayer outputLayer, sbyte[] input)
        {
            const int REGISTER_WIDTH = 256 / 8;
            int numInChunks = outputLayer.Input_size / REGISTER_WIDTH;

            Vector256<int> sum = Vector256<int>.Zero;
            int[] intermediateArray = new int[REGISTER_WIDTH / 4];
            int outputValue = 0;

            for (int i = 0; i < numInChunks; i++)
            {
                fixed (sbyte* Pointer_0 = &input[i * REGISTER_WIDTH], Pointer_1 = &outputLayer.weight[i * REGISTER_WIDTH])
                {
                    Vector256<sbyte> IN = Avx2.LoadVector256(Pointer_0);
                    sum = AvxVnni.MultiplyWideningAndAdd(sum, IN.AsByte(), Avx2.LoadVector256(Pointer_1)); 
                }

                fixed (int* currentAddress = &intermediateArray[0])
                {
                    Avx2.Store(currentAddress, sum);
                }

                foreach (int value in intermediateArray)
                {
                    outputValue += value;
                }
            }

            return outputValue + outputLayer.bias[0];
        }

        public static class HalfKP
        {
            // Create the Lists of added features and removed features
            // The Board must be the one before making the move so we can know if a piece has been captured and remove its feature
            // This is used for the update accumulator it must not be used in case of king move (also castling) 
            public static (List<int> addedFeatureIndices, List<int> removedFeatureIndices) CreateChangedIndices(Move move, Board pastBoard, int perspective)
            {

                List<int> addedFeatureIndices = new List<int>();
                List<int> removedFeatureIndices = new List<int>();

                
                int kingSquare = Orient(perspective, pastBoard.KingSquare[perspective]);

                // All case of removed feature and added feature
                // The Piece that moved has been removed from the square
                removedFeatureIndices.Add(MakeIndex(perspective, move.StartSquare, pastBoard.Square[move.StartSquare], kingSquare));
                // The Piece that's been captured
                if (pastBoard.Square[move.TargetSquare] != Piece.None)
                {
                    removedFeatureIndices.Add(MakeIndex(perspective, move.TargetSquare, pastBoard.Square[move.TargetSquare], kingSquare));
                }
                // The piece promoted is added
                if (move.IsPromotion)
                {
                    addedFeatureIndices.Add(MakeIndex(perspective, move.TargetSquare, move.PromotionPieceType, kingSquare));
                }
                else
                {
                    // if it isn't a promotion then the piece added is the one that was on the start square
                    addedFeatureIndices.Add(MakeIndex(perspective, move.TargetSquare, pastBoard.Square[move.StartSquare], kingSquare));
                }

                return (addedFeatureIndices, removedFeatureIndices);
            }




            public static List<int> CreateActiveIndices(Board board, int perspective)
            {
                List<int> activeIndices = new List<int>();

                int kingSquare = Orient(perspective, board.KingSquare[perspective]);
                int i = 0;
                foreach (int piece in board.Square)
                {
                    if(piece == Piece.None || piece == Piece.WhiteKing || piece == Piece.BlackKing)
                    {
                        i++;
                        continue;
                    }
                    activeIndices.Add(MakeIndex(perspective, i, piece, kingSquare));
                    i++;
                }
                return activeIndices;
            }

            // Make the HalfKP index from the square, piece and kingSquare
            // The perspective is used since we want the wight and black POV
            static int MakeIndex(int perspective, int square, int piece, int kingSquare)
            {
                return Orient(perspective, square) + kpp_board_index[piece, perspective] + PS_END * kingSquare;
            }

            // we orient for the according perspective 0 = white, 1 = black based on the board
            static int Orient(int perspective, int square)
            {
                return square ^ (perspective * 63);
            }

            // Used to get the index of the feature
            static int[,] kpp_board_index = {
                // convention: W - us, B - them
                // viewed from the other side, W and B are reversed
                { PS_NONE, PS_NONE },
                { PS_W_PAWN, PS_B_PAWN },
                { PS_W_KNIGHT, PS_B_KNIGHT },
                { PS_W_BISHOP, PS_B_BISHOP },
                { PS_W_ROOK, PS_B_ROOK },
                { PS_W_QUEEN, PS_B_QUEEN },
                { PS_W_KING, PS_B_KING },
                { PS_NONE, PS_NONE },
                { PS_NONE, PS_NONE },
                { PS_B_PAWN, PS_W_PAWN },
                { PS_B_KNIGHT, PS_W_KNIGHT },
                { PS_B_BISHOP, PS_W_BISHOP },
                { PS_B_ROOK, PS_W_ROOK },
                { PS_B_QUEEN, PS_W_QUEEN },
                { PS_B_KING, PS_W_KING },
                { PS_NONE, PS_NONE }
            };

            const int PIECE_NB = 16;
            const int COLOR_NB = 2;

            const int PS_NONE = 0;
            const int PS_W_PAWN = 1;
            const int PS_B_PAWN = 1 * 64 + 1;
            const int PS_W_KNIGHT = 2 * 64 + 1;
            const int PS_B_KNIGHT = 3 * 64 + 1;
            const int PS_W_BISHOP = 4 * 64 + 1;
            const int PS_B_BISHOP = 5 * 64 + 1;
            const int PS_W_ROOK = 6 * 64 + 1;
            const int PS_B_ROOK = 7 * 64 + 1;
            const int PS_W_QUEEN = 8 * 64 + 1;
            const int PS_B_QUEEN = 9 * 64 + 1;
            const int PS_W_KING = 10 * 64 + 1;
            const int PS_END = PS_W_KING; // pieces without kings (pawns included)
            const int PS_B_KING = 11 * 64 + 1;
            const int PS_END2 = 12 * 64 + 1;

            const int SQUARE_NONE = 64;
        }

        public class Accumulator
        {
            //Accumulator (Acc[0] White, Acc[1] Black)
            public short[][] accu;

            public Accumulator(int Size)
            {
                accu = new short[2][];
                accu[0] = new short[Size];
                accu[1] = new short[Size];
            }

            public short[] ReturnSide(byte color)
            {
                return accu[color];
            }
        }

        public class LinearLayer
        {
            public sbyte[] weight; // we use a 1D array instead of 2D
            public int[] bias;
            public int Input_size;
            public int Output_size;
            public LinearLayer(int ColumnSize, int RowSize)
            {
                Input_size = ColumnSize;
                Output_size = RowSize;
                weight = new sbyte[ColumnSize * RowSize];
                bias = new int[RowSize];
            }
        }

        public class FeatureTransformer
        {
            public short[] weight;// we use a 1D array instead of 2D
            public short[] bias;
            public int Input_size;
            public int Output_size;
            public FeatureTransformer(int ColumnSize, int RowSize)
            {
                Input_size = ColumnSize;
                Output_size = RowSize;
                weight = new short[ColumnSize * RowSize];
                bias = new short[RowSize];
            }
        }

    }
}
