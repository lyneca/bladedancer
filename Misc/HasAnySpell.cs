using ThunderRoad;
using ThunderRoad.AI;

namespace Bladedancer;

public partial class HasAnySpell : ConditionNode {
    public override bool Evaluate() {
        if (!creature.mana) return false;
        foreach (var spellData in creature.mana.spells) {
            if (spellData is SpellCastData) {
                return true;
            }
        }

        return false;
    }
}
