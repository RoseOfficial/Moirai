using System.Numerics;

namespace Moirai.Core.Model;

public static class Geometry
{
    // Ground-plane distance: fate rings and dropoff arrival ignore elevation
    public static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }
}
