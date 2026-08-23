using System;
using UnityEngine;

namespace SkinWeightTools
{
    public enum FalloffPreset
    {
        Linear = 0,
        Smooth = 1,
        Sharp = 2,
        Wide = 3,
        Shelf = 4
    }

    [Serializable]
    public class EvenOutSettings
    {
        [Tooltip("Target influences per vertex. 2, 3 or 4.")]
        public int maxInfluences = 4;

        [Tooltip("Influences below this are treated as absent.")]
        public float weightThreshold = 0.001f;

        [Tooltip("A vertex seeds a bone's distance field when it holds at least this weight.")]
        public float seedThreshold = 0.4f;

        [Tooltip("Measure distance in edge hops instead of metres.")]
        public bool useHopCount = false;

        [Tooltip("Search radius in metres, or in edge hops when useHopCount is on.")]
        public float searchRadius = 0.12f;

        [Tooltip("Bone influence as a function of normalised topological distance.")]
        public AnimationCurve falloff = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        [Range(0f, 1f)]
        [Tooltip("0 keeps the original weight ratios, 1 drives them entirely from the falloff curve.")]
        public float falloffBlend = 0.35f;

        [Range(0f, 1f)]
        [Tooltip("Scales bones the vertex did not originally have. Lower values stay closer to the source rig.")]
        public float newBoneBias = 0.6f;

        [Tooltip("Laplacian smoothing passes applied before culling.")]
        public int smoothIterations = 2;

        [Range(0f, 1f)]
        public float smoothStrength = 0.5f;

        [Tooltip("Force the original strongest bone to survive culling.")]
        public bool preserveDominantBone = true;

        [Tooltip("Seed a bone from the vertices it dominates when nothing clears the seed threshold. Prevents thinly weighted bones being dropped entirely.")]
        public bool useDominantFallbackSeeding = true;

        public void ApplyFalloffPreset(FalloffPreset preset)
        {
            if (preset == FalloffPreset.Linear)
            {
                falloff = AnimationCurve.Linear(0f, 1f, 1f, 0f);
                return;
            }

            if (preset == FalloffPreset.Smooth)
            {
                falloff = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
                return;
            }

            if (preset == FalloffPreset.Sharp)
            {
                Keyframe[] sharpKeys = new Keyframe[3];
                sharpKeys[0] = new Keyframe(0f, 1f, 0f, -3f);
                sharpKeys[1] = new Keyframe(0.3f, 0.2f, -1.2f, -1.2f);
                sharpKeys[2] = new Keyframe(1f, 0f, -0.1f, 0f);
                falloff = new AnimationCurve(sharpKeys);
                return;
            }

            if (preset == FalloffPreset.Wide)
            {
                Keyframe[] wideKeys = new Keyframe[3];
                wideKeys[0] = new Keyframe(0f, 1f, 0f, -0.2f);
                wideKeys[1] = new Keyframe(0.7f, 0.75f, -0.6f, -0.6f);
                wideKeys[2] = new Keyframe(1f, 0f, -2.5f, 0f);
                falloff = new AnimationCurve(wideKeys);
                return;
            }

            Keyframe[] shelfKeys = new Keyframe[4];
            shelfKeys[0] = new Keyframe(0f, 1f, 0f, 0f);
            shelfKeys[1] = new Keyframe(0.45f, 0.95f, 0f, 0f);
            shelfKeys[2] = new Keyframe(0.55f, 0.15f, -4f, -4f);
            shelfKeys[3] = new Keyframe(1f, 0f, 0f, 0f);
            falloff = new AnimationCurve(shelfKeys);
        }

        public EvenOutSettings Clone()
        {
            EvenOutSettings copy = new EvenOutSettings();
            copy.maxInfluences = maxInfluences;
            copy.weightThreshold = weightThreshold;
            copy.seedThreshold = seedThreshold;
            copy.useHopCount = useHopCount;
            copy.searchRadius = searchRadius;
            copy.falloff = new AnimationCurve(falloff.keys);
            copy.falloffBlend = falloffBlend;
            copy.newBoneBias = newBoneBias;
            copy.smoothIterations = smoothIterations;
            copy.smoothStrength = smoothStrength;
            copy.preserveDominantBone = preserveDominantBone;
            copy.useDominantFallbackSeeding = useDominantFallbackSeeding;
            return copy;
        }
    }
}
