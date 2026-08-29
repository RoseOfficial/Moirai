using System.Numerics;

namespace Moirai.Core.Model;

public sealed record InteractableSnapshot(
    ulong Id,
    Vector3 Position,
    uint FateId,
    InteractableKind Kind);
