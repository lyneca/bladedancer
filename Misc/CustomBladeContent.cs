using Newtonsoft.Json;
using ThunderRoad;

namespace Bladedancer;

public class CustomBladeContent : ItemOrTableContent<CustomBladeData, CustomBladeContent> {
    public override CatalogData catalogData => Catalog.GetData<ItemData>(referenceID);

    [JsonConstructor]
    public CustomBladeContent() { }
    public CustomBladeContent(string id) {
        referenceID = id;
    }
    public CustomBladeContent(CustomBladeData data) {
        referenceID = data.itemId;
    }

    public override CustomBladeContent CloneGeneric() {
        return new CustomBladeContent(referenceID);
    }
}

public class CustomBladeData : CustomData, IContainerLoadable<CustomBladeData> {
    public string itemId;
    public void OnLoadedFromContainer(Container container) { }
    public ContainerContent InstanceContent() => new CustomBladeContent(this);
}
