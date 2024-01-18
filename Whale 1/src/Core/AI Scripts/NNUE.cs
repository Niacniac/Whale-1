using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using static System.Math;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.CompilerServices;

namespace Whale_1.src.Core.AI_Scripts
{
    public class NNUE
    {
        // Constants
        // Register size of AVX2
        public const int Register_SIZE = 256;
        // Register width for int16
        public const int REGISTER_WIDTH = 256 / 16;

        // The neural network has the format FeatureSet[40960] --> 256x2 -->  32  -->  32  --> 1
        // Based on SFNNv1 architecture (Stockfish 12)         L_0  C_0  L_1  C_1 L_2  C_2 L_3





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
                for (int i = 0; i <= NUM_CHUNKS; i++)
                {
                    fixed (short* currentAddress = &transformer.weight[a, i* REGISTER_WIDTH])
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

        unsafe void UpdateAccumulator(FeatureTransformer transformer, Accumulator newAcc, Accumulator prevAcc, List<int> addedFeatures, List<int> removedFeatures, int perspective)
        {
            // Size of the Feature Transformer  : Output size divided by the register width
            const int NUM_CHUNKS = 256 / REGISTER_WIDTH;
            // Generate the avx2 registers
            Vector256<short>[] registers = new Vector256<short>[NUM_CHUNKS];

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
                for (int i = 0; i < NUM_CHUNKS; i++)
                {
                    fixed (short* currentAddress = &transformer.weight[r, i * REGISTER_WIDTH])
                    {
                        registers[i] = Avx2.Subtract(registers[i], Avx2.LoadVector256(currentAddress));
                    }
                }
            }

            // Add the weight of the added features
            foreach (int a in addedFeatures)
            {
                for (int i = 0; i < NUM_CHUNKS; i++)
                {
                    fixed (short* currentAddress = &transformer.weight[a, i * REGISTER_WIDTH])
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
        }

        float[] Linear(LinearLayer layer, float[] input, float[] output)
        {

            // Copy the biases to the output. We will be adding columns on top of it.
            for (int i = 0; i < layer.Output_size; i++)
            {
                output[i] = layer.bias[i];
            }
            // Adding columns one by one, scaled by the input values.
            for (int i = 0; i < layer.Input_size; i++)
            {
                for (int j = 0;  j < layer.Output_size; j++)
                {
                    output[j] += input[i] * layer.weight[i, j];
                }
            }

            return output;
        }

        unsafe sbyte[] Crelu16(int size, sbyte[] output, short[] inputA, short[] inputB)
        {
            const int IN_REGISTER_WIDTH = 256 / 16;
            const int OUT_REGISTER_WIDTH = 256 / 8;

            int NUM_OUT_CHUNKS = size / OUT_REGISTER_WIDTH;

            const int control = 0b11011000;

            Vector256<sbyte> zero = Vector256<sbyte>.Zero;

            for (int i = 0; i < NUM_OUT_CHUNKS; i++)
            {
                Vector256<sbyte> resultA;

                fixed (short* pointer_0 = &inputA[(i * 2 + 0) * IN_REGISTER_WIDTH], pointer_1 = &inputA[(i * 2 + 1) * IN_REGISTER_WIDTH])
                {
                    Vector256<short> in0 = Avx2.LoadVector256(pointer_0);
                    Vector256<short> in1 = Avx2.LoadVector256(pointer_1);
                    resultA = Avx2.Permute4x64(Avx2.Max(Avx2.PackSignedSaturate(in0, in1).AsSByte(), zero).AsInt64(), control).AsSByte();
                }

                Vector256<sbyte> resultB;

                fixed (short* pointer_0 = &inputB[(i * 2 + 0) * IN_REGISTER_WIDTH], pointer_1 = &inputB[(i * 2 + 1) * IN_REGISTER_WIDTH])
                {
                    Vector256<short> in2 = Avx2.LoadVector256(pointer_0);
                    Vector256<short> in3 = Avx2.LoadVector256(pointer_1);
                    resultB = Avx2.Permute4x64(Avx2.Max(Avx2.PackSignedSaturate(in2, in3).AsSByte(), zero).AsInt64(), control).AsSByte();
                }


                fixed (sbyte* pointer_0 = &output[i * OUT_REGISTER_WIDTH], pointer_1 = &output[(i * OUT_REGISTER_WIDTH) + (output.Length / 2)])
                {
                    Avx2.Store(pointer_0, resultA);
                    Avx2.Store(pointer_1, resultB);
                }              
            }

            return output;
        }

        unsafe sbyte[] Crelu32(int size, sbyte[] output, int[] input)
        {
            const int IN_REGISTER_WIDTH = 256 / 32;
            const int OUT_REGISTER_WIDTH = 256 / 8;

            int NUM_OUT_CHUNKS = size / OUT_REGISTER_WIDTH;

            Vector256<int> control = Vector256.Create(7, 3, 6, 2, 5, 1, 4, 0);
            Vector256<sbyte> zero = Vector256<sbyte>.Zero;

            for (int i = 0; i < NUM_OUT_CHUNKS; i++)
            {
                Vector256<sbyte> result;

                fixed (int* pointer_0 = &input[(i * 4 + 0) * IN_REGISTER_WIDTH], pointer_1 = &input[(i * 4 + 1) * IN_REGISTER_WIDTH],
                    pointer_2 = &input[(i * 4 + 2) * IN_REGISTER_WIDTH], pointer_3 = &input[(i * 4 + 3) * IN_REGISTER_WIDTH])
                {
                    Vector256<short> in0 = Avx2.PackSignedSaturate(Avx2.LoadVector256(pointer_0), Avx2.LoadVector256(pointer_1));
                    Vector256<short> in1 = Avx2.PackSignedSaturate(Avx2.LoadVector256(pointer_2), Avx2.LoadVector256(pointer_3));
                    result = Avx2.PermuteVar8x32(Avx2.Max(Avx2.PackSignedSaturate(in0, in1).AsSByte(), zero).AsInt32(), control).AsSByte();
                }

                fixed (sbyte* currentAddress = &output[i * OUT_REGISTER_WIDTH])
                {
                    Avx2.Store(currentAddress, result);
                }
            }

            return output;
        }

        public static class HalfKP
        {
            // Create the Lists of added features and removed features
            // The Board must be the one before making the move so we can know if a piece has been captured and remove its feature
            // This is used for the update accumulator it must not be used in case of king move (also castling) 
            public static (List<int> addedFeatureIndices, List<int> removedFeatureIndices) CreateChangedIndices(Move move, Board board,int perspective)
            {

                List<int> addedFeatureIndices = new List<int>();
                List<int> removedFeatureIndices = new List<int>();

                
                int kingSquare = Orient(perspective, board.KingSquare[perspective]);

                // All case of removed feature and added feature
                // The Piece that moved has been removed from the square
                removedFeatureIndices.Add(MakeIndex(perspective, move.StartSquare, board.Square[move.StartSquare], kingSquare));
                // The Piece that's been captured
                if (board.Square[move.TargetSquare] != Piece.None)
                {
                    removedFeatureIndices.Add(MakeIndex(perspective, move.TargetSquare, board.Square[move.TargetSquare], kingSquare));
                }
                // The piece promoted is added
                if (move.IsPromotion)
                {
                    addedFeatureIndices.Add(MakeIndex(perspective, move.TargetSquare, move.PromotionPieceType, kingSquare));
                }
                else
                {
                    // if it isn't a promotion then the piece added is the one that was on the start square
                    addedFeatureIndices.Add(MakeIndex(perspective, move.TargetSquare, board.Square[move.StartSquare], kingSquare));
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
                    i++;
                    if(piece == Piece.None)
                    {
                        continue;
                    }
                    activeIndices.Add(MakeIndex(perspective, i, piece, kingSquare));
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


        class Accumulator
        {
            //Accumulator (Acc[1] White, Acc[0] Black)
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

        class LinearLayer
        {
            public short[,] weight;
            public int[] bias;
            public int Input_size = 0, Output_size = 0;
            public LinearLayer(int ColumnSize, int RowSize)
            {
                Input_size = ColumnSize;
                Output_size = RowSize;
                weight = new short[ColumnSize,RowSize];
                bias = new int[RowSize];
            }
        }

        class FeatureTransformer
        {
            public short[,] weight;
            public short[] bias;
            public FeatureTransformer(int ColumnSize, int RowSize)
            {
                weight = new short[ColumnSize, RowSize];
                bias = new short[RowSize];
            }
        }

    }
}
