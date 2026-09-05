using Moirai.Core.Behaviors;
using Moirai.Core.Planning;

namespace Moirai.Core.Replay;

// Everything a Director is built from, so a recording can rebuild the same planner (§12)
public sealed record RunSettings(
    SelectionConfig Selection,
    MovementConfig Movement,
    EngageConfig Engage,
    CompanionConfig Companion,
    DirectorConfig Director);
