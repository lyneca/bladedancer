using ThunderRoad;
using ThunderRoad.Skill;

namespace Bladedancer.Skills;

public class SkillImbueBlades : AISkillData {
    public const string ImbueBladeSpell = "ImbueBladeSpell";
    public bool random = false;
    public string spellId;
    public SpellCastCharge spellData;

    public override void OnCatalogRefresh() {
        base.OnCatalogRefresh();
        spellData = Catalog.GetData<SpellCastCharge>(spellId);
    }

    public override void OnLateSkillsLoaded(SkillData skillData, Creature creature) {
        base.OnLateSkillsLoaded(skillData, creature);
        var spell = spellData;

        if (random) {
            Catalog.GetDataList<SpellCastCharge>()
                .RandomFilteredSelectInPlace(
                    each => each.imbueEnabled
                            && each.showInTree
                            && each.allowSkill
                            && !each.hideInSkillMenu
                            && each is not SpellCastSlingblade, out spell);
        }

        spell.spellCaster = creature.handRight.caster;

        creature.SetVariable(ImbueBladeSpell, spell);
    }

    public override void OnSpellLoad(SpellData spell, SpellCaster caster = null) {
        base.OnSpellLoad(spell, caster);
        if (spell is not SpellCastSlingblade blade
            || caster == null
            || !caster.mana.creature.TryGetVariable(ImbueBladeSpell, out SpellCastCharge imbueSpell)) return;

        blade.quiver.MaxImbue(imbueSpell);

        blade.OnBladeSpawnEvent -= OnBladeSpawn;
        blade.OnBladeSpawnEvent += OnBladeSpawn;
        blade.quiver.OnBladeAddEvent -= OnBladeAdd;
        blade.quiver.OnBladeAddEvent += OnBladeAdd;
    }

    public override void OnSpellUnload(SpellData spell, SpellCaster caster = null) {
        base.OnSpellUnload(spell, caster);
        if (spell is not SpellCastSlingblade blade) return;
        blade.OnBladeSpawnEvent -= OnBladeSpawn;
        blade.quiver.OnBladeAddEvent -= OnBladeAdd;
    }

    private void OnBladeSpawn(SpellCastSlingblade bladeSpell, Blade blade) {
        if (!blade
            || !blade.item
            || blade.item.colliderGroups is not { Count: > 0 } groups
            || groups[0].imbue is not Imbue imbue
            || bladeSpell?.spellCaster == null
            || !bladeSpell.spellCaster.mana.creature.TryGetVariable(ImbueBladeSpell, out SpellCastCharge spell)
            || spell == null)
            return;

        if (imbue.spellCastBase is SpellCastCharge currentSpell
            && currentSpell.hashId != spell.hashId) {
            imbue.SetEnergyInstant(0);
        }

        imbue.Transfer(spell, imbue.maxEnergy, bladeSpell.spellCaster.mana.creature);
    }

    private void OnBladeAdd(Quiver quiver, Blade blade) {
        if (quiver.creature.TryGetVariable(ImbueBladeSpell, out SpellCastCharge spell))
            quiver.MaxImbue(spell);
    }
}