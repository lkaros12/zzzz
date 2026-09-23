using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 자기 밑의 모든 레거시 Text 에 한글 OS 폰트를 입힌다.
/// 내장 폰트에는 한글 글리프가 없어서 이게 없으면 전부 빈 사각형으로 나온다.
/// 런타임에 만들어지는 Text(인벤토리 아이템 라벨)는 만드는 쪽에서 직접 KoreanFont 를 쓴다.
/// </summary>
public class KoreanFontApplier : MonoBehaviour
{
    void Awake()
    {
        var font = KoreanFont.Get();
        if (!font) return;

        var texts = GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++) texts[i].font = font;
    }
}
