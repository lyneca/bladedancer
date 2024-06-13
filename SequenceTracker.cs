using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SequenceTracker {
    using NamedConditionSet = Tuple<string, Func<bool>[]>;
    using NamedCondition = Tuple<string, Func<bool>>;
    public class Step {
        protected Step skipTo;
        protected Step parent;
        private Condition condition;
        List<Step> children;
        private Action action;
        private Action onStateChange;
        private string actionName;
        private string name;
        private float lastChangedToTime;
        private float delay;
        private Func<bool> predicate;

        public static Step Start(Action onStateChange = null) {
            var step = new Step(new Condition(() => true), null, onStateChange) { name = "Start" };
            return step;
        }
        protected class Condition {
            protected Func<bool> start;
            protected Func<bool> end;
            protected float duration;
            protected bool hasStarted;
            protected float startTime;
            public bool runOnChange;
            public DurationMode mode;

            public Condition(Func<bool> start, Func<bool> end = null, float duration = 0, DurationMode mode = DurationMode.After, bool runOnChange = true) {
                this.start = start;
                this.end = end;
                this.duration = duration;
                this.mode = mode;
                this.runOnChange = runOnChange;
            }

            public bool Evaluate() {
                if (!hasStarted && start()) {
                    hasStarted = true;
                    startTime = Time.time;
                }

                // Has not hit leading edge
                if (!hasStarted) return false;

                // No duration, return end condition
                if (duration == 0) return end?.Invoke() ?? true;

                float sinceStart = Time.time - startTime;

                bool hasEnded = end?.Invoke() ?? true;

                // Time limit exceeded, reset
                if (mode == DurationMode.Before && sinceStart > duration && hasEnded) {
                    hasStarted = false;
                    return false;
                }
                
                // Check whether time within duration
                bool durationCheck = mode == DurationMode.After
                    ? sinceStart >= duration
                    : sinceStart <= duration;

                return durationCheck && hasEnded;
            }

            public void Reset() {
                hasStarted = false;
            }
        }
        protected Step(Condition condition, Step parent = null, Action onStateChange = null) {
            this.parent = parent;
            this.condition = condition;
            this.onStateChange = onStateChange ?? parent?.onStateChange;
            children = new List<Step>();
        }

        public Step While(Func<bool> predicate, string name = "") {
            var step = Then(() => true, "While " + name);
            step.predicate = predicate;
            return step;
        }

        public Step Then(
            Func<bool> startCondition,
            string name = "",
            float duration = 0,
            Func<bool> endCondition = null,
            DurationMode mode = DurationMode.After,
            bool runOnChange = true,
            bool toggle = false) {
            var trigger = new Condition(startCondition,
                toggle ? () => !startCondition() : endCondition,
                duration,
                mode,
                runOnChange);

            var step = new Step(trigger, this) {
                name = name
            };
            children.Add(step);
            return step;
        }

        public Step Then(params NamedCondition[] conditions) {
            if (conditions.Length == 0) return this;
            var next = Then(conditions[0].Item2, conditions[0].Item1);
            return conditions.Length > 1 ? next.Then(conditions.Skip(1).ToArray()) : next;
        }

        public Step Then(params NamedConditionSet[] conditions) {
            if (conditions.Length == 0) return this;
            var next = Then(conditions[0].Item1, conditions[0].Item2);
            return conditions.Length > 1 ? next.Then(conditions.Skip(1).ToArray()) : next;
        }
        public Step Then(string name = "", params Func<bool>[] conditions) {
            var list = new Queue<Func<bool>>(conditions);
            var next = Then(list.Dequeue(), name);

            while (list.Count > 0) {
                next = next.Then(list.Dequeue());
            }

            return next;
        }

        public Step Do(Action action, string actionName = "") {
            this.action = action;
            this.actionName = actionName;
            return this;
        }

        public Step After(float delay) {
            var step = Then(() => true, $"Wait {delay:0.##}s", runOnChange: true);
            step.delay = delay;

            return step;
        }

        public void Reset() {
            skipTo = null;
            condition.Reset();
            for (var index = 0; index < children.Count; index++) {
                var child = children[index];
                child.Reset();
            }
        }

        protected bool Check() {
            return condition.Evaluate();
        }

        protected void DoUpdate() {
            if (skipTo == null
                && predicate?.Invoke() != false
                && (parent == null || Time.time - parent.lastChangedToTime > delay)) {
                for (var index = 0; index < children.Count; index++) {
                    var child = children[index];
                    if (child.Check()) {
                        child.action?.Invoke();
                        if (child.condition.runOnChange)
                            onStateChange?.Invoke();
                        skipTo = child;
                        lastChangedToTime = Time.time;
                        break;
                    }
                }
            }

            if (skipTo != null) {
                skipTo.DoUpdate();
            }
        }

        public void Update() {
            if (parent == null) DoUpdate();
            else parent.Update();
        }

        public string GetCurrentPath() {
            if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(actionName)) {
                return skipTo != null ? skipTo.GetCurrentPath() : "";
            }
            if (string.IsNullOrEmpty(name)) {
                return $"[{actionName}]" + (skipTo != null ? " > " + skipTo.GetCurrentPath() : "");
            }
            if (string.IsNullOrEmpty(actionName)) {
                return name + (skipTo != null ? " > " + skipTo.GetCurrentPath() : "");
            }

            return "";
        }

        public bool AtEnd() {
            return skipTo?.AtEnd() ?? children.Count == 0;
        }

        public string DisplayTree(int indent = 0) {
            var output = "";
            bool increaseIndent = true;
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(actionName)) {
                output += new String(' ', indent * 2) + $"- {name}\n" + new String(' ', (indent + 1) * 2) + $"- Action: {actionName}\n";
            } else if (!string.IsNullOrEmpty(name)) {
                output += new String(' ', indent * 2) + $"- {name}\n";
            } else if (!string.IsNullOrEmpty(actionName)) {
                output += new String(' ', indent * 2) + $"- Action: {actionName}\n";
                increaseIndent = false;
            } else {
                increaseIndent = false;
            }

            foreach (var child in children) {
                string childTree = child.DisplayTree(indent + (increaseIndent ? 1 : 0));
                if (!string.IsNullOrWhiteSpace(childTree))
                    output += childTree;
            }

            return output;
        }
    }

    public enum DurationMode {
        Before,
        After
    }
}
