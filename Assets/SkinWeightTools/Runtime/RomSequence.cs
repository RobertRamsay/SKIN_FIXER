using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkinWeightTools
{
    public enum RomSymmetry
    {
        Symmetric = 0,
        Asymmetric = 1
    }

    /// <summary>One testable joint, bending inward about a local axis.</summary>
    [Serializable]
    public class RomJointDefinition
    {
        public string label = "";
        public HumanBodyBones leftBone = HumanBodyBones.LastBone;
        public HumanBodyBones rightBone = HumanBodyBones.LastBone;
        public Vector3 bendAxis = new Vector3(1f, 0f, 0f);
        public float bendAngle = 60f;
        public bool mirrorAngleOnRight = false;
        public bool enabled = true;

        public RomJointDefinition(
            string newLabel,
            HumanBodyBones newLeft,
            HumanBodyBones newRight,
            Vector3 newAxis,
            float newAngle,
            bool newMirror)
        {
            label = newLabel;
            leftBone = newLeft;
            rightBone = newRight;
            bendAxis = newAxis;
            bendAngle = newAngle;
            mirrorAngleOnRight = newMirror;
        }

        public bool IsPaired()
        {
            if (leftBone == HumanBodyBones.LastBone)
            {
                return false;
            }

            if (rightBone == HumanBodyBones.LastBone)
            {
                return false;
            }

            return true;
        }
    }

    /// <summary>One entry in the playback order: which joint, and which side or both.</summary>
    public struct RomStep
    {
        public int jointIndex;
        public bool driveLeft;
        public bool driveRight;
        public string label;
    }

    [Serializable]
    public class RomSequence
    {
        public RomSymmetry symmetry = RomSymmetry.Symmetric;
        public float defaultAngle = 60f;
        public int samplesPerStep = 8;
        public List<RomJointDefinition> joints = new List<RomJointDefinition>();

        public static RomSequence BuildDefaultHumanoid()
        {
            RomSequence sequence = new RomSequence();
            List<RomJointDefinition> list = sequence.joints;

            list.Add(new RomJointDefinition("Shoulder", HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, new Vector3(0f, 0f, 1f), 60f, true));
            list.Add(new RomJointDefinition("Elbow", HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm, new Vector3(0f, 1f, 0f), 60f, true));
            list.Add(new RomJointDefinition("Wrist", HumanBodyBones.LeftHand, HumanBodyBones.RightHand, new Vector3(0f, 0f, 1f), 60f, true));
            list.Add(new RomJointDefinition("Hip", HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, new Vector3(1f, 0f, 0f), 60f, false));
            list.Add(new RomJointDefinition("Knee", HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg, new Vector3(1f, 0f, 0f), 60f, false));
            list.Add(new RomJointDefinition("Ankle", HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, new Vector3(1f, 0f, 0f), 40f, false));
            list.Add(new RomJointDefinition("Spine", HumanBodyBones.Spine, HumanBodyBones.LastBone, new Vector3(1f, 0f, 0f), 40f, false));
            list.Add(new RomJointDefinition("Neck", HumanBodyBones.Neck, HumanBodyBones.LastBone, new Vector3(1f, 0f, 0f), 45f, false));

            return sequence;
        }

        public List<RomStep> BuildSteps()
        {
            List<RomStep> steps = new List<RomStep>();

            for (int index = 0; index < joints.Count; index++)
            {
                RomJointDefinition joint = joints[index];

                if (joint.enabled == false)
                {
                    continue;
                }

                bool hasLeft = joint.leftBone != HumanBodyBones.LastBone;
                bool hasRight = joint.rightBone != HumanBodyBones.LastBone;

                if (hasLeft == false && hasRight == false)
                {
                    continue;
                }

                if (symmetry == RomSymmetry.Symmetric || joint.IsPaired() == false)
                {
                    RomStep step = new RomStep();
                    step.jointIndex = index;
                    step.driveLeft = hasLeft;
                    step.driveRight = hasRight;
                    step.label = joint.label;

                    if (joint.IsPaired() == true)
                    {
                        step.label = joint.label + " (sym)";
                    }

                    steps.Add(step);
                    continue;
                }

                RomStep leftStep = new RomStep();
                leftStep.jointIndex = index;
                leftStep.driveLeft = true;
                leftStep.driveRight = false;
                leftStep.label = joint.label + " (L)";
                steps.Add(leftStep);

                RomStep rightStep = new RomStep();
                rightStep.jointIndex = index;
                rightStep.driveLeft = false;
                rightStep.driveRight = true;
                rightStep.label = joint.label + " (R)";
                steps.Add(rightStep);
            }

            return steps;
        }

        /// <summary>Phase runs 0 to 1 across a step. 0 is the rest pose, 1 is full bend.</summary>
        public void ApplyStep(
            RomStep step,
            float phase,
            Animator animator,
            Dictionary<HumanBodyBones, Quaternion> restRotations)
        {
            if (step.jointIndex < 0 || step.jointIndex >= joints.Count)
            {
                return;
            }

            RomJointDefinition joint = joints[step.jointIndex];
            float angle = joint.bendAngle * Mathf.Clamp01(phase);

            if (step.driveLeft == true)
            {
                ApplyToBone(animator, joint.leftBone, joint.bendAxis, angle, restRotations);
            }

            if (step.driveRight == true)
            {
                float rightAngle = angle;

                if (joint.mirrorAngleOnRight == true)
                {
                    rightAngle = -angle;
                }

                ApplyToBone(animator, joint.rightBone, joint.bendAxis, rightAngle, restRotations);
            }
        }

        private void ApplyToBone(
            Animator animator,
            HumanBodyBones bone,
            Vector3 axis,
            float angle,
            Dictionary<HumanBodyBones, Quaternion> restRotations)
        {
            Transform boneTransform = ResolveBone(animator, bone);

            if (boneTransform == null)
            {
                return;
            }

            Quaternion rest = Quaternion.identity;
            bool found = restRotations.TryGetValue(bone, out rest);

            if (found == false)
            {
                rest = boneTransform.localRotation;
                restRotations.Add(bone, rest);
            }

            boneTransform.localRotation = rest * Quaternion.AngleAxis(angle, axis.normalized);
        }

        public static Transform ResolveBone(Animator animator, HumanBodyBones bone)
        {
            if (animator == null)
            {
                return null;
            }

            if (bone == HumanBodyBones.LastBone)
            {
                return null;
            }

            if (animator.isHuman == false)
            {
                return null;
            }

            return animator.GetBoneTransform(bone);
        }

        /// <summary>Caches the rest pose of every bone the sequence can touch.</summary>
        public Dictionary<HumanBodyBones, Quaternion> CaptureRest(Animator animator)
        {
            Dictionary<HumanBodyBones, Quaternion> restRotations = new Dictionary<HumanBodyBones, Quaternion>();

            for (int index = 0; index < joints.Count; index++)
            {
                RomJointDefinition joint = joints[index];
                CaptureBone(animator, joint.leftBone, restRotations);
                CaptureBone(animator, joint.rightBone, restRotations);
            }

            return restRotations;
        }

        private void CaptureBone(
            Animator animator,
            HumanBodyBones bone,
            Dictionary<HumanBodyBones, Quaternion> restRotations)
        {
            Transform boneTransform = ResolveBone(animator, bone);

            if (boneTransform == null)
            {
                return;
            }

            if (restRotations.ContainsKey(bone) == true)
            {
                return;
            }

            restRotations.Add(bone, boneTransform.localRotation);
        }

        public List<Transform> CollectDrivenTransforms(Animator animator)
        {
            List<Transform> found = new List<Transform>();

            for (int index = 0; index < joints.Count; index++)
            {
                RomJointDefinition joint = joints[index];
                Transform leftTransform = ResolveBone(animator, joint.leftBone);
                Transform rightTransform = ResolveBone(animator, joint.rightBone);

                if (leftTransform != null && found.Contains(leftTransform) == false)
                {
                    found.Add(leftTransform);
                }

                if (rightTransform != null && found.Contains(rightTransform) == false)
                {
                    found.Add(rightTransform);
                }
            }

            return found;
        }

        public void RestoreRest(Animator animator, Dictionary<HumanBodyBones, Quaternion> restRotations)
        {
            foreach (KeyValuePair<HumanBodyBones, Quaternion> pair in restRotations)
            {
                Transform boneTransform = ResolveBone(animator, pair.Key);

                if (boneTransform != null)
                {
                    boneTransform.localRotation = pair.Value;
                }
            }
        }

        public void SetAllAngles(float angle)
        {
            defaultAngle = angle;

            for (int index = 0; index < joints.Count; index++)
            {
                joints[index].bendAngle = angle;
            }
        }
    }
}
