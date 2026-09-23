using UnityEngine;

/// <summary>
/// 집 안 전등의 켜짐/꺼짐. 기획 6항 태양광 + ESS 덕에 2년째 전기가 들어온다.
///
/// 켜져 있으면 지금 있는 방 전체가 보이고(VisionField.RevealLitRoom), 꺼져 있으면
/// 부채꼴 시야만 보인다 (사용자 요구). 실외에서는 아무 영향이 없다.
///
/// ponytail: 방마다 따로 켜는 스위치는 아직 필요 없다 — 집 단위 하나로 둔다.
/// 방별 스위치가 필요해지면 Room 에 상태를 옮긴다.
/// </summary>
public static class HouseLights
{
    public static bool On { get; private set; } = true;

    public static void Toggle() => On = !On;
    public static void Set(bool on) => On = on;

    // 정적 필드는 씬을 다시 열어도 남는다. 재생을 시작할 때마다 켜진 상태로 되돌린다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay() => On = true;
}
