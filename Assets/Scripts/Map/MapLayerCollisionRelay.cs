using UnityEngine;

public class MapLayerCollisionRelay : MonoBehaviour
{
    [SerializeField] private MapGenerator mapGenerator;
    [SerializeField] private string layerName;
    [SerializeField] private int routePointIndex = -1;

    public void Initialize(MapGenerator generator, string targetLayerName)
    {
        Initialize(generator, targetLayerName, -1);
    }

    public void Initialize(MapGenerator generator, string targetLayerName, int targetRoutePointIndex)
    {
        mapGenerator = generator;
        layerName = targetLayerName;
        routePointIndex = targetRoutePointIndex;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (mapGenerator == null)
        {
            return;
        }

        mapGenerator.NotifyLayerCollision(layerName, collision);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (mapGenerator == null)
        {
            return;
        }

        mapGenerator.NotifyLayerTrigger(layerName, routePointIndex, other);
    }
}
