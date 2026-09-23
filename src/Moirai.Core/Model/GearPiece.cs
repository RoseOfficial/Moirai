namespace Moirai.Core.Model;

// §7.5: one equipped piece that takes wear, with its condition and whether self-repair can mend it
// now: the level of the class that repairs it, and Dark Matter of the grade it takes or better
public sealed record GearPiece(int ConditionPercent, bool SelfRepairable);
