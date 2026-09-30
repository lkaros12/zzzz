using UnityEngine;

/// <summary>
/// 저장/로드에서 아이템을 id 문자열로 되찾기 위한 목록.
/// ItemDefinition 은 Resources 밖(Assets/_Game/Data/Items) 에 있어서 경로로 런타임 로드가 안 된다.
/// 빌더가 이 에셋을 만들어 씬의 SaveSystem 에 물려준다.
/// </summary>
[CreateAssetMenu(fileName = "ItemCatalog", menuName = "V0.1/Item Catalog")]
public class ItemCatalog : ScriptableObject
{
    [SerializeField] ItemDefinition[] items = new ItemDefinition[0];

    public int Count => items != null ? items.Length : 0;

    public ItemDefinition Get(int index) =>
        items != null && index >= 0 && index < items.Length ? items[index] : null;

    public ItemDefinition Find(string id)
    {
        if (items == null || string.IsNullOrEmpty(id)) return null;

        for (int i = 0; i < items.Length; i++)
            if (items[i] && items[i].id == id) return items[i];

        return null;
    }

    public void SetItems(ItemDefinition[] all) => items = all;
}
