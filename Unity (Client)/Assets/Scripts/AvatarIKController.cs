using UnityEngine;

[RequireComponent(typeof(Animator))]
public class AvatarIKController : MonoBehaviour
{
    public PoseReceiver poseReceiver; // Drag PoseReceiver GameObject here
    public float scale = 2.0f;         // Match avatar proportions
    public float ikSmooth = 10f;       // Smoothing for IK target movement

    private Animator animator;

    // Smoothed IK targets
    private Vector3 leftHandTarget, rightHandTarget;
    private Vector3 leftFootTarget, rightFootTarget;
    private Vector3 lookTarget;

    void Start()
    {
        animator = GetComponent<Animator>();

        // Init targets so there’s no snapping on first frame
        leftHandTarget = rightHandTarget = leftFootTarget = rightFootTarget = transform.position;
        lookTarget = transform.position + transform.forward * 2f;
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (poseReceiver == null || poseReceiver.latestPose == null) return;

        // Smoothly update IK targets
        leftHandTarget = SmoothTarget(leftHandTarget, poseReceiver.GetWorldPosition(15, scale));
        rightHandTarget = SmoothTarget(rightHandTarget, poseReceiver.GetWorldPosition(16, scale));
        leftFootTarget = SmoothTarget(leftFootTarget, poseReceiver.GetWorldPosition(27, scale));
        rightFootTarget = SmoothTarget(rightFootTarget, poseReceiver.GetWorldPosition(28, scale));
        lookTarget = SmoothTarget(lookTarget, poseReceiver.GetWorldPosition(0, scale));

        // --- Hands ---
        animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 1f);
        animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 1f);
        animator.SetIKPosition(AvatarIKGoal.LeftHand, leftHandTarget);

        animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 1f);
        animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 1f);
        animator.SetIKPosition(AvatarIKGoal.RightHand, rightHandTarget);

        // --- Feet ---
        animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 1f);
        animator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, 1f);
        animator.SetIKPosition(AvatarIKGoal.LeftFoot, leftFootTarget);

        animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, 1f);
        animator.SetIKRotationWeight(AvatarIKGoal.RightFoot, 1f);
        animator.SetIKPosition(AvatarIKGoal.RightFoot, rightFootTarget);

        // --- Head Look ---
        animator.SetLookAtWeight(1f);
        animator.SetLookAtPosition(lookTarget);
    }

    private Vector3 SmoothTarget(Vector3 current, Vector3 target)
    {
        if (target == Vector3.zero) return current; // Skip if no landmark detected
        return Vector3.Lerp(current, target, 1f - Mathf.Exp(-ikSmooth * Time.deltaTime));
    }
}
