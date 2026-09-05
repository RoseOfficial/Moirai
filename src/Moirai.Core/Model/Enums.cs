namespace Moirai.Core.Model;

public enum FateKind { Battle, Boss, Defend, Escort, Collect, NpcStart }
public enum FatePhase { Preparing, Running, Ended, Failed }
public enum InteractableKind { Collectable, ObjectiveNpc, StarterNpc }

// The game dialog open on screen, if any: a Talk window, a yes/no prompt, or the item hand-in window
public enum DialogKind { None, Talk, YesNo, Request }
