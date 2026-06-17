using WorldMonitor.Data.Ml;
using Xunit;

namespace WorldMonitor.Data.Tests.Unit;

public class VectorMathTests
{
    [Fact]
    public void Cosine_of_identical_unit_vectors_is_one()
        => Assert.Equal(1.0, VectorMath.Cosine([1f, 0f, 0f], [1f, 0f, 0f]), 6);

    [Fact]
    public void Cosine_of_orthogonal_is_zero()
        => Assert.Equal(0.0, VectorMath.Cosine([1f, 0f], [0f, 1f]), 6);

    [Fact]
    public void Cosine_with_a_zero_vector_is_zero_not_NaN()
        => Assert.Equal(0.0, VectorMath.Cosine([0f, 0f], [1f, 2f]));

    [Fact]
    public void Float_byte_round_trip_preserves_values()
    {
        float[] v = [0.5f, -1.25f, 3.0f, 0f];
        Assert.Equal(v, VectorMath.ToFloats(VectorMath.ToBytes(v)));
    }
}
