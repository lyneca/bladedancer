using ThunderRoad;
using ThunderRoad.Skill;

namespace Bladedancer.Skills;

public class SkillTwinBladeMaestro : SpellSkillData {
    public static float shootVelocityThreshold = 4f;
    public static float throwMult = 2f;
    public static bool enabled = false;

    public delegate void SkillEnableEvent(SkillData data, Creature creature);

    public static event SkillEnableEvent OnSkillEnableEvent;
    public static event SkillEnableEvent OnSkillDisableEvent;

    public static void GiveSkill() {
        Player.currentCreature.container.AddSkillContent("TwinBladeMaestro");
        DisplayMessage.instance.ShowMessage(new DisplayMessage.MessageData(ItemModuleTwinBlade.skillData.description, 0));
    }

    public static void RemoveSkill() {
        Player.currentCreature.container.RemoveContent("TwinBladeMaestro");
    }

    public override void OnCatalogRefresh() {
        base.OnCatalogRefresh();
        IngameDebugConsole.DebugLogConsole.AddCommand("bd-spoil", "Spoil the secret of Bladedancing for yourself",
            GiveSkill);
        IngameDebugConsole.DebugLogConsole.AddCommand("bd-unspoil", "Remove the secret of Bladedancing from yourself",
            RemoveSkill);
    }

    public override void OnSkillLoaded(SkillData skillData, Creature creature) {
        base.OnSkillLoaded(skillData, creature);
        enabled = true;
        OnSkillEnableEvent?.Invoke(skillData, creature);
    }

    public override void OnSkillUnloaded(SkillData skillData, Creature creature) {
        base.OnSkillUnloaded(skillData, creature);
        enabled = false;
        OnSkillDisableEvent?.Invoke(skillData, creature);
    }
}
