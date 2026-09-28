using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[RequireComponent(typeof(BoxCollider))]
public class SceneExit : MonoBehaviour
{
    [SerializeField] string targetSceneName;
    [SerializeField] string arrivalPointName;

    static SaveData pendingState;
    static ItemCatalog pendingCatalog;
    bool loading;

    public string TargetSceneName => targetSceneName;
    public string ArrivalPointName => arrivalPointName;

    public void Setup(string sceneName, string pointName)
    {
        targetSceneName = sceneName;
        arrivalPointName = pointName;
    }

    void OnTriggerEnter(Collider other)
    {
        if (loading || !other.GetComponent<PlayerStats>()) return;

        var save = FindFirstObjectByType<SaveSystem>();
        if (!save || !save.Catalog || !Application.CanStreamedLevelBeLoaded(targetSceneName))
        {
            Debug.LogError("[SceneExit] 이동할 씬 또는 플레이어 저장 상태를 준비할 수 없습니다.");
            return;
        }

        pendingState = save.CapturePlayerState();
        pendingCatalog = save.Catalog;
        if (pendingState == null) return;

        loading = true;
        GetComponent<Collider>().enabled = false;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
        StartCoroutine(LoadTarget());
    }

    IEnumerator LoadTarget()
    {
        var promptObject = GameObject.Find("PickupPrompt");
        var prompt = promptObject ? promptObject.GetComponent<Text>() : null;
        if (prompt)
        {
            var updater = prompt.GetComponentInParent<PickupPromptUI>();
            if (updater) updater.enabled = false;
            prompt.enabled = true;
            prompt.text = "이동 중...";
        }

        yield return null;
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.LoadScene(targetSceneName);
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        StartCoroutine(RestoreAfterStart());
    }

    IEnumerator RestoreAfterStart()
    {
        // 새 씬의 Start 초기화가 이전 상태를 덮지 않도록 한 프레임 뒤에 복원한다.
        yield return null;

        var player = FindFirstObjectByType<PlayerStats>();
        var clock = FindFirstObjectByType<GameClock>();
        var arrival = GameObject.Find(arrivalPointName);
        if (!arrival || !SaveSystem.RestorePlayerState(
                pendingState, pendingCatalog, player, clock, arrival.transform))
            Debug.LogError("[SceneExit] 도착 지점 또는 플레이어 상태를 복원할 수 없습니다.");

        pendingState = null;
        pendingCatalog = null;
        Destroy(gameObject);
    }

    void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;
}
