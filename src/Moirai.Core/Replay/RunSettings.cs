using Moirai.Core.Behaviors;
using Moirai.Core.Modules;
using Moirai.Core.Planning;

namespace Moirai.Core.Replay;

// Everything a Director is built from, so a recording can rebuild the same planner (§12).
// Rotation null or empty means the single-zone module.
public sealed record RunSettings(
    SelectionConfig Selection,
    MovementConfig Movement,
    EngageConfig Engage,
    CompanionConfig Companion,
    DirectorConfig Director,
    RotationConfig? Rotation = null);
