using System.Collections.Generic;
using UnityEngine;

public class AvatarPoseController : MonoBehaviour
{
    [Header("Pose Receiver")]
    public PoseReceiver poseReceiver;

    [Header("Spine & Head")]
    public Transform hips;
    public Transform spine;
    public Transform neck;
    public Transform head;

    [Header("Left Arm Bones")]
    public Transform leftUpperArm;
    public Transform leftLowerArm;

    [Header("Right Arm Bones")]
    public Transform rightUpperArm;
    public Transform rightLowerArm;

    [Header("Left Leg Bones")]
    public Transform leftUpperLeg;
    public Transform leftLowerLeg;

    [Header("Right Leg Bones")]
    public Transform rightUpperLeg;
    public Transform rightLowerLeg;

    [Header("Settings")]
    public float scale = 2.0f;
    public float smoothFactor = 10f;

    [Header("Root follow")]
    public bool followHipCenter = true;
    public float rootSmooth = 8f;

    [Header("Root orientation")]
    public bool followRootRotation = true;
    public bool yawOnly = true;                 // turn only around Y
    public float rootRotationSmooth = 10f;
    public bool useNoseToDisambiguate = true;   // use nose to pick front vs back

    [Header("Axis calibration")]
    public Vector3 globalAxisOffset = Vector3.zero; // applied in local space after retarget
    [System.Serializable]
    public class BoneAxisOffset { public Transform bone; public Vector3 eulerOffset; }
    public BoneAxisOffset[] perBoneAxisOffsets;

    private Transform[] allBones;
    private Quaternion[] bindPoseRotations;
    private Dictionary<Transform, Quaternion> perBoneOffsetMap;

    // Bind-pose data for stable retarget
    private struct BoneBind
    {
        public Transform bone;
        public Quaternion bindLocalRot;     // bone.localRotation at start
        public Vector3 bindAimParent;       // in parent space, direction bone was pointing in bind pose
    }
    private Dictionary<Transform, BoneBind> bindMap = new Dictionary<Transform, BoneBind>();

    private Vector3 smoothedHipPos;
    private Vector3 hipsStartPos;

    [Header("Root Rotation Correction")]
    public Vector3 rootRotationOffsetEuler = Vector3.zero;

    void Start()
    {
        // Do NOT apply rootRotationOffsetEuler here; it's applied every frame with target rotation.

        hipsStartPos = hips != null ? hips.position : transform.position;
        allBones = new Transform[]
        {
            hips, spine, neck, head,
            leftUpperArm, leftLowerArm,
            rightUpperArm, rightLowerArm,
            leftUpperLeg, leftLowerLeg,
            rightUpperLeg, rightLowerLeg,
        };

        bindPoseRotations = new Quaternion[allBones.Length];
        for (int i = 0; i < allBones.Length; i++)
        {
            if (allBones[i] != null)
                bindPoseRotations[i] = allBones[i].localRotation;
        }

        // Per-bone local-space offsets
        perBoneOffsetMap = new Dictionary<Transform, Quaternion>();
        if (perBoneAxisOffsets != null)
        {
            foreach (var o in perBoneAxisOffsets)
                if (o != null && o.bone != null)
                    perBoneOffsetMap[o.bone] = Quaternion.Euler(o.eulerOffset);
        }

        // Cache bind-pose aim directions in parent space for driven bones
        CacheBind(leftUpperArm);
        CacheBind(leftLowerArm);
        CacheBind(rightUpperArm);
        CacheBind(rightLowerArm);
        CacheBind(leftUpperLeg);
        CacheBind(leftLowerLeg);
        CacheBind(rightUpperLeg);
        CacheBind(rightLowerLeg);
        CacheBind(spine);
        CacheBind(neck);
        CacheBind(head);

        smoothedHipPos = hips != null ? hips.position : transform.position;
    }

    void CacheBind(Transform bone)
    {
        if (bone == null || bone.parent == null) return;

        Vector3 aimParent;
        // Prefer child direction; fallback to bone's local +X in parent space (Mixamo bones commonly aim along +X)
        if (bone.childCount > 0)
        {
            // If multiple children, pick the first non-finger/toe child
            Transform child = bone.GetChild(0);
            aimParent = bone.parent.InverseTransformDirection((child.position - bone.position).normalized);
        }
        else
        {
            // Convert local +X to parent space using bind local rotation
            aimParent = (bone.localRotation * Vector3.right).normalized;
        }

        bindMap[bone] = new BoneBind
        {
            bone = bone,
            bindLocalRot = bone.localRotation,
            bindAimParent = aimParent
        };
    }

    void LateUpdate()
    {
        if (poseReceiver == null || poseReceiver.latestPose == null) return;

        // Get key landmark positions (PoseReceiver should return world positions)
        Vector3 lShoulder = poseReceiver.GetWorldPosition(11, scale);
        Vector3 rShoulder = poseReceiver.GetWorldPosition(12, scale);
        Vector3 lElbow = poseReceiver.GetWorldPosition(13, scale);
        Vector3 rElbow = poseReceiver.GetWorldPosition(14, scale);
        Vector3 lWrist = poseReceiver.GetWorldPosition(15, scale);
        Vector3 rWrist = poseReceiver.GetWorldPosition(16, scale);
        Vector3 lHip = poseReceiver.GetWorldPosition(23, scale);
        Vector3 rHip = poseReceiver.GetWorldPosition(24, scale);
        Vector3 lKnee = poseReceiver.GetWorldPosition(25, scale);
        Vector3 rKnee = poseReceiver.GetWorldPosition(26, scale);
        Vector3 lAnkle = poseReceiver.GetWorldPosition(27, scale);
        Vector3 rAnkle = poseReceiver.GetWorldPosition(28, scale);
        Vector3 nose = poseReceiver.GetWorldPosition(0, scale);

        Vector3 hipCenter = (lHip + rHip) * 0.5f;
        Vector3 shoulderCenter = (lShoulder + rShoulder) * 0.5f;

        // Root follow (position)
        if (followHipCenter && hips != null && hipCenter != Vector3.zero)
        {
            float rt = 1f - Mathf.Exp(-rootSmooth * Time.deltaTime);
            Vector3 target = hipsStartPos + (hipCenter * 1.5f);
            smoothedHipPos = Vector3.Lerp(smoothedHipPos, target, rt);
            hips.position = smoothedHipPos;
        }

        // Root follow (rotation)
        if (followRootRotation && hips != null)
        {
            Vector3 bodyForward = ComputeBodyForward(lShoulder, rShoulder, lHip, rHip, shoulderCenter, nose, yawOnly, useNoseToDisambiguate);

            if (bodyForward.sqrMagnitude > 1e-6f)
            {
                Quaternion look = Quaternion.LookRotation(bodyForward, Vector3.up);

                // Apply correction tilt each frame
                Quaternion correction = Quaternion.Euler(rootRotationOffsetEuler);
                Quaternion targetRot = correction * look;

                float tRot = 1f - Mathf.Exp(-rootRotationSmooth * Time.deltaTime);
                hips.rotation = Quaternion.Slerp(hips.rotation, targetRot, tRot);
            }
        }

        // Arms
        AimBone(leftUpperArm, rShoulder, rElbow);
        AimBone(leftLowerArm, rElbow, rWrist);
        AimBone(rightUpperArm, lShoulder, lElbow);
        AimBone(rightLowerArm, lElbow, lWrist);

        // Legs
        AimBone(leftUpperLeg, rHip, rKnee);
        AimBone(leftLowerLeg, rKnee, rAnkle);
        AimBone(rightUpperLeg, lHip, lKnee);
        AimBone(rightLowerLeg, lKnee, lAnkle);

        // Spine & head
        AimBone(spine, hipCenter, shoulderCenter);
        AimBone(neck, shoulderCenter, nose);
        AimBone(head, shoulderCenter, nose);

        ResetFingersAndToes();
    }

    Vector3 ComputeBodyForward(
        Vector3 lShoulder, Vector3 rShoulder,
        Vector3 lHip, Vector3 rHip,
        Vector3 shoulderCenter, Vector3 nose,
        bool yawOnlyForward, bool disambiguateWithNose)
    {
        const float eps = 1e-6f;

        // Primary: shoulders
        Vector3 forward = Vector3.zero;
        Vector3 shoulderDir = rShoulder - lShoulder;
        if (shoulderDir.sqrMagnitude > eps && lShoulder != Vector3.zero && rShoulder != Vector3.zero)
        {
            forward = Vector3.Cross(Vector3.up, shoulderDir).normalized;
        }
        else
        {
            // Fallback: hips
            Vector3 hipDir = rHip - lHip;
            if (hipDir.sqrMagnitude > eps && lHip != Vector3.zero && rHip != Vector3.zero)
                forward = Vector3.Cross(Vector3.up, hipDir).normalized;
        }

        if (yawOnlyForward)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude > eps) forward.Normalize();
        }

        // Disambiguate 180° using nose
        if (disambiguateWithNose && nose != Vector3.zero && shoulderCenter != Vector3.zero && forward.sqrMagnitude > eps)
        {
            Vector3 faceDir = (nose - shoulderCenter);
            if (yawOnlyForward) faceDir.y = 0f;
            if (faceDir.sqrMagnitude > eps)
            {
                faceDir.Normalize();
                if (Vector3.Dot(forward, faceDir) < 0f)
                    forward = -forward;
            }
        }

        return forward;
    }

    void AimBone(Transform bone, Vector3 startWorld, Vector3 endWorld)
    {
        if (bone == null || bone.parent == null) return;
        if (startWorld == Vector3.zero || endWorld == Vector3.zero) return;

        Vector3 dirWorld = endWorld - startWorld;
        if (dirWorld.sqrMagnitude < 1e-6f) return;

        // Target direction expressed in parent space
        Vector3 targetDirParent = bone.parent.InverseTransformDirection(dirWorld.normalized);

        // Get bind data; if missing, cache now
        if (!bindMap.TryGetValue(bone, out var bind))
        {
            CacheBind(bone);
            if (!bindMap.TryGetValue(bone, out bind)) return;
        }

        // Rotate from bind aim to current target direction (both in parent space)
        Quaternion deltaParent = Quaternion.FromToRotation(bind.bindAimParent, targetDirParent);

        // Desired local rotation = delta * bindLocalRot
        Quaternion desiredLocal = deltaParent * bind.bindLocalRot;

        // Apply optional local-space offsets (for rigs whose bone forward isn't +X)
        Quaternion globalOffset = Quaternion.Euler(globalAxisOffset);
        if (perBoneOffsetMap.TryGetValue(bone, out var perBoneOffset))
            desiredLocal = desiredLocal * globalOffset * perBoneOffset;
        else
            desiredLocal = desiredLocal * globalOffset;

        // Smooth
        float t = 1f - Mathf.Exp(-smoothFactor * Time.deltaTime);
        bone.localRotation = Quaternion.Slerp(bone.localRotation, desiredLocal, t);
    }

    private void ResetFingersAndToes()
    {
        for (int i = 0; i < allBones.Length; i++)
        {
            var bone = allBones[i];
            if (bone == null) continue;

            string lname = bone.name.ToLowerInvariant();
            if (lname.Contains("finger") || lname.Contains("thumb") || lname.Contains("toe"))
            {
                bone.localRotation = bindPoseRotations[i];
            }
        }
    }

    void OnDrawGizmos()
    {
        if (poseReceiver?.latestPose?.landmarks == null) return;

        Gizmos.color = Color.cyan;
        for (int i = 0; i < poseReceiver.latestPose.landmarks.Count; i++)
        {
            Vector3 pos = poseReceiver.GetWorldPosition(i, scale);
            Gizmos.DrawSphere(pos, 0.01f);
        }

        Gizmos.color = Color.red;
        DrawBoneDirection(leftUpperArm, leftLowerArm);
        DrawBoneDirection(rightUpperArm, rightLowerArm);
        DrawBoneDirection(leftUpperLeg, leftLowerLeg);
        DrawBoneDirection(rightUpperLeg, rightLowerLeg);
    }

    private void DrawBoneDirection(Transform startBone, Transform endBone)
    {
        if (startBone == null || endBone == null) return;
        Gizmos.DrawLine(startBone.position, endBone.position);
    }
}