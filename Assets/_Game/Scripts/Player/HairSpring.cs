using System.Collections.Generic;
using UnityEngine;

/// <summary>애니메이션 뒤에 7가닥 머리카락 본을 가볍게 흔든다.</summary>
[DefaultExecutionOrder(-200)]
public class HairSpring : MonoBehaviour
{
    [SerializeField] float stiffness = 80f;
    [SerializeField, Range(0f, 1f)] float damping = 0.15f;
    [SerializeField] float gravity = 0.5f;
    [SerializeField, Range(0f, 15f)] float maxAngle = 12f;
    [SerializeField] Transform[] roots;

    Transform[] bones;
    Quaternion[] referenceRotations;
    Vector3[] referenceEnds;
    Quaternion[] rootReferenceRotations;
    Vector3[] positions;
    Vector3[] previousPositions;
    Vector3 lastRootPosition;
    float runSpeed;

    public int StrandCount => roots == null ? 0 : roots.Length;
    public int BoneCount
    {
        get
        {
            int count = 0;
            if (roots == null) return count;
            for (int i = 0; i < roots.Length; i++)
                for (Transform bone = roots[i]; bone; bone = bone.childCount == 1 ? bone.GetChild(0) : null)
                    count++;
            return count;
        }
    }
    public float MaxAngle => maxAngle;

    void Awake() => Initialize();

    void LateUpdate()
    {
        if (Time.deltaTime > 0f) Simulate(Time.deltaTime);
    }

    /// <summary>에디트 모드 자기검사에서도 같은 한 프레임 계산을 호출한다.</summary>
    public void Simulate(float deltaTime)
    {
        if (deltaTime <= 0f || !Initialize()) return;
        deltaTime = Mathf.Min(deltaTime, 1f / 30f);

        for (int i = 0; i < bones.Length; i++) bones[i].localRotation = referenceRotations[i];
        for (int i = 0; i < bones.Length; i++)
            rootReferenceRotations[i] = bones[i].rotation;

        Vector3 rootDelta = transform.position - lastRootPosition;
        lastRootPosition = transform.position;
        if (PlayerAnimator.IsTeleport(rootDelta, deltaTime, runSpeed))
        {
            // 순간이동 거리를 관성으로 해석하면 머리카락이 최대 각도까지 튀므로 현재 자세에서 다시 시작한다.
            ResetPoints();
            return;
        }

        float step = deltaTime * deltaTime;
        float angle = maxAngle * Mathf.Deg2Rad;
        float velocityRetention = Mathf.Pow(1f - damping, deltaTime * 60f);
        for (int i = 0; i < bones.Length; i++)
        {
            var bone = bones[i];
            Vector3 targetEnd = bone.TransformPoint(referenceEnds[i]);
            Vector3 targetDirection = targetEnd - bone.position;
            Vector3 next = positions[i]
                         + (positions[i] - previousPositions[i]) * velocityRetention
                         + (targetEnd - positions[i]) * stiffness * step
                         + Vector3.down * gravity * step;
            Vector3 direction = next - bone.position;
            if (targetDirection.sqrMagnitude > 0f && direction.sqrMagnitude > 0f)
            {
                direction = Vector3.RotateTowards(targetDirection, direction, angle, 0f).normalized
                            * targetDirection.magnitude;
                bone.rotation = Quaternion.FromToRotation(targetDirection, direction) * bone.rotation;
                bone.rotation = Quaternion.RotateTowards(rootReferenceRotations[i], bone.rotation, maxAngle);
                bone.localRotation = Quaternion.RotateTowards(referenceRotations[i], bone.localRotation, maxAngle);
                direction = bone.TransformPoint(referenceEnds[i]) - bone.position;
            }

            previousPositions[i] = positions[i];
            positions[i] = bone.position + direction;
        }
    }

    bool Initialize()
    {
        if (bones != null) return true;
        if (roots == null || roots.Length == 0) return false;

        var motor = GetComponentInParent<PlayerMotor>();
        if (!motor) return false;
        runSpeed = motor.RunSpeed;

        var found = new List<Transform>();
        for (int i = 0; i < roots.Length; i++)
        {
            if (!roots[i]) continue;
            for (Transform bone = roots[i]; bone; bone = bone.childCount == 1 ? bone.GetChild(0) : null)
                found.Add(bone);
        }

        bones = found.ToArray();
        referenceRotations = new Quaternion[bones.Length];
        referenceEnds = new Vector3[bones.Length];
        rootReferenceRotations = new Quaternion[bones.Length];
        positions = new Vector3[bones.Length];
        previousPositions = new Vector3[bones.Length];

        for (int i = 0; i < bones.Length; i++)
        {
            var bone = bones[i];
            referenceRotations[i] = bone.localRotation;
            if (bone.childCount == 1) referenceEnds[i] = bone.GetChild(0).localPosition;
            else
            {
                // 끝 본에는 자식 끝점이 없어서 직전 마디의 방향과 길이를 한 번 더 연장한다.
                Vector3 segment = bone.position - bone.parent.position;
                referenceEnds[i] = bone.InverseTransformPoint(bone.position + segment);
            }
        }

        lastRootPosition = transform.position;
        ResetPoints();
        return true;
    }

    public void ResetSimulation()
    {
        if (!Initialize()) return;
        for (int i = 0; i < bones.Length; i++) bones[i].localRotation = referenceRotations[i];
        lastRootPosition = transform.position;
        ResetPoints();
    }

    void ResetPoints()
    {
        for (int i = 0; i < bones.Length; i++)
            positions[i] = previousPositions[i] = bones[i].TransformPoint(referenceEnds[i]);
    }
}
