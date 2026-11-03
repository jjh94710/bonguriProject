using System;
using UnityEngine;

/// <summary>Polyline on explicitly assigned belt colliders. Coordinates describe the belt surface, not a parcel pivot.</summary>
public sealed class ConveyorSurfacePath
{
    readonly Vector3[] points;
    readonly float[] distances;
    readonly Collider[] surfaces;
    readonly float clearance, probeHeight, maxSlope;
    public float Length => distances[distances.Length - 1];

    public ConveyorSurfacePath(Vector3[] points, Collider[] surfaces, float clearance, float probeHeight, float maxSlope)
    {
        this.points = points; this.surfaces = surfaces; this.clearance = clearance;
        this.probeHeight = probeHeight; this.maxSlope = maxSlope;
        distances = new float[points.Length];
        for (int i = 1; i < points.Length; i++) distances[i] = distances[i - 1] + Vector3.Distance(points[i - 1], points[i]);
    }
    public void Sample(float distance, out Vector3 position, out Vector3 tangent)
    {
        distance = Mathf.Clamp(distance, 0, Length);
        int i = 1;
        while (i < points.Length - 1 && distances[i] < distance) i++;
        tangent = (points[i] - points[i - 1]).normalized;
        float length = distances[i] - distances[i - 1];
        position = Vector3.Lerp(points[i - 1], points[i], length > .00001f ? (distance - distances[i - 1]) / length : 0);
    }
    public float Project(Vector3 position)
    {
        float best = float.PositiveInfinity, result = 0;
        for (int i = 1; i < points.Length; i++)
        {
            Vector3 delta = points[i] - points[i - 1];
            float t = Mathf.Clamp01(Vector3.Dot(position - points[i - 1], delta) / Mathf.Max(delta.sqrMagnitude, .000001f));
            float error = (position - points[i - 1] - delta * t).sqrMagnitude;
            if (error < best) { best = error; result = Mathf.Lerp(distances[i - 1], distances[i], t); }
        }
        return result;
    }
    public bool TrySurface(Vector3 expected, out Vector3 point, out Vector3 normal)
    {
        point = default; normal = Vector3.up;
        float best = float.PositiveInfinity;
        // Roller meshes have narrow slots. Probe at most 25 mm around the centre;
        // larger missing sections still fail validation rather than becoming invisible bridges.
        for (int probe = 0; probe < 9; probe++)
        {
        if (probe > 0 && !float.IsPositiveInfinity(best)) break;
        Vector3 offset = probe == 0 ? Vector3.zero : new Vector3(Mathf.Cos((probe-1)*Mathf.PI/4),0,Mathf.Sin((probe-1)*Mathf.PI/4))*.025f;
        var ray = new Ray(expected + offset + Vector3.up * probeHeight, Vector3.down);
        foreach (var collider in surfaces)
        {
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger) continue;
            if (!collider.Raycast(ray, out RaycastHit hit, probeHeight * 2)) continue;
            if (hit.normal.y < Mathf.Cos(maxSlope * Mathf.Deg2Rad)) continue;
            float error = Mathf.Abs(hit.point.y - expected.y);
            if (error < best) { best = error; normal = hit.normal; point = new Vector3(expected.x,hit.point.y+(hit.normal.x*offset.x+hit.normal.z*offset.z)/hit.normal.y,expected.z); }
        }
        }
        return !float.IsPositiveInfinity(best);
    }
    public bool Validate(out string error)
    {
        error = null;
        if (points.Length < 2 || Length < .01f || surfaces == null || surfaces.Length == 0)
        { error = "표면 경유점과 벨트 Collider를 연결하세요."; return false; }
        for (int i = 1; i < points.Length; i++)
        {
            Vector3 delta = points[i] - points[i - 1];
            if (delta.magnitude < .001f || Vector3.Angle(delta, Vector3.ProjectOnPlane(delta, Vector3.up)) > maxSlope)
            { error = "중복 경유점 또는 허용 경사를 넘는 수직 단차가 있습니다: " + i; return false; }
        }
        Vector3 previous = default;
        int count = Mathf.CeilToInt(Length / .02f);
        for (int i = 0; i <= count; i++)
        {
            Sample(Length * i / count, out var expected, out _);
            if (!TrySurface(expected, out var surface, out _))
            { error = $"벨트 표면이 끊겼거나 경유점 높이가 다릅니다 ({Length * i / count:F2}m, {expected.ToString("F4")})."; return false; }
            if (i > 0 && Mathf.Abs(surface.y - previous.y) > Vector3.Distance(surface, previous) * Mathf.Sin(maxSlope * Mathf.Deg2Rad) + .008f)
            { error = $"연결 경사 없는 높이 단차가 있습니다 ({Length * i / count:F2}m)."; return false; }
            previous = surface;
        }
        return true;
    }
    public bool TryPose(BoxInfo box, Quaternion levelRotation, float distance, float lateral, out Vector3 position, out Quaternion rotation, out string error)
    {
        position = default; rotation = levelRotation; error = null;
        var shape = box.GetComponent<BoxCollider>();
        if (shape == null) { error = "표면 이송에는 상자 루트의 BoxCollider가 필요합니다."; return false; }
        Sample(distance, out var expected, out var tangent);
        if (!TrySurface(expected, out var surface, out var normal)) { error = "이송 위치에 벨트 표면이 없습니다."; return false; }
        Vector3 across = Vector3.Cross(normal, tangent).normalized;
        expected = surface + across * lateral;
        if (!TrySurface(expected, out surface, out normal)) { error = "상자가 벨트 표면 밖입니다."; return false; }
        rotation = Quaternion.FromToRotation(Vector3.up, normal) * levelRotation;
        Vector3 scale = box.transform.lossyScale;
        Vector3 half = Vector3.Scale(shape.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z))) * .5f;
        float support = Mathf.Abs(Vector3.Dot(rotation * Vector3.right, normal)) * half.x
                      + Mathf.Abs(Vector3.Dot(rotation * Vector3.up, normal)) * half.y
                      + Mathf.Abs(Vector3.Dot(rotation * Vector3.forward, normal)) * half.z;
        position = surface + normal * (support + clearance) - rotation * Vector3.Scale(shape.center, scale);
        // At a ramp join the footprint spans both surfaces. Lift enough for every box corner.
        float lift = 0;
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            Vector3 corner = position + rotation * Vector3.Scale(shape.center + Vector3.Scale(shape.size * .5f, new Vector3(x, y, z)), scale);
            var probe = new Vector3(corner.x, surface.y, corner.z);
            if (TrySurface(probe, out var under, out _)) lift = Mathf.Max(lift, under.y + clearance - corner.y);
        }
        position += Vector3.up * lift;
        // Corner height probes alone cannot detect contact with a side rail or a convex ramp crest.
        float railLift = 0;
        for (int pass = 0; pass < 24; pass++)
        {
            bool moved = false;
            foreach (var belt in surfaces)
            {
                if (belt == null || !belt.enabled || !belt.gameObject.activeInHierarchy || belt.isTrigger) continue;
                if (!Physics.ComputePenetration(shape, position, rotation, belt, belt.transform.position, belt.transform.rotation, out var direction, out float depth) || depth < .0001f) continue;
                if (direction.y < .3f)
                {
                    // A roller edge may report a horizontal separation normal although
                    // the parcel can rest on its top. Resolve small edge contacts upward.
                    if(railLift>=.03f){error=$"상자와 벨트 측면/프레임이 충돌합니다 ({distance:F3}m). 경로 폭과 코너 경유점을 확인하세요.";return false;}
                    position+=Vector3.up*.002f;railLift+=.002f;moved=true;break;
                }
                position += Vector3.up * ((depth + clearance) / direction.y);
                moved = true;
            }
            if (!moved) return true;
        }
        error = "경사 연결부에서 상자와 벨트의 겹침을 해소하지 못했습니다.";
        return false;
    }
}

