using UnityEngine;

namespace ConveyorWorks
{
    [CreateAssetMenu(fileName = "NewColliderMesh", menuName = "ConveyorWorks/Collider Mesh")]
    public class ColliderMeshObject : ScriptableObject
    {
        public Mesh mesh;
    }
}
