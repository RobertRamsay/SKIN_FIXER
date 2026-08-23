# Skin Weight Tools

Editor tooling for repairing bad skin weights on imported skinned meshes.
Unity 6.3 (6000.3.x), URP.

Open with **Tools > Skin Weight Tools > Weight Editor**.

## Workflow

1. **Source tab** — import a model, or drag a `SkinnedMeshRenderer` from the scene
   into the field at the top. Press **Analyse mesh**.
2. **Even Out tab** — press EVEN OUT 2, 3 or 4. Each press re-solves from the original
   weights, so presses never stack.
3. **Layers tab** — stack tighteners, looseners and smoothers over the result.
4. **ROM tab** — drive each joint through its range and measure where the skin breaks.
   Feed the worst areas back into a layer mask.
5. **Bake result to mesh asset** — writes to `Assets/SkinWeightTools/GeneratedMeshes`.

## Format support

| Format | Requirement |
| --- | --- |
| FBX | Built in |
| USD, USDA, USDC, USDZ | `com.unity.formats.usd` |
| glTF, GLB | `com.unity.cloud.gltfast` |

Install optional packages through **Window > Package Manager > + > Add package by name**.
The Source tab shows which are present.

## How Even Out works

A naive top-N cull discards influences without redistributing them, which collapses
silhouettes at joints. Instead:

1. Every bone gets a **topological distance field**, computed with multi-source Dijkstra
   over the welded mesh graph, seeded from the vertices that bone already owns.
2. Each vertex scores every bone within `searchRadius` as
   `lerp(originalWeight, falloffCurve(d / searchRadius), falloffBlend)`.
3. The top N survive; the rest are dropped and the remainder renormalised.

`falloffBlend` at 0 reduces to a plain top-N cull, which is useful as a baseline.

## Notes and limits

- The imported mesh is never modified. Edits always target a clone, so
  **Revert renderer to original mesh** is always available.
- Vertices are welded by position before adjacency is built, because import splits them
  at UV, normal and material seams. If **Welded groups** is close to the vertex count,
  welding failed and topological distance will not cross seams — raise the weld tolerance.
- The ROM test requires a **Humanoid** Animator. Generic rigs are not resolved yet.
- Bend axes in `RomSequence.BuildDefaultHumanoid` are reasonable defaults, not universal.
  Local joint axes vary by DCC export convention. If a knee bends sideways, change that
  joint's `bendAxis`.
- Smoothing allocates a full clone per iteration. Fine to roughly 100k vertices.
