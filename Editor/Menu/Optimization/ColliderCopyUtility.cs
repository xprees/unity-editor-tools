using UnityEngine;

namespace Xprees.EditorTools.Editor.Menu.Optimization
{
    public static class ColliderCopyUtility
    {
        /// Creates a copy of the given collider on the target GameObject.
        public static Collider CopyColliderTo(this Collider source, GameObject target)
        {
            var worldCenter = source.transform.TransformPoint(GetColliderCenter(source));
            var targetLocalCenter = target.transform.InverseTransformPoint(worldCenter);
            var rotationDifference = Quaternion.Inverse(target.transform.rotation) * source.transform.rotation;

            if (source is BoxCollider box)
            {
                var copy = target.AddComponent<BoxCollider>();
                copy.center = targetLocalCenter;
                copy.size = RotateSize(box.size, rotationDifference);
                copy.isTrigger = box.isTrigger;
                CopyPhysicsMaterial(source, copy);

                return copy;
            }

            if (source is SphereCollider sphere)
            {
                var copy = target.AddComponent<SphereCollider>();
                copy.center = targetLocalCenter;
                copy.radius = sphere.radius;
                copy.isTrigger = sphere.isTrigger;
                CopyPhysicsMaterial(source, copy);

                return copy;
            }

            if (source is CapsuleCollider capsule)
            {
                var copy = target.AddComponent<CapsuleCollider>();
                copy.center = targetLocalCenter;
                copy.radius = capsule.radius;
                copy.height = capsule.height;
                copy.direction = capsule.direction;
                copy.isTrigger = capsule.isTrigger;
                CopyPhysicsMaterial(source, copy);

                return copy;
            }

            if (source is MeshCollider mesh)
            {
                // MeshCollider does not use center
                var copy = target.AddComponent<MeshCollider>();
                copy.sharedMesh = mesh.sharedMesh;
                copy.convex = mesh.convex;
                copy.isTrigger = mesh.isTrigger;
                CopyPhysicsMaterial(source, copy);

                return copy;
            }

            return null; // Unsupported collider type
        }


        private static Vector3 GetColliderCenter(Collider collider)
        {
            if (collider is BoxCollider box) return box.center;
            if (collider is SphereCollider sphere) return sphere.center;
            if (collider is CapsuleCollider capsule) return capsule.center;

            return Vector3.zero;
        }

        private static Vector3 RotateSize(Vector3 size, Quaternion rotation)
        {
            // Apply absolute values to handle rotation properly for box colliders
            var rotatedSize = rotation * size;
            return new Vector3(Mathf.Abs(rotatedSize.x), Mathf.Abs(rotatedSize.y), Mathf.Abs(rotatedSize.z));
        }

        private static void CopyPhysicsMaterial(Collider source, Collider target)
        {
            return; // Do not copy physics material, currently causing issues with adding default material when none is assigned
            var sourceMaterial = source.material;

            // Only copy if material exists and is not the default physics material
            if (sourceMaterial != null && sourceMaterial.name != "Default (PhysicMaterial)")
            {
                target.material = sourceMaterial;
            }
        }

    }
}