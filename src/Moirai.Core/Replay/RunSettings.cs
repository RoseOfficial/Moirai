using Moirai.Core.Behaviors;
using Moirai.Core.Modules;
using Moirai.Core.Planning;

namespace Moirai.Core.Replay;

// Everything a Director is built from, so a recording can rebuild the same planner (§12).
// The yokai module when enabled, else rotation when zones are listed, else the single zone.
public sealed record RunSettings(
    SelectionConfig Selection,
    MovementConfig Movement,
    EngageConfig Engage,
    CompanionConfig Companion,
    DirectorConfig Director,
    RotationConfig? Rotation = null,
    YokaiConfig? Yokai = null);
