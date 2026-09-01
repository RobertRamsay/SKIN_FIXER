using System.Collections.Generic;
using UnityEngine;

namespace SkinWeightTools.App
{
    /// <summary>
    /// Drives an arbitrary skinned rig through a range of motion without an Animator or a
    /// humanoid avatar. RomSequence resolves joints through HumanBodyBones and gives up when
    /// animator.isHuman is false, which is every model loaded from a glTF at runtime, so the
    /// bones come straight off the SkinnedMeshRenderer instead.
    ///
    /// Each driven bone swings about an axis perpendicular to the direction of its first
    /// child, which is the axis a limb actually bends around whatever the export convention.
    /// </summary>
    public class GenericRomAnimator : MonoBehaviour
    {
        private class DrivenBone
        {
            public Transform transform = null;
            public Quaternion restRotation = Quaternion.identity;
            public Vector3 bendAxis = Vector3.right;
            public float phaseOffset = 0f;
        }

        [Tooltip("Peak bend angle in degrees, either side of the rest pose.")]
        [Range(5f, 90f)]
        public float amplitude = 45f;

        [Tooltip("Full bend cycles per second.")]
        [Range(0.05f, 2f)]
        public float speed = 0.35f;

        [Tooltip("Spreads bones through the cycle so the whole rig does not move as one block.")]
        public bool staggerBones = true;

        [Tooltip("Drive one bone at a time instead of the whole rig. Use isolatedBone to choose.")]
        public bool isolateSingleBone = false;

        public int isolatedBone = 0;

        private readonly List<DrivenBone> drivenBones = new List<DrivenBone>();
        private bool playing = false;
        private float cycleTime = 0f;

        public bool IsPlaying
        {
            get { return playing; }
        }

        public int DrivenBoneCount
        {
            get { return drivenBones.Count; }
        }

        public string IsolatedBoneName
        {
            get
            {
                if (drivenBones.Count == 0)
                {
                    return "none";
                }

                int index = Mathf.Clamp(isolatedBone, 0, drivenBones.Count - 1);
                return drivenBones[index].transform.name;
            }
        }

        /// <summary>Collects drivable joints from every renderer and caches the rest pose.</summary>
        public void Bind(IList<SkinnedMeshRenderer> renderers)
        {
            Stop();
            drivenBones.Clear();

            if (renderers == null)
            {
                return;
            }

            HashSet<Transform> allBones = new HashSet<Transform>();

            for (int index = 0; index < renderers.Count; index++)
            {
                SkinnedMeshRenderer renderer = renderers[index];

                if (renderer == null)
                {
                    continue;
                }

                Transform[] bones = renderer.bones;

                for (int slot = 0; slot < bones.Length; slot++)
                {
                    if (bones[slot] != null)
                    {
                        allBones.Add(bones[slot]);
                    }
                }
            }

            foreach (Transform bone in allBones)
            {
                Transform child = FindChildBone(bone, allBones);

                if (child == null)
                {
                    // A leaf bone has no direction to derive a bend axis from, and a tip
                    // joint contributes almost nothing to the deformation being tested.
                    continue;
                }

                Vector3 toChild = child.position - bone.position;

                if (toChild.sqrMagnitude < 1e-10f)
                {
                    continue;
                }

                DrivenBone entry = new DrivenBone();
                entry.transform = bone;
                entry.restRotation = bone.localRotation;
                entry.bendAxis = BuildBendAxis(bone, toChild);
                drivenBones.Add(entry);
            }

            drivenBones.Sort(CompareByHierarchyDepth);

            for (int index = 0; index < drivenBones.Count; index++)
            {
                drivenBones[index].phaseOffset = (float)index * 0.37f;
            }
        }

        private static Transform FindChildBone(Transform bone, HashSet<Transform> allBones)
        {
            for (int index = 0; index < bone.childCount; index++)
            {
                Transform child = bone.GetChild(index);

                if (allBones.Contains(child) == true)
                {
                    return child;
                }
            }

            // Fall back to any child. An unskinned intermediate node still shows which way
            // the chain continues.
            if (bone.childCount > 0)
            {
                return bone.GetChild(0);
            }

            return null;
        }

        /// <summary>
        /// A limb bends about an axis at right angles to its own length. Crossing the local
        /// child direction with a world reference gives that axis without knowing whether
        /// the exporter ran X, Y or Z down the bone.
        /// </summary>
        private static Vector3 BuildBendAxis(Transform bone, Vector3 toChildWorld)
        {
            Vector3 localDirection = bone.InverseTransformDirection(toChildWorld.normalized);
            Vector3 reference = bone.InverseTransformDirection(Vector3.up);

            if (Mathf.Abs(Vector3.Dot(localDirection, reference)) > 0.95f)
            {
                reference = bone.InverseTransformDirection(Vector3.forward);
            }

            Vector3 axis = Vector3.Cross(localDirection, reference);

            if (axis.sqrMagnitude < 1e-8f)
            {
                return Vector3.right;
            }

            return axis.normalized;
        }

        private static int CompareByHierarchyDepth(DrivenBone left, DrivenBone right)
        {
            return Depth(left.transform).CompareTo(Depth(right.transform));
        }

        private static int Depth(Transform transform)
        {
            int depth = 0;
            Transform cursor = transform;

            while (cursor.parent != null)
            {
                depth = depth + 1;
                cursor = cursor.parent;
            }

            return depth;
        }

        public void Play()
        {
            if (drivenBones.Count == 0)
            {
                return;
            }

            playing = true;
        }

        public void Pause()
        {
            playing = false;
        }

        public void TogglePlay()
        {
            if (playing == true)
            {
                Pause();
                return;
            }

            Play();
        }

        /// <summary>Stops and puts every driven bone back on its rest rotation.</summary>
        public void Stop()
        {
            playing = false;
            cycleTime = 0f;

            for (int index = 0; index < drivenBones.Count; index++)
            {
                DrivenBone entry = drivenBones[index];

                if (entry.transform != null)
                {
                    entry.transform.localRotation = entry.restRotation;
                }
            }
        }

        public void StepIsolatedBone(int delta)
        {
            if (drivenBones.Count == 0)
            {
                return;
            }

            // Return the bone we are stepping away from to rest, or it stays bent.
            int previous = Mathf.Clamp(isolatedBone, 0, drivenBones.Count - 1);
            DrivenBone previousBone = drivenBones[previous];

            if (previousBone.transform != null)
            {
                previousBone.transform.localRotation = previousBone.restRotation;
            }

            isolatedBone = (int)Mathf.Repeat(isolatedBone + delta, drivenBones.Count);
        }

        private void Update()
        {
            if (playing == false || drivenBones.Count == 0)
            {
                return;
            }

            cycleTime = cycleTime + Time.deltaTime * speed * Mathf.PI * 2f;

            if (isolateSingleBone == true)
            {
                int index = Mathf.Clamp(isolatedBone, 0, drivenBones.Count - 1);
                ApplyBone(drivenBones[index], cycleTime);
                return;
            }

            for (int index = 0; index < drivenBones.Count; index++)
            {
                DrivenBone entry = drivenBones[index];
                float phase = cycleTime;

                if (staggerBones == true)
                {
                    phase = phase + entry.phaseOffset;
                }

                ApplyBone(entry, phase);
            }
        }

        private void ApplyBone(DrivenBone entry, float phase)
        {
            if (entry.transform == null)
            {
                return;
            }

            float angle = Mathf.Sin(phase) * amplitude;
            entry.transform.localRotation = entry.restRotation * Quaternion.AngleAxis(angle, entry.bendAxis);
        }

        private void OnDisable()
        {
            Stop();
        }
    }
}
