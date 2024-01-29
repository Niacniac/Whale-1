using System;
using System.IO;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using Whale_1.src.Core.AI_Scripts;

public class ReadEvalFile
{
    public static bool ReadNetFile(string resourceName, out uint hashValue, out string architecture, NNUE.FeatureTransformer ft, NNUE.LinearLayer HL1, 
        NNUE.LinearLayer HL2, NNUE.LinearLayer OL)
    {
        hashValue = 0;
        architecture = null;

        try
        {
            // Read the header from the embedded resource
            using (Stream resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            using (BinaryReader binaryReader = new BinaryReader(resourceStream))
            {
                // Use the ReadHeader method to read the header
                bool headerCheck = ReadHeader(binaryReader, out hashValue, out architecture);

            }

            //Read all the parameters 
            using (Stream resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            using (BinaryReader binaryReader = new BinaryReader(resourceStream))
            {
                binaryReader.ReadBytes(3 * 4 + 177 + 4);
                ReadTransformerParameters(binaryReader, ft);
                binaryReader.ReadBytes(4);
                ReadLinearLayerParameter(binaryReader, HL1);
                ReadLinearLayerParameter(binaryReader, HL2);
                ReadLinearLayerParameter(binaryReader, OL);
            }
            
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading file header: {ex.Message}");
            return false;
        }
    }

    private static bool ReadHeader(BinaryReader binaryReader, out uint hashValue, out string architecture)
    {
        hashValue = 0;
        architecture = null;

        // Read version, hash_value, and size from the stream using little-endian format
        uint version = ReadLittleEndian<uint>(binaryReader.BaseStream);
        hashValue = ReadLittleEndian<uint>(binaryReader.BaseStream);
        uint size = ReadLittleEndian<uint>(binaryReader.BaseStream);

        // Read the architecture string from the stream
        int architectureSize = (int)size;
        byte[] architectureBytes = new byte[architectureSize];
        binaryReader.Read(architectureBytes, 0, architectureSize);

        // Convert the byte array to a string
        architecture = System.Text.Encoding.UTF8.GetString(architectureBytes);

        return true;
    }

    private static void ReadTransformerParameters(BinaryReader binaryReader, NNUE.FeatureTransformer ft)
    {
        try
        {
            for (int i = 0; i < ft.Output_size; i++)
            {
                ft.bias[i] = ReadLittleEndian<short>(binaryReader.BaseStream);
            }
            for (int i = 0; i < ft.Input_size * ft.Output_size; i++)
            {
                ft.weight[i] = ReadLittleEndian<short>(binaryReader.BaseStream);
            }
        }
        catch (Exception e) { }
    }

    private static void ReadLinearLayerParameter(BinaryReader binaryReader, NNUE.LinearLayer linear)
    {
        try
        {
            for (int i = 0; i < linear.Output_size; i++)
            {
                linear.bias[i] = ReadLittleEndian<int>(binaryReader.BaseStream);
            }
            for (int i = 0; i < (linear.Input_size * linear.Output_size); i++)
            {
                linear.weight[i] = ReadLittleEndian<sbyte>(binaryReader.BaseStream);
            }

        }
        catch (Exception e) { }
    }

    public static T ReadLittleEndian<T>(Stream stream) where T : struct
    {
        BinaryReader reader = new BinaryReader(stream);

        if (typeof(T) == typeof(int))
            return (T)(object)reader.ReadInt32();
        else if (typeof(T) == typeof(uint))
            return (T)(object)reader.ReadUInt32();
        else if (typeof(T) == typeof(short))
            return (T)(object)reader.ReadInt16();
        else if (typeof(T) == typeof(ushort))
            return (T)(object)reader.ReadUInt16();
        else if (typeof(T) == typeof(sbyte))
            return (T)(object)(sbyte)reader.ReadSByte();

        throw new NotSupportedException($"Type {typeof(T).Name} is not supported.");
    }
}
