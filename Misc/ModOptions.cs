using System.Collections.Generic;
using System.Reflection;
using ThunderRoad;

namespace Bladedancer;

public static class ModOptions {
    public static Dictionary<string, ModOption> options = new Dictionary<string, ModOption>();

    public static void Setup() {
        if (!ModManager.TryGetModData(Assembly.GetExecutingAssembly(), out var data)) return;

        for (var i = 0; i < data.modOptions.Count; i++) {
            options[data.modOptions[i].name] = data.modOptions[i];
        }
    }

    public static bool TryGetOption(string name, out ModOption option) {
        return options.TryGetValue(name, out option);
    }
}
