using ThunderRoad;
using UnityEngine;

namespace Bladedancer;

public class ItemModuleCustomCrystal : ItemModule {
    public string meshAddress;
    public string materialAddress;
    public Vector3 position;
    public Vector3 rotation;

    public override void OnItemDataRefresh(ItemData data) {
        base.OnItemDataRefresh(data);
        // let's just sneak in front... nyeheheh...
        itemData.modules.Remove(this);
        itemData.modules.Insert(0, this);
    }

    public override void OnItemLoaded(Item item) {
        base.OnItemLoaded(item);

        if (!string.IsNullOrEmpty(materialAddress))
            Catalog.LoadAssetAsync<Material>(materialAddress, material => {
                    var renderers = item.GetComponentsInChildren<MeshRenderer>(true);
                    for (var i = 0; i < renderers.Length; i++) {
                        renderers[i].material = material;
                        if (renderers[i].TryGetComponent(out MaterialInstance instance))
                            instance.AcquireMaterials();
                    }
                }, $"{item.data.id}.ItemModuleCustomCrystal");

        if (!string.IsNullOrEmpty(meshAddress))
            Catalog.LoadAssetAsync<Mesh>(meshAddress, mesh => {
                    var filters = item.GetComponentsInChildren<MeshFilter>(true);
                    for (var i = 0; i < filters.Length; i++) {
                        filters[i].mesh = mesh;
                        filters[i].transform.localRotation = Quaternion.Euler(rotation);
                        filters[i].transform.localPosition += position;
                    }
                }, $"{item.data.id}.ItemModuleCustomCrystal");
    }
}