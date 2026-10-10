using System;
using System.Collections.Generic;
using System.Linq;

namespace twodog.Testing.Xunit;

/// <summary>Managed test rows for MemberData. Do not construct Godot objects during discovery.</summary>
public static class TestMatrix
{
    /// <summary>Every combination of the supplied dimensions, in input order.</summary>
    public static IEnumerable<object?[]> Combinatorial(params object?[][] dimensions)
    {
        Validate(dimensions);
        return Combine(0, new object?[dimensions.Length]);

        IEnumerable<object?[]> Combine(int index, object?[] row)
        {
            if (index == dimensions.Length)
            {
                yield return (object?[])row.Clone();
                yield break;
            }
            foreach (var value in dimensions[index])
            {
                row[index] = value;
                foreach (var result in Combine(index + 1, row)) yield return result;
            }
        }
    }

    /// <summary>
    /// Deterministic rows covering every pair of values from different dimensions.
    /// Uses a greedy reduction, not a guarantee of the smallest possible matrix.
    /// </summary>
    public static IEnumerable<object?[]> Pairwise(params object?[][] dimensions)
    {
        Validate(dimensions);
        if (dimensions.Length < 2) return Combinatorial(dimensions);
        if (dimensions.Any(d => d.Length == 0)) return [];
        return CoverPairs();

        IEnumerable<object?[]> CoverPairs()
        {
            var uncovered = new SortedSet<(int Left, int Right, int A, int B)>();
            for (var left = 0; left < dimensions.Length; left++)
                for (var right = left + 1; right < dimensions.Length; right++)
                    for (var a = 0; a < dimensions[left].Length; a++)
                        for (var b = 0; b < dimensions[right].Length; b++)
                            uncovered.Add((left, right, a, b));

            while (uncovered.Count > 0)
            {
                var row = Enumerable.Repeat(-1, dimensions.Length).ToArray();
                var seed = uncovered.Min;
                row[seed.Left] = seed.A;
                row[seed.Right] = seed.B;
                for (var index = 0; index < row.Length; index++)
                {
                    if (row[index] >= 0) continue;
                    var best = 0;
                    var bestScore = -1;
                    for (var value = 0; value < dimensions[index].Length; value++)
                    {
                        var score = 0;
                        for (var other = 0; other < row.Length; other++)
                        {
                            if (row[other] < 0) continue;
                            var pair = index < other ? (index, other, value, row[other]) : (other, index, row[other], value);
                            if (uncovered.Contains(pair)) score++;
                        }
                        if (score > bestScore) { bestScore = score; best = value; }
                    }
                    row[index] = best;
                }

                for (var left = 0; left < row.Length; left++)
                    for (var right = left + 1; right < row.Length; right++)
                        uncovered.Remove((left, right, row[left], row[right]));
                yield return row.Select((value, index) => dimensions[index][value]).ToArray();
            }
        }
    }

    private static void Validate(object?[][] dimensions)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        if (dimensions.Any(d => d is null)) throw new ArgumentException("A test dimension cannot be null.", nameof(dimensions));
    }
}
