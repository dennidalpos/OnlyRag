using System.Buffers.Binary;

namespace OnlyRag.Infrastructure.Storage;

internal static class SqliteVectorBlob
{
    public static byte[] Serialize(IReadOnlyList<float> vector)
    {
        byte[] bytes = new byte[vector.Count * sizeof(float)];
        for (int index = 0; index < vector.Count; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(index * sizeof(float), sizeof(float)), vector[index]);
        }

        return bytes;
    }

    public static float[] Deserialize(byte[] bytes, int dimensions)
    {
        if (bytes.Length != dimensions * sizeof(float))
        {
            return [];
        }

        float[] vector = new float[dimensions];
        for (int index = 0; index < dimensions; index++)
        {
            vector[index] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(index * sizeof(float), sizeof(float)));
        }

        return vector;
    }
}
