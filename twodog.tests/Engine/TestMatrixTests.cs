using twodog.Testing.Xunit;

namespace twodog.tests.EngineTests;

public class TestMatrixTests
{
    private static readonly object?[][] Dimensions = [["easy", "normal", "hard"], [1, 2, 3], [true, false], [null, "save"]];

    [Fact]
    public void CombinatorialProducesTheWholeProductWithoutAliasingRows()
    {
        var rows = TestMatrix.Combinatorial([1, 2], ["a", "b"]).ToArray();
        Assert.Equal(4, rows.Length);
        Assert.Equal([1, "a"], rows[0]);
        Assert.Equal([1, "b"], rows[1]);
        Assert.Equal([2, "a"], rows[2]);
        Assert.Equal([2, "b"], rows[3]);
        Assert.NotSame(rows[0], rows[1]);
    }

    [Fact]
    public void PairwiseCoversEveryPairWithFewerRowsThanTheProduct()
    {
        var rows = TestMatrix.Pairwise(Dimensions).ToArray();
        Assert.True(rows.Length < TestMatrix.Combinatorial(Dimensions).Count());
        foreach (var row in rows)
        {
            Assert.Equal(Dimensions.Length, row.Length);
            for (var index = 0; index < row.Length; index++) Assert.Contains(row[index], Dimensions[index]);
        }
        for (var left = 0; left < Dimensions.Length; left++)
            for (var right = left + 1; right < Dimensions.Length; right++)
                foreach (var a in Dimensions[left])
                    foreach (var b in Dimensions[right])
                        Assert.Contains(rows, row => Equals(row[left], a) && Equals(row[right], b));

        Assert.Equal(rows, TestMatrix.Pairwise(Dimensions).ToArray());
    }

    [Fact]
    public void DegenerateMatricesHaveDefinedResults()
    {
        Assert.Single(TestMatrix.Combinatorial());
        Assert.Single(TestMatrix.Pairwise());
        Assert.Empty(TestMatrix.Pairwise([1], []));
        Assert.Empty(TestMatrix.Combinatorial([1], []));
        Assert.Equal(3, TestMatrix.Pairwise([1, 2, 3]).Count());
        Assert.Equal(6, TestMatrix.Pairwise([1, 2], ["a", "b", "c"]).Count());
        Assert.Throws<ArgumentNullException>(() => TestMatrix.Pairwise(null!));
        Assert.Throws<ArgumentException>(() => TestMatrix.Combinatorial([1], null!));
    }

    public static IEnumerable<object?[]> Rows => TestMatrix.Pairwise([1, 2], ["a", "b"], [true, false]);

    [Theory]
    [MemberData(nameof(Rows))]
    public void RowsWorkWithXunitDiscovery(int number, string text, bool flag)
    {
        Assert.InRange(number, 1, 2);
        Assert.Contains(text, new[] { "a", "b" });
        Assert.IsType<bool>(flag);
    }
}
