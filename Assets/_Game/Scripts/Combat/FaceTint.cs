using UnityEngine;

/// <summary>
/// 얼굴 조각의 색을 들고 있다가 런타임에 다시 입힌다.
///
/// MaterialPropertyBlock 은 씬에 직렬화되지 않는다 — 에디트 모드에서 색을 입혀도 씬을 다시
/// 열면 사라져서 얼굴이 몸통과 같은 색이 된다 (실제로 겪었다). 색은 직렬화되는 필드에 두고
/// Awake 에서 블록을 다시 만든다. 머티리얼을 인스턴스화하지 않으므로 누출도 없다.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class FaceTint : MonoBehaviour
{
    [SerializeField] Color tint = Color.black;

    public void SetTint(Color color)
    {
        tint = color;
        Apply();
    }

    void Awake() => Apply();

    void Apply()
    {
        var renderer = GetComponent<Renderer>();
        if (!renderer) return;

        var block = new MaterialPropertyBlock();
        block.SetColor(Shader.PropertyToID("_BaseColor"), tint);
        block.SetColor(Shader.PropertyToID("_Color"), tint);
        renderer.SetPropertyBlock(block);
    }
}
