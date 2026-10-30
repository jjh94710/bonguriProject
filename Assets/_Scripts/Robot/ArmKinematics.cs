using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed class ArmKinematics
{
    public ArticulationBody[] Joints { get; }
    readonly Transform parent;
    readonly Node[] nodes;
    readonly float[] reference, lower, upper;
    readonly Part[] parts;
    readonly Matrix4x4[] world;
    static BoxCollider clearanceProbe;
    struct Part { public int node; public Matrix4x4 relative; public Vector3 center, size; public Collider collider; }
    struct Node { public Vector3 position, scale, anchor; public Quaternion rotation, anchorRotation; public int joint; }
    public ArmKinematics(Transform root, Transform tcp)
    {
        if (tcp == null || !tcp.IsChildOf(root)) throw new Exception("로봇 아래의 tcp를 지정하세요.");
        var chain = new List<Transform>();
        for (var t = tcp; t != root; t = t.parent) chain.Add(t);
        chain.Add(root); chain.Reverse(); parent = root.parent;
        Joints = chain.Select(t => t.GetComponent<ArticulationBody>())
            .Where(b => b != null && !b.isRoot && b.jointType == ArticulationJointType.RevoluteJoint).ToArray();
        if (Joints.Length != 6) throw new Exception("6개의 회전 관절이 필요합니다. 발견: " + Joints.Length);
        reference = CurrentAngles(); lower = new float[6]; upper = new float[6]; nodes = new Node[chain.Count];
        for (int i = 0; i < 6; i++)
        {
            var d = Joints[i].xDrive;
            lower[i] = Joints[i].twistLock == ArticulationDofLock.FreeMotion ? -360 : d.lowerLimit;
            upper[i] = Joints[i].twistLock == ArticulationDofLock.FreeMotion ? 360 : d.upperLimit;
        }
        for (int i = 0; i < chain.Count; i++)
        {
            var t = chain[i]; int j = Array.FindIndex(Joints, b => b.transform == t);
            nodes[i] = new Node { position = t.localPosition, rotation = t.localRotation, scale = t.localScale, joint = j,
                anchor = j < 0 ? Vector3.zero : Vector3.Scale(Joints[j].anchorPosition, t.localScale),
                anchorRotation = j < 0 ? Quaternion.identity : Joints[j].anchorRotation };
        }
        world = new Matrix4x4[nodes.Length];
        var geometry = new List<Part>();
        foreach (var collider in root.GetComponentsInChildren<Collider>())
        {
            if (!collider.enabled || collider.isTrigger) continue;
            Bounds bounds;
            if (collider is BoxCollider box) bounds = new Bounds(box.center, box.size);
            else if (collider is MeshCollider mesh && mesh.sharedMesh != null) bounds = mesh.sharedMesh.bounds;
            else continue;
            int index = -1;
            for (int i = chain.Count - 1; i >= 0; i--)
                if (collider.transform == chain[i] || collider.transform.IsChildOf(chain[i])) { index = i; break; }
            if (index >= 0) geometry.Add(new Part { node = index, relative = chain[index].worldToLocalMatrix * collider.transform.localToWorldMatrix, center = bounds.center, size = bounds.size, collider = collider });
        }
        parts = geometry.ToArray();
    }
    public float[] CurrentAngles()
    {
        var result = new float[Joints.Length];
        for (int i = 0; i < result.Length; i++)
        {
            var b = Joints[i];
            if (Application.isPlaying && b.dofCount > 0) result[i] = b.jointPosition[0] * Mathf.Rad2Deg;
            else
            {
                var relative = Quaternion.Inverse(b.parentAnchorRotation) * b.transform.localRotation * b.anchorRotation;
                result[i] = Mathf.DeltaAngle(0, 2 * Mathf.Atan2(relative.x, relative.w) * Mathf.Rad2Deg);
            }
        }
        return result;
    }
    public void ApplyEditorPose(float[] q)
    {
        if (Application.isPlaying) throw new InvalidOperationException("Editor pose only");
        for (int j = 0; j < Joints.Length; j++)
        {
            var body = Joints[j]; var n = nodes.First(v => v.joint == j);
            var rotation = n.rotation * n.anchorRotation * Quaternion.AngleAxis(q[j] - reference[j], Vector3.right) * Quaternion.Inverse(n.anchorRotation);
            body.transform.localRotation = rotation;
            body.transform.localPosition = n.position + n.rotation * n.anchor - rotation * n.anchor;
            var drive = body.xDrive; drive.target = q[j]; drive.targetVelocity = 0; body.xDrive = drive;
        }
    }
    public void Forward(float[] q, out Vector3 position, out Quaternion rotation)
    {
        var matrix = parent != null ? parent.localToWorldMatrix : Matrix4x4.identity;
        rotation = parent != null ? parent.rotation : Quaternion.identity;
        for (int i = 0; i < nodes.Length; i++)
        {
            var n = nodes[i];
            var r = n.rotation; var p = n.position;
            if (n.joint >= 0)
            {
                r = n.rotation * n.anchorRotation * Quaternion.AngleAxis(q[n.joint] - reference[n.joint], Vector3.right) * Quaternion.Inverse(n.anchorRotation);
                p += n.rotation * n.anchor - r * n.anchor;
            }
            matrix *= Matrix4x4.TRS(p, r, n.scale); rotation *= r;
            world[i] = matrix;
        }
        position = matrix.MultiplyPoint3x4(Vector3.zero);
    }
    public bool IsBoxClear(float[] q, BoxCollider box, float margin)
    {
        if (box == null) return false;
        if (clearanceProbe == null)
        {
            var probeObject = new GameObject("IK_ClearanceQuery_Only") { hideFlags = HideFlags.HideAndDontSave, layer = 2 };
            probeObject.transform.position = Vector3.one * -10000;
            clearanceProbe = probeObject.AddComponent<BoxCollider>(); clearanceProbe.isTrigger = true;
        }
        var scale = box.transform.lossyScale;
        clearanceProbe.transform.localScale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        clearanceProbe.center = box.center;
        clearanceProbe.size = box.size + new Vector3(2 * margin / Mathf.Abs(scale.x), 2 * margin / Mathf.Abs(scale.y), 2 * margin / Mathf.Abs(scale.z));
        Forward(q, out _, out _);
        var boxMatrix = box.transform.localToWorldMatrix;
        Vector3 boxCenter = boxMatrix.MultiplyPoint3x4(box.center);
        Vector3[] bAxes = { boxMatrix.MultiplyVector(Vector3.right), boxMatrix.MultiplyVector(Vector3.up), boxMatrix.MultiplyVector(Vector3.forward) };
        Vector3 be = box.size * .5f;
        for (int i = 0; i < 3; i++) { be[i] = be[i] * bAxes[i].magnitude + margin; bAxes[i].Normalize(); }
        foreach (var part in parts)
        {
            var matrix = world[part.node] * part.relative;
            Vector3 center = matrix.MultiplyPoint3x4(part.center);
            Vector3[] axes = { matrix.MultiplyVector(Vector3.right), matrix.MultiplyVector(Vector3.up), matrix.MultiplyVector(Vector3.forward) };
            Vector3 extents = part.size * .5f;
            for (int i = 0; i < 3; i++) { extents[i] *= axes[i].magnitude; axes[i].Normalize(); }
            if (Overlap(center, axes, extents, boxCenter, bAxes, be) &&
                Physics.ComputePenetration(clearanceProbe, box.transform.position, box.transform.rotation,
                    part.collider, matrix.MultiplyPoint3x4(Vector3.zero), matrix.rotation, out _, out _)) return false;
        }
        return true;
    }
    public bool IsDistalAbove(float[] q, float height)
    {
        Forward(q, out _, out _);
        int wrist = Array.FindIndex(nodes, n => n.joint == 3);
        foreach (var part in parts)
        {
            if (part.node < wrist) continue;
            var m = world[part.node] * part.relative;
            float radius = Mathf.Abs(m.MultiplyVector(Vector3.right * part.size.x * .5f).y)
                + Mathf.Abs(m.MultiplyVector(Vector3.up * part.size.y * .5f).y)
                + Mathf.Abs(m.MultiplyVector(Vector3.forward * part.size.z * .5f).y);
            if (m.MultiplyPoint3x4(part.center).y - radius < height) { return false; }
        }
        return true;
    }
    public bool IsElbowRaised(float[] q)
    {
        Forward(q, out _, out _);
        int shoulder = Array.FindIndex(nodes, n => n.joint == 1);
        int elbow = Array.FindIndex(nodes, n => n.joint == 2);
        Vector3 segment = world[elbow].MultiplyPoint3x4(Vector3.zero) - world[shoulder].MultiplyPoint3x4(Vector3.zero);
        return segment.y > .20f && Vector3.Dot(segment.normalized, Vector3.up) > .65f;
    }
    // Separating-axis test of conservative collider bounds; checks every arm link, not only TCP.
    static bool Overlap(Vector3 a, Vector3[] axes, Vector3 ae, Vector3 b, Vector3[] other, Vector3 be)
    {
        var difference = b - a;
        for (int i = 0; i < 15; i++)
        {
            Vector3 axis = i < 3 ? axes[i] : i < 6 ? other[i - 3] : Vector3.Cross(axes[(i - 6) / 3], other[(i - 6) % 3]);
            if (axis.sqrMagnitude < 1e-10f) continue;
            float radius = 0;
            for (int j = 0; j < 3; j++) radius += ae[j] * Mathf.Abs(Vector3.Dot(axes[j], axis)) + be[j] * Mathf.Abs(Vector3.Dot(other[j], axis));
            if (Mathf.Abs(Vector3.Dot(difference, axis)) > radius + .000001f) return false;
        }
        return true;
    }
    static Vector3 RotationError(Quaternion target, Quaternion actual)
    {
        var delta = target * Quaternion.Inverse(actual);
        if (delta.w < 0) delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
        delta.ToAngleAxis(out float angle, out Vector3 axis);
        return axis.sqrMagnitude < .1f || float.IsNaN(axis.x) ? Vector3.zero : axis * (Mathf.DeltaAngle(0, angle) * Mathf.Deg2Rad);
    }
    public bool Solve(Vector3 target, Quaternion targetRotation, float[] seed, out float[] best, out float positionError, out float angleError, float allowedAngle = 3f, Func<float[], bool> acceptable = null)
    {
        best = (float[])seed.Clone(); float bestCost = float.PositiveInfinity;
        const float weight = .25f, epsilon = .25f;
        for (int attempt = 0; attempt < 9; attempt++)
        {
            var q = (float[])seed.Clone();
            if (attempt > 0) for (int j = 0; j < 6; j++) q[j] = Mathf.Clamp(q[j] + Mathf.Sin(attempt * 5.37f + j * 2.41f) * (attempt < 4 ? 35 : 95), lower[j], upper[j]);
            for (int iteration = 0; iteration < 260; iteration++)
            {
                Forward(q, out var p, out var r);
                Vector3 dp = target - p, dr = RotationError(targetRotation, r);
                float cost = dp.sqrMagnitude + weight * weight * dr.sqrMagnitude;
                if (cost < bestCost && (acceptable == null || acceptable(q))) { bestCost = cost; Array.Copy(q, best, 6); }
                if (dp.magnitude < .003f && dr.magnitude < 2 * Mathf.Deg2Rad) break;
                var jacobian = new float[6, 6];
                for (int j = 0; j < 6; j++)
                {
                    q[j] += epsilon; Forward(q, out var pp, out var rr); q[j] -= epsilon;
                    var jp = (pp - p) / (epsilon * Mathf.Deg2Rad);
                    var jr = RotationError(rr, r) * weight / (epsilon * Mathf.Deg2Rad);
                    jacobian[0, j] = jp.x; jacobian[1, j] = jp.y; jacobian[2, j] = jp.z;
                    jacobian[3, j] = jr.x; jacobian[4, j] = jr.y; jacobian[5, j] = jr.z;
                }
                float[] error = { dp.x, dp.y, dp.z, dr.x * weight, dr.y * weight, dr.z * weight };
                var normal = new float[6, 7];
                for (int row = 0; row < 6; row++)
                {
                    for (int col = 0; col < 6; col++)
                        for (int k = 0; k < 6; k++) normal[row, col] += jacobian[k, row] * jacobian[k, col];
                    normal[row, row] += .000025f;
                    for (int k = 0; k < 6; k++) normal[row, 6] += jacobian[k, row] * error[k];
                }
                if (!Eliminate(normal)) break;
                float largest = 0; for (int j = 0; j < 6; j++) largest = Mathf.Max(largest, Mathf.Abs(normal[j, 6] * Mathf.Rad2Deg));
                float stepScale = largest > 7 ? 7 / largest : 1;
                for (int j = 0; j < 6; j++) q[j] = Mathf.Clamp(q[j] + normal[j, 6] * Mathf.Rad2Deg * stepScale, lower[j], upper[j]);
            }
            Forward(best, out var bp, out var br);
            if (Vector3.Distance(bp, target) < .005f && Quaternion.Angle(br, targetRotation) < 3) break;
        }
        Forward(best, out var finalP, out var finalR);
        positionError = Vector3.Distance(finalP, target); angleError = Quaternion.Angle(finalR, targetRotation);
        return positionError < .005f && angleError < allowedAngle;
    }
    static bool Eliminate(float[,] a)
    {
        for (int k = 0; k < 6; k++)
        {
            int pivot = k; for (int i = k + 1; i < 6; i++) if (Mathf.Abs(a[i, k]) > Mathf.Abs(a[pivot, k])) pivot = i;
            if (Mathf.Abs(a[pivot, k]) < 1e-10f) return false;
            for (int j = k; j < 7; j++) { float v = a[k, j]; a[k, j] = a[pivot, j]; a[pivot, j] = v; }
            float divisor = a[k, k]; for (int j = k; j < 7; j++) a[k, j] /= divisor;
            for (int i = 0; i < 6; i++) if (i != k)
            { float factor = a[i, k]; for (int j = k; j < 7; j++) a[i, j] -= factor * a[k, j]; }
        }
        return true;
    }
}
