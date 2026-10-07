using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Leans a UI graphic sideways (a shear), turning a rectangle into a slanted bar.
/// Sits next to an Image or other Graphic.
/// </summary>
public class UISkew : BaseMeshEffect
{
    [Tooltip("Horizontal shift per unit of height. Positive leans right, negative leans left.")]
    public float amount = 0.45f;

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive()) return;

        UIVertex vertex = new UIVertex();
        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref vertex, i);
            vertex.position.x += vertex.position.y * amount;
            vh.SetUIVertex(vertex, i);
        }
    }
}
