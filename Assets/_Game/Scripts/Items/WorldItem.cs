using UnityEngine;

/// <summary>
/// GDD 12항 월드 아이템. 삭제하지 않고 실제 월드 오브젝트로 존재하며 Box Collider 를 가진다.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
// ponytail: 아이템마다 Rigidbody 가 붙는다. 정지하면 물리 엔진이 자동으로 sleep 시키므로
// V0.1 규모에서는 문제없다. 월드에 수백 개가 쌓이면 정지한 아이템의 Rigidbody 를 떼는 최적화가 필요.
[RequireComponent(typeof(Rigidbody))]
public class WorldItem : MonoBehaviour
{
    [SerializeField] ItemDefinition definition;
    [SerializeField, Min(1)] int quantity = 1;
    [Tooltip("GDD 11항 내구도. 바닥에 떨어진 무기도 마모 상태를 유지한다")]
    [SerializeField] int durability = -1;
    [Tooltip("GDD 4항 장전된 탄. -1 이면 정의의 장탄수")]
    [SerializeField] int loadedAmmo = -1;

    public ItemDefinition Definition => definition;
    public int Quantity => quantity;

    /// <summary>-1 이면 "최대치" 를 뜻한다 (씬에 손으로 배치한 아이템).</summary>
    public int Durability => durability < 0 && definition ? definition.maxDurability : durability;

    /// <summary>장전된 탄. 내구도와 같은 이유로 개체마다 달라야 한다.</summary>
    public int LoadedAmmo => loadedAmmo < 0 && definition ? definition.magazineSize : loadedAmmo;

    public WorldItem SetAmmo(int value)
    {
        loadedAmmo = value;
        return this;
    }

    public WorldItem SetDurability(int value)
    {
        durability = value;
        return this;
    }

    public string Label
    {
        get
        {
            if (!definition) return "???";
            return quantity > 1 ? definition.displayName + " x" + quantity : definition.displayName;
        }
    }

    void Start() => ApplyPlaceholderLook(); // 씬에 미리 배치된 아이템도 색을 입힌다

    public void Setup(ItemDefinition newDefinition, int newQuantity)
    {
        definition = newDefinition;
        quantity = Mathf.Max(1, newQuantity);
        ApplyPlaceholderLook();
    }

    public void SetQuantity(int newQuantity) => quantity = Mathf.Max(1, newQuantity);

    void ApplyPlaceholderLook()
    {
        if (!definition) return;
        gameObject.name = "WorldItem_" + definition.id;

        // 플레이어/몹과 충돌하지 않도록 WorldItem 레이어로 보낸다 (충돌 매트릭스에서 Character 와 끊어둠).
        int itemLayer = LayerMask.NameToLayer("WorldItem");
        if (itemLayer >= 0) gameObject.layer = itemLayer;

        // material(인스턴스) 이 아니라 sharedMaterial 을 쓴다.
        // material 은 에디트 모드에서 머티리얼을 씬에 누출시키고, 아이템마다 드로우콜을 늘린다.
        if (!definition.placeholderMaterial) return;
        var meshRenderer = GetComponent<Renderer>();
        if (meshRenderer) meshRenderer.sharedMaterial = definition.placeholderMaterial;
    }

    /// <summary>GDD 12항 투척/드롭용. 월드 모델이 없으므로 프리미티브 큐브로 대체.</summary>
    public static WorldItem Spawn(ItemDefinition definition, int quantity, Vector3 position, Vector3 throwVelocity)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.transform.position = position;
        go.transform.localScale = Vector3.one * 0.4f;

        var item = go.AddComponent<WorldItem>();
        item.Setup(definition, quantity);

        var body = go.GetComponent<Rigidbody>(); // RequireComponent 가 붙여준다
        body.linearVelocity = throwVelocity;

        // 회전을 크게 주면 착지 후에도 한참 굴러가서 던진 지점과 멀어진다. 살짝만 돌리고 빠르게 감쇠시킨다.
        body.angularVelocity = Random.insideUnitSphere * 2.5f;
        body.angularDamping = 3f;
        return item;
    }
}
