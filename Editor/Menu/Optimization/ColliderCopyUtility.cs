using UnityEditor;
using UnityEngine;

namespace Xprees.EditorTools.Editor.Menu.Optimization
{
    public static class ColliderCopyUtility
    {
        public const string CollidersContainerName = "Colliders";
        private const float Epsilon = 1e-4f;

        /// Copies the collider into the target's "Colliders" container so it keeps its world position, rotation and scale, using as few objects as
        /// possible. It goes on the first object (the container, then its children) where it is exact: box with rotation in 90° steps; sphere with
        /// uniform scale; capsule both; mesh collider (no center) only at the same pose. Otherwise a new child with the source's world pose is created.
        /// The container and the child objects are always fully static (collider only, they never move). Undoable.
        public static Collider CopyColliderTo(this Collider source, GameObject target)
        {
            if (source is not (BoxCollider or SphereCollider or CapsuleCollider or MeshCollider)) return null; // Unsupported collider type

            var container = GetOrCreateContainer(target);
            var copy = TryCopyInto(source, container.gameObject);
            if (copy) return copy;

            foreach (Transform frame in container)
            {
                copy = TryCopyInto(source, frame.gameObject);
                if (copy) return copy;
            }

            return AddCopy(source, CreateChild(source, container)); // Same pose: exact for any collider
        }

        /// Exact copy on the frame object, or null when the collider can't be expressed there (or the layer differs).
        private static Collider TryCopyInto(Collider source, GameObject frame)
        {
            if (frame.layer != source.gameObject.layer) return null;

            // Source collider space -> frame space
            var relative = frame.transform.worldToLocalMatrix * source.transform.localToWorldMatrix;
            return source switch
            {
                BoxCollider box when IsAxisAligned(relative) => CopyBox(box, frame, relative),
                SphereCollider sphere when TryGetUniformScale(relative, out var scale) => CopySphere(sphere, frame, relative, scale),
                CapsuleCollider capsule when TryGetUniformScale(relative, out var scale) && IsAxisAligned(relative) =>
                    CopyCapsule(capsule, frame, relative, scale),
                MeshCollider when IsIdentity(relative) => AddCopy(source, frame),
                _ => null,
            };
        }

        private static Collider CopyBox(BoxCollider source, GameObject target, Matrix4x4 relative)
        {
            var copy = AddCopy(source, target);
            copy.center = relative.MultiplyPoint3x4(source.center);
            copy.size = AbsMultiply(relative, source.size);
            return copy;
        }

        private static Collider CopySphere(SphereCollider source, GameObject target, Matrix4x4 relative, float scale)
        {
            var copy = AddCopy(source, target);
            copy.center = relative.MultiplyPoint3x4(source.center);
            copy.radius = source.radius * scale;
            return copy;
        }

        private static Collider CopyCapsule(CapsuleCollider source, GameObject target, Matrix4x4 relative, float scale)
        {
            var copy = AddCopy(source, target);
            copy.center = relative.MultiplyPoint3x4(source.center);
            copy.radius = source.radius * scale;
            copy.height = source.height * scale;
            copy.direction = DominantAxis(relative.GetColumn(source.direction));
            return copy;
        }

        /// A child with the source's world pose (convex/concave meshes, rotated boxes, non-uniformly scaled spheres...). Later colliders at the same
        /// pose or rotation reuse it.
        private static GameObject CreateChild(Collider source, Transform container)
        {
            // Collider-only objects never move: always static, also under a "Dynamic" combined mesh (originals not marked static)
            var child = new GameObject($"{source.name} Collider") { layer = source.gameObject.layer, isStatic = true };
            child.transform.SetParent(container, false);
            child.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            child.transform.localScale = Divide(source.transform.lossyScale, container.lossyScale);
            Undo.RegisterCreatedObjectUndo(child, "Copy collider");
            return child;
        }

        private static Transform GetOrCreateContainer(GameObject target)
        {
            var existing = target.transform.Find(CollidersContainerName);
            if (existing) return existing;

            var container = new GameObject(CollidersContainerName) { layer = target.layer, isStatic = true };
            container.transform.SetParent(target.transform, false);
            Undo.RegisterCreatedObjectUndo(container, "Copy collider");
            return container.transform;
        }

        /// Same collider type with all settings (trigger, physics material, layer overrides...); the caller fixes the center/size for the target.
        private static T AddCopy<T>(T source, GameObject target) where T : Collider
        {
            var copy = (T) Undo.AddComponent(target, source.GetType()); // Runtime type: T is the abstract Collider for the child copies
            EditorUtility.CopySerialized(source, copy);
            return copy;
        }

        /// Same pose: a mesh collider (no center/offset) can only be copied as-is.
        private static bool IsIdentity(Matrix4x4 matrix)
        {
            for (var i = 0; i < 16; i++)
            {
                if (Mathf.Abs(matrix[i] - Matrix4x4.identity[i]) > Epsilon) return false;
            }

            return true;
        }

        /// Every source axis maps onto one target axis (rotation in 90° steps, any scale).
        private static bool IsAxisAligned(Matrix4x4 matrix)
        {
            for (var column = 0; column < 3; column++)
            {
                var axis = (Vector3) matrix.GetColumn(column);
                var dominant = DominantAxis(axis);
                for (var row = 0; row < 3; row++)
                {
                    if (row != dominant && Mathf.Abs(axis[row]) > Epsilon * axis.magnitude) return false;
                }
            }

            return true;
        }

        private static bool TryGetUniformScale(Matrix4x4 matrix, out float scale)
        {
            var x = ((Vector3) matrix.GetColumn(0)).magnitude;
            var y = ((Vector3) matrix.GetColumn(1)).magnitude;
            var z = ((Vector3) matrix.GetColumn(2)).magnitude;
            scale = x;
            return Mathf.Abs(x - y) <= Epsilon * x && Mathf.Abs(x - z) <= Epsilon * x;
        }

        private static int DominantAxis(Vector3 axis)
        {
            var abs = new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));
            return abs.x >= abs.y && abs.x >= abs.z ? 0 : abs.y >= abs.z ? 1 : 2;
        }

        /// |M| * v: the size of the box in target space (exact for axis-aligned matrices).
        private static Vector3 AbsMultiply(Matrix4x4 matrix, Vector3 size)
        {
            var result = Vector3.zero;
            for (var row = 0; row < 3; row++)
            {
                for (var column = 0; column < 3; column++)
                {
                    result[row] += Mathf.Abs(matrix[row, column]) * size[column];
                }
            }

            return result;
        }

        private static Vector3 Divide(Vector3 a, Vector3 b) => new(a.x / b.x, a.y / b.y, a.z / b.z);
    }
}