using UnityEngine;

/// <summary>모델 Animator의 IK 콜백을 플레이어 루트로 전달한다.</summary>
public class PlayerLookAtRelay : MonoBehaviour
{
    [SerializeField] PlayerAnimator target;

    public PlayerAnimator Target => target;

    public void Bind(PlayerAnimator playerAnimator) => target = playerAnimator;

    void OnAnimatorIK(int layerIndex)
    {
        if (target) target.ApplyLookAtIK();
    }
}
