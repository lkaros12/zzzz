using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// GDD 12항 "가까이 접근하면 아이템 이름 표시" + 아이템 사용 결과 메시지.
/// GDD 23항대로 읽기만 하고 인벤토리 로직은 건드리지 않는다.
/// </summary>
public class PickupPromptUI : MonoBehaviour
{
    [SerializeField] PlayerInteractor interactor;
    [SerializeField] PlayerConsumer consumer;
    [SerializeField] PlayerAssassinate assassinate;
    [SerializeField] PlayerShield shield;
    [SerializeField] VisionField vision;
    [SerializeField] Text promptText;
    [Tooltip("아이템 사용 결과 메시지를 띄워두는 시간")]
    [SerializeField] float messageDuration = 2f;

    void Update() => UpdatePrompt();

    void UpdatePrompt()
    {
        if (!promptText) return;

        // 사용 결과가 방금 나왔으면 그것을 먼저 보여준다.
        // 이게 없으면 E 로 붕대를 써도(치료할 부상이 없을 때) 화면에 아무 반응이 없어 고장처럼 보인다.
        if (consumer && Time.time - consumer.LastMessageTime < messageDuration)
        {
            promptText.enabled = true;
            promptText.text = consumer.LastMessage;
            return;
        }

        // GDD 8항 투척 결과를 잠깐 띄운다.
        if (interactor && Time.time - interactor.ThrowMessageTime < messageDuration)
        {
            promptText.enabled = true;
            promptText.text = interactor.ThrowMessage;
            return;
        }

        // 기획 4항 창문 안내. 컨테이너보다 먼저 본다 — NearestWindow 자체가
        // "창문이 가장 가깝다" 는 뜻이라 여기서 다시 비교하지 않는다.
        if (interactor && interactor.NearestWindow)
        {
            promptText.enabled = true;
            promptText.text = interactor.NearestWindow.PromptText;
            return;
        }

        // 컨테이너는 E 로 열지 않는다 (사용자 요구: E 는 문과 창문 전용).
        // 사거리에 들어오면 Tab 화면에 내용이 바로 나오므로 안내 문구도 두지 않는다.

        // GDD 19항: 암살 중 / 암살 가능은 줍기 안내보다 먼저 보여준다 — E 가 두 가지 일을 한다.
        if (assassinate && assassinate.IsAssassinating)
        {
            promptText.enabled = true;
            promptText.text = "암살 중... (무적)";
            return;
        }

        if (assassinate && assassinate.FindTarget() != null)
        {
            promptText.enabled = true;
            promptText.text = "[E] 암살";
            return;
        }

        // GDD 21항 방패 상태. 방어/후딜은 눈에 보이는 단서가 없어서 글자로 알려준다.
        if (shield && shield.HasShield && (shield.IsBlocking || shield.IsRecovering))
        {
            promptText.enabled = true;
            promptText.text = shield.IsBlocking ? "방어 중 (우클릭)" : "방패 재정비...";
            return;
        }

        var item = interactor ? interactor.NearestItem : null;
        bool flashlightOff = vision && !vision.FlashlightOn;
        promptText.enabled = item != null || flashlightOff;
        if (item != null) promptText.text = "[E] " + item.Label;
        else if (flashlightOff) promptText.text = "손전등 꺼짐";
    }

    public void Bind(PlayerInteractor playerInteractor, PlayerConsumer playerConsumer, Text prompt)
    {
        interactor = playerInteractor;
        consumer = playerConsumer;
        promptText = prompt;

        if (playerInteractor)
        {
            assassinate = playerInteractor.GetComponent<PlayerAssassinate>();
            shield = playerInteractor.GetComponent<PlayerShield>();
            vision = playerInteractor.GetComponent<VisionField>();
        }
    }
}
