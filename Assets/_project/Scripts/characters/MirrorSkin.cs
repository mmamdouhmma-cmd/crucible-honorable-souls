using UnityEngine;

/// <summary>
/// Player 2's colours in a MIRROR match - both players on the same fighter. The menu clones the
/// fighter for Player 2 (<c>GameMenu.EnsureTwin</c>) and this re-dresses the clone's BODY in the
/// alternate basecolor from <c>Resources/MirrorSkins/&lt;Name&gt;_P2</c>, made offline by
/// <c>ArtSource/Textures/make_mirror_skins.py</c> (the palette is the user's pick, 2026-10-26).
/// Only the skinned body changes - weapons and shields stay as they are - and only on the
/// clone's own material instances, so the original fighter is never touched.
/// </summary>
public static class MirrorSkin
{
    const string Folder = "MirrorSkins/";

    // No texture on disk (a fresh clone that has not run the script): a cool cast over the whole
    // body still tells the two apart, just less handsomely.
    static readonly Color FallbackTint = new Color(0.62f, 0.72f, 1.0f, 1f);

    public static void Apply(Fighter fighter)
    {
        if (fighter == null) return;
        var alt = Resources.Load<Texture2D>(Folder + fighter.CharacterName + "_P2");
        if (alt == null)
            Debug.LogWarning($"MirrorSkin: no {Folder}{fighter.CharacterName}_P2 texture - tinting Player 2's {fighter.CharacterName} instead");

        foreach (var body in fighter.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mats = body.materials;                       // instances: the original keeps his
            foreach (var m in mats)
            {
                if (m == null) continue;
                if (alt != null && m.HasProperty("_BaseMap"))
                {
                    m.SetTexture("_BaseMap", alt);
                    if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", alt);
                }
                else if (m.HasProperty("_BaseColor"))
                    m.SetColor("_BaseColor", m.GetColor("_BaseColor") * FallbackTint);
            }
            body.materials = mats;
        }
    }
}
