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

                fixed (sbyte* currentAddress = &output[i * OUT_REGISTER_WIDTH])
                {
                    Avx2.Store(currentAddress, result);
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
