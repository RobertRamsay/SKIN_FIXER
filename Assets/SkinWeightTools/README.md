# Skin Weight Tools

Tooling for repairing bad skin weights on imported skinned meshes.
Unity 6.3 (6000.3.x), URP.

There are two front ends over the same solver:

- **Editor window** — `Tools > Skin Weight Tools > Weight Editor`. Full control, bakes
  mesh assets, works on anything already imported into the project.
- **Runtime app** — `Tools > Skin Weight Tools > Create Runtime Scene`, then press Play.
  Pick a file from disk, watch the rig move, and re-solve while it moves.

## Runtime app

1. **LOAD MODEL...** opens a native file dialog. glTF and GLB only, see below.
2. The topology graph is built on load. This is the slow step, a few seconds on a dense
   mesh, and the status line reports progress.
3. **PLAY ROM** sweeps every joint through its range so the deformation is visible.
   Bend angle and speed are live.
4. **EVEN OUT 2 / 3 / 4** re-solves from the source weights, as in the editor window.
   **APPLY SMOOTH PASS** stacks one Laplacian pass per press, and **CULL TO INFLUENCE
   BUDGET** trims the influence count that smoothing pushes back up.
5. **REVERT** restores the weights the file shipped with.

Left drag orbits, middle drag pans, wheel zooms. Results live in memory only; to keep a
fix, bring the model into the project and use the editor window, which bakes mesh assets.

### Why runtime loading is glTF only

Unity has no runtime FBX importer. `com.unity.cloud.gltfast` reads glTF and GLB in a
player build; FBX would need a third party library such as TriLib. The editor window is
unaffected and still reads FBX and USD.

### Generic rigs

`RomSequence` drives joints through `HumanBodyBones` and returns nothing when
`animator.isHuman` is false, which is every model loaded from a glTF at runtime. The
runtime app uses `GenericRomAnimator` instead: it takes bones straight off the
`SkinnedMeshRenderer`, and bends each about an axis perpendicular to the direction of its
first child, so it needs no avatar and no per-joint axis table.

## Editor workflow

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
