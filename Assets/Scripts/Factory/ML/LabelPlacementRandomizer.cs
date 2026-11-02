using System;
using UnityEngine;

/// <summary>학습용 복사본의 송장 전체(종이/글자/바코드)를 같은 면 안에서 이동·회전한다.</summary>
[Serializable]
public class LabelPlacementRandomizer
{
    public bool enabled = true;
    [Tooltip("송장 자체를 면의 법선 둘레로 회전하는 최대 각도. 180이면 전체 방향을 포함한다.")]
    [Range(0f, 180f)] public float rotationRange = 180f;
    [Tooltip("0은 원래 위치(필요하면 경계 안으로 보정), 1은 면 안의 가능한 위치 전체.")]
    [Range(0f, 1f)] public float positionRange = 1f;
    [Tooltip("면의 각 변 길이에 대한 가장자리 여백 비율.")]
    [Range(0f, 0.2f)] public float edgeMargin = 0.02f;

    public sealed class Placement
    {
        readonly Transform box, label;
        readonly Vector3 position;
        readonly Quaternion rotation;
        readonly Vector3 normal;
        readonly Bounds bodyBounds;
        readonly bool valid;
        bool warned;

        public Placement(GameObject boxObject, GameObject labelObject, Vector3 localNormal)
        {
            box = boxObject.transform;
            if (labelObject == null) return;
            label = labelObject.transform;
            position = label.localPosition;
            rotation = label.localRotation;
            // 프로젝트의 BoxEntry 법선은 상자 로컬 축 방향이다.
            var n = localNormal.normalized;
            int axis = Mathf.Abs(n.x) > Mathf.Abs(n.y) ? 0 : 1;
            if (Mathf.Abs(n.z) > Mathf.Abs(n[axis])) axis = 2;
            normal = Vector3.zero;
            normal[axis] = Mathf.Sign(n[axis]);
            valid = TryBounds(box, label, false, out bodyBounds);
        }

        public bool Apply(LabelPlacementRandomizer settings, Func<double> random)
        {
            if (label == null) return false;
            // 재사용하는 데이터셋 상자에서도 이동/회전이 누적되지 않는다.
            label.localPosition = position;
            label.localRotation = rotation;
            if (settings == null || !settings.enabled) return true;
            if (!valid) return Fail();
            int axis = normal.x != 0 ? 0 : normal.y != 0 ? 1 : 2;
            int u = (axis + 1) % 3, v = (axis + 2) % 3;
            Quaternion originalWorldRotation = label.rotation;
            // 긴 송장은 일부 각도에서 면보다 커진다. 가능한 회전만 사용한다.
            for (int attempt = 0; attempt < 33; attempt++)
            {
                label.localPosition = position;
                float angle = attempt == 32 ? 0f : ((float)random() * 2f - 1f) * settings.rotationRange;
                label.rotation = Quaternion.AngleAxis(angle, box.TransformDirection(normal)) * originalWorldRotation;
                if (!TryBounds(box, label, true, out Bounds paper)) break;
                Vector3 lower = bodyBounds.min - paper.min;
                Vector3 upper = bodyBounds.max - paper.max;
                float marginU = bodyBounds.size[u] * settings.edgeMargin;
                float marginV = bodyBounds.size[v] * settings.edgeMargin;
                lower[u] += marginU; upper[u] -= marginU;
                lower[v] += marginV; upper[v] -= marginV;
                if (lower[u] > upper[u] || lower[v] > upper[v]) continue;
                Vector3 shift = Vector3.zero;
                float range = Mathf.Clamp01(settings.positionRange);
                shift[u] = Mathf.Lerp(Mathf.Clamp(0f, lower[u], upper[u]),
                    Mathf.Lerp(lower[u], upper[u], (float)random()), range);
                shift[v] = Mathf.Lerp(Mathf.Clamp(0f, lower[v], upper[v]),
                    Mathf.Lerp(lower[v], upper[v], (float)random()), range);
                // 법선 방향은 이동하지 않아 원래 부착 높이를 유지한다.
                label.position += box.TransformVector(shift);
                return true;
            }
            label.localPosition = position;
            label.localRotation = rotation;
            return Fail();
        }

        bool Fail()
        {
            if (!warned)
            {
                Debug.LogWarning("[LabelPlacement] 송장/상자 경계를 확인하세요. 면 안에 배치할 수 없어 원래 배치를 사용합니다.", box);
                warned = true;
            }
            return false;
        }

        // 월드 AABB 대신 렌더러 로컬 경계의 8꼭짓점을 상자 좌표로 옮긴다.
        // 회전된 상자 및 종이/글자/바코드가 묶인 송장을 함께 처리한다.
        static bool TryBounds(Transform box, Transform label, bool labelOnly, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            foreach (var renderer in box.GetComponentsInChildren<Renderer>(true))
            {
                bool isLabel = renderer.transform == label || renderer.transform.IsChildOf(label);
                if (isLabel != labelOnly || !renderer.enabled) continue;
                var local = renderer.localBounds;
                var matrix = box.worldToLocalMatrix * renderer.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var point = new Vector3((i & 1) == 0 ? local.min.x : local.max.x,
                        (i & 2) == 0 ? local.min.y : local.max.y,
                        (i & 4) == 0 ? local.min.z : local.max.z);
                    point = matrix.MultiplyPoint3x4(point);
                    if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                    else bounds.Encapsulate(point);
                }
            }
            return found;
        }
    }
}
