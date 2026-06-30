namespace SpriteForge.Core.Rendering;

/// <summary>
/// Computes the list of camera yaw angles (in degrees) for a given direction count.
/// Yaw is added to the base camera angle; pitch stays fixed.
/// </summary>
public static class DirectionScheduler
{
    /// <summary>
    /// Returns the yaw angles in degrees for the requested number of directions,
    /// evenly distributed around the model starting at 0 (front).
    /// </summary>
    /// <param name="directions">The number of directions: 2, 4, or 8.</param>
    /// <returns>
    /// 2 → [0, 180]; 4 → [0, 90, 180, 270]; 8 → [0, 45, 90, 135, 180, 225, 270, 315].
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="directions"/> is not 2, 4, or 8.</exception>
    public static IReadOnlyList<float> GetYaws(int directions)
    {
        if (directions is not (2 or 4 or 8))
        {
            throw new ArgumentOutOfRangeException(
                nameof(directions),
                directions,
                "Directions must be 2, 4, or 8.");
        }

        float step = 360f / directions;
        var yaws = new float[directions];
        for (int i = 0; i < directions; i++)
        {
            yaws[i] = i * step;
        }

        return yaws;
    }
}
