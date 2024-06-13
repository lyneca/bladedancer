using ThunderRoad;
using ThunderRoad.Skill;

namespace Bladedancer.Skills; 

public class SkillDoubleTrouble : SkillData {
    public delegate void MergeEvent(SpellMergeData spell);
    public static event MergeEvent OnMergeStartEvent;
    public static event MergeEvent OnMergeEndEvent;

    public static void InvokeOnMergeStart(SpellMergeData spell) {
        OnMergeStartEvent?.Invoke(spell);
    }

    public static void InvokeOnMergeEnd(SpellMergeData spell) {
        OnMergeEndEvent?.Invoke(spell);
    }

    public override void OnSkillLoaded(SkillData skillData, Creature creature) {
        base.OnSkillLoaded(skillData, creature);
        OnMergeStartEvent -= OnMergeStart;
        OnMergeStartEvent += OnMergeStart;
        OnMergeEndEvent -= OnMergeEnd;
        OnMergeEndEvent += OnMergeEnd;
    }

    private void OnMergeStart(SpellMergeData spell) {
        Quiver.GetMaxCountHandler(spell.mana.creature).Add(this, 2);
    }

    private void OnMergeEnd(SpellMergeData spell) {
        Quiver.GetMaxCountHandler(spell.mana.creature).Remove(this);
    }

    public override void OnLateSkillsLoaded(SkillData skillData, Creature creature) {
        base.OnLateSkillsLoaded(skillData, creature);
        if (!creature.TryGetSkill("SecondWind", out SkillSecondWind secondWind)) return;
        secondWind.OnSecondWindEvent -= OnSecondWind;
        secondWind.OnSecondWindEvent += OnSecondWind;
    }

    public override void OnSkillUnloaded(SkillData skillData, Creature creature) {
        base.OnSkillUnloaded(skillData, creature);
        OnMergeStartEvent -= OnMergeStart;
        OnMergeEndEvent -= OnMergeEnd;

        if (!creature.TryGetSkill("SecondWind", out SkillSecondWind secondWind)) return;
        Quiver.GetMaxCountHandler(creature).Remove(secondWind);
        secondWind.OnSecondWindEvent -= OnSecondWind;
    }

    private void OnSecondWind(SkillSecondWind skill, EventTime time) {
        if (time == EventTime.OnStart) {
            Quiver.Main.MaxCountHandler.Add(skill, 2);
        } else {
            Quiver.Main.MaxCountHandler.Remove(skill);
        }
    }
}
