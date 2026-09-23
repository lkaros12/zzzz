using UnityEngine;

/// <summary>
/// 캡슐 플레이스홀더에 앞/뒤를 알 수 있는 얼굴을 붙인다. 좀비와 플레이어가 같이 쓴다.
///
/// GDD 17항 시야는 정면 원뿔이고 GDD 19항 암살은 후면 120° 이므로, 어느 쪽이 앞인지
/// 보이지 않으면 플레이어가 판단할 근거가 없다. 플레이어도 마우스 방향으로 도는 만큼
/// 자기 몸이 어디를 보는지 보여야 한다.
///
/// 머티리얼을 새로 만들지 않고 몸통의 공유 머티리얼에 MaterialPropertyBlock 으로 색만 덮는다 —
/// 에디트 모드에서 머티리얼을 인스턴스화하면 씬에 누출된다 (HitFlash 와 같은 이유).
///
/// ponytail: 프리미티브 조립이다. 실제 캐릭터 메쉬가 들어오면 이 파일은 지운다.
/// </summary>
public static class CapsuleFace
{
    static readonly Color EyeColor = new Color(0.06f, 0.06f, 0.08f);
    static readonly Color MouthColor = new Color(0.40f, 0.08f, 0.08f);
    static readonly Color BackMarkColor = new Color(0.85f, 0.80f, 0.35f);

    /// <summary>이미 얼굴이 있으면 아무것도 하지 않는다.</summary>
    public static void Build(Transform body, Material bodyMaterial)
    {
        if (!body || body.Find("Face")) return;

        var face = new GameObject("Face").transform;
        face.SetParent(body, false);
        face.localPosition = Vector3.zero;

        // 아이소메트릭 기본 줌에서도 읽히도록 크게 잡는다. 작게 했더니 화면에서 안 보였다.
        AddPart(face, bodyMaterial, PrimitiveType.Sphere, "Eye_L",
                new Vector3(-0.21f, 0.52f, 0.38f), new Vector3(0.27f, 0.31f, 0.22f), EyeColor);
        AddPart(face, bodyMaterial, PrimitiveType.Sphere, "Eye_R",
                new Vector3(0.21f, 0.52f, 0.38f), new Vector3(0.27f, 0.31f, 0.22f), EyeColor);
        AddPart(face, bodyMaterial, PrimitiveType.Cube, "Mouth",
                new Vector3(0f, 0.22f, 0.44f), new Vector3(0.42f, 0.10f, 0.14f), MouthColor);

        // 뒤통수 표시. 등 쪽에도 단서를 둬서 어느 쪽이 뒤인지 바로 알게 한다 (암살 방향).
        AddPart(face, bodyMaterial, PrimitiveType.Cube, "BackMark",
                new Vector3(0f, 0.30f, -0.45f), new Vector3(0.12f, 0.70f, 0.12f), BackMarkColor);
    }

    static void AddPart(Transform parent, Material bodyMaterial, PrimitiveType shape, string name,
                        Vector3 localPosition, Vector3 localScale, Color color)
    {
        var go = GameObject.CreatePrimitive(shape);
        go.name = name;

        // 콜라이더를 반드시 없앤다. 남겨두면 히트스캔이 이걸 엄폐물로 보고 총알을 막는다.
        // Destroy 는 프레임 끝까지 미뤄져 그 사이 물리 판정에 그대로 잡히므로 (실제로 뒤통수
        // 표시가 총알을 막았다) 먼저 끄고 나서 파괴한다.
        var collider = go.GetComponent<Collider>();
        if (collider)
        {
            collider.enabled = false;
            if (Application.isPlaying) Object.Destroy(collider);
            else Object.DestroyImmediate(collider);
        }

        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = localScale;

        var renderer = go.GetComponent<Renderer>();
        if (!renderer) return;

        if (bodyMaterial) renderer.sharedMaterial = bodyMaterial;

        // 색은 FaceTint 가 들고 있다가 런타임에 다시 입힌다 —
        // MaterialPropertyBlock 만 걸어두면 씬 저장/로드에서 사라진다.
        go.AddComponent<FaceTint>().SetTint(color);
    }
}
