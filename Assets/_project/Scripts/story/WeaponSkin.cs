using UnityEngine;

/// <summary>
/// A second look for a weapon: swaps the mesh and materials on the SAME object, so the hand mount, the blade
/// hitbox (BoxCollider + HitDetector) and WeaponAim are untouched. Musashi trains with the bokken and fights Arima
/// with a real katana (the user's call, 2026-11-04 - the cut scene where he takes it comes later); the two meshes
/// share one local frame (centre and size agree within 0.0003), so nothing moves.
/// </summary>
public class WeaponSkin : MonoBehaviour
{
    [Tooltip("The mesh shown while the alternate look is on (Miyamoto_Katana for Musashi's bokken).")]
    public Mesh altMesh;
    [Tooltip("Its materials.")]
    public Material[] altMaterials;

    Mesh baseMesh;
    Material[] baseMaterials;
    bool captured;

    public bool UsingAlt { get; private set; }

    void Capture()
    {
        if (captured) return;
        var mf = GetComponent<MeshFilter>();
        var mr = GetComponent<MeshRenderer>();
        baseMesh = mf != null ? mf.sharedMesh : null;
        baseMaterials = mr != null ? mr.sharedMaterials : null;
        captured = true;
    }

    public void Use(bool alt)
    {
        Capture();
        var mf = GetComponent<MeshFilter>();
        var mr = GetComponent<MeshRenderer>();
        if (alt && altMesh == null) { Debug.LogWarning($"{name}: no alternate mesh set"); return; }
        if (mf != null) mf.sharedMesh = alt ? altMesh : baseMesh;
        if (mr != null) mr.sharedMaterials = alt ? altMaterials : baseMaterials;
        UsingAlt = alt;
        Debug.Log($"{name}: {(alt ? altMesh.name : "own")} look");
    }

    /// <summary>Every WeaponSkin under a fighter.</summary>
    public static void UseAll(Component fighter, bool alt)
    {
        if (fighter == null) return;
        foreach (var w in fighter.GetComponentsInChildren<WeaponSkin>(true)) w.Use(alt);
    }
}
