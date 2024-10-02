using ThunderRoad;

namespace Bladedancer.Skills; 

public class SkillPsyblades : SkillData {
    public string statusId = "Disoriented";
    public StatusData statusData;
    [SkillCategory("Psyblades", Category.Mind, 2)]
    [ModOptionFloatValues(0, 60, 1f)]
    [ModOptionSlider, ModOption("Disoriented Duration", "How long enemies are disoriented by Psyblades")]
    public static float duration = 15;

    public override void OnCatalogRefresh() {
        base.OnCatalogRefresh();
        statusData = Catalog.GetData<StatusData>(statusId);
    }

    public override void OnSkillLoaded(SkillData skillData, Creature creature) {
        base.OnSkillLoaded(skillData, creature);
        if (!Quiver.TryGet(creature, out var quiver)) return;
        quiver.OnBladeHit -= OnBladeHit;
        quiver.OnBladeHit += OnBladeHit;
    }

    public override void OnSkillUnloaded(SkillData skillData, Creature creature) {
        base.OnSkillUnloaded(skillData, creature);
        if (!Quiver.TryGet(creature, out var quiver)) return;
        quiver.OnBladeHit -= OnBladeHit;
    }

    private void OnBladeHit(Blade blade, ThunderEntity entity, CollisionInstance hit) {
        entity.Inflict(statusData, this, duration);
    }
}
