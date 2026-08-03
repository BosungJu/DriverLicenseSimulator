using UnityEngine;

public class MapLayerCollisionRelay : MonoBehaviour
{
    private MapGenerator mapGenerator;
    private string layerName;

    public void Initialize(MapGenerator generator, string targetLayerName)
    {
        mapGenerator = generator;
        layerName = targetLayerName;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (mapGenerator == null)
        {
            return;
        }

        mapGenerator.NotifyLayerCollision(layerName, collision);
    }
}
