using System.Runtime.InteropServices;

namespace WorldMonitor.Data.Ml;

/// <summary>Brute-force cosine similarity + Float32[]↔byte[] conversion. Mirrors the legacy
/// cosineSimilarityF32 (dot/(|a||b|), 0 on a zero-norm vector).</summary>
public static class VectorMath
{
    public static double Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length) return 0;
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += (double)a[i] * b[i];
            na += (double)a[i] * a[i];
            nb += (double)b[i] * b[i];
        }
        if (na == 0 || nb == 0) return 0;
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    public static byte[] ToBytes(ReadOnlySpan<float> v)
    {
        var bytes = new byte[v.Length * sizeof(float)];
        MemoryMarshal.AsBytes(v).CopyTo(bytes);
        return bytes;
    }

    public static float[] ToFloats(ReadOnlySpan<byte> b)
    {
        var floats = new float[b.Length / sizeof(float)];
        b.CopyTo(MemoryMarshal.AsBytes(floats.AsSpan()));
        return floats;
    }
}
