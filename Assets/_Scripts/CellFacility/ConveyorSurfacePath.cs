using System;
using UnityEngine;

//  ============================================================
//  ConveyorSurfacePath
//  벨트 표면 경로 계산 (ConveyorLine 안에서 쓰는 도우미, 컴포넌트가 아니다).
//  경유점을 이은 꺾은선을 따라, 지정한 벨트 Collider의 실제 윗면 위로 상자를 놓을 위치 · 자세를 계산한다.
//      - 경로 위 거리 ↔ 위치 · 진행 방향을 바꾼다 (Sample(), Project()).
//      - 아래로 광선을 쏘아 벨트 윗면 높이 · 법선을 찾는다 (TrySurface()). 롤러 사이 틈은 주변 25 mm까지 찾아본다.
//      - 상자 바닥을 표면에 맞추고 경사만큼 눕힌다. 경사 연결부 · 롤러 모서리 · 옆 레일 겹침은 위로 올려 푼다 (TryPose()).
//      - 경로 전체를 2 cm마다 확인해 표면이 끊기거나 경사 없는 단차가 있으면 실패로 알린다 (Validate()).
//
//  [사용] ConveyorLine에서 Use Surface Path를 켜면 Entry → Surface Waypoints → Exit와 Belt Surfaces로 만든다 (ValidateSurfacePath()).
//         좌표는 벨트 윗면 위의 점이다 (상자 기준점이 아니다).
//  [!] Belt Surfaces에는 상자가 실제로 올라가는 벨트 Collider만 넣는다. 바닥 · 로봇 · 상자를 넣으면 그 위로 경로가 생긴다.
//  [!] 허용 경사(maxSlope)를 넘는 면, Trigger, 꺼진 Collider는 표면으로 보지 않는다.
//  [!] 큰 표면 누락은 다리처럼 메우지 않고 검증 실패로 남긴다. 벨트를 옮기면 경유점과 Belt Surfaces도 맞춘다.
//
//  [설계서와의 차이]
//      * 설계서에 없는 클래스다. 터미널 A · B · C 벨트(코너 · 경사로)를 따라 상자를 옮기려고 지훈님이 더했다 (11/03 통합).
//      * 설계서의 ConveyorLine은 입구 → 출구 직선만 다룬다. 표면 경로를 켜지 않은 구간은 지금도 직선 방식이다.
//  ============================================================
public sealed class ConveyorSurfacePath
{
    //  ============================================================
    //  안쪽 값 (만들 때 정하고 바꾸지 않는다)
    //  ============================================================
    readonly Vector3[] points;                              //  경유점 (벨트 윗면 위, Entry → Exit 순서)
    readonly float[] distances;                             //  0번 점부터 각 점까지 경로 거리 m (누적)
    readonly Collider[] surfaces;                           //  상자가 올라가는 벨트 Collider
    readonly float clearance;                               //  상자 바닥과 표면 사이 여유 m (ConveyorLine 기본 2 mm)
    readonly float probeHeight;                             //  표면 찾기 광선을 쏘는 높이 m (예상 점 위로. 광선 길이는 2배)
    readonly float maxSlope;                                //  표면으로 볼 최대 경사 ° (ConveyorLine 기본 35°)


    //  ============================================================
    //  상태
    //  ============================================================
    /// 경로 전체 길이 m
    public float Length => distances[distances.Length - 1];


    //  ============================================================
    //  만들기
    //  ============================================================
    // 경유점 · 벨트 Collider · 여유 · 광선 높이 · 최대 경사로 경로를 만들고, 점마다 누적 거리를 계산한다.
    /// [호출] ConveyorLine.ValidateSurfacePath(), 에디터 도구 BonguriConveyorSurfaceSetup
    /// [!] 검증은 하지 않는다. 만든 뒤 Validate()를 부른다.
    public ConveyorSurfacePath(Vector3[] points, Collider[] surfaces, float clearance, float probeHeight, float maxSlope)
    {
        this.points = points;
        this.surfaces = surfaces;
        this.clearance = clearance;
        this.probeHeight = probeHeight;
        this.maxSlope = maxSlope;
        distances = new float[points.Length];
        for (int i = 1; i < points.Length; i++)
            { distances[i] = distances[i - 1] + Vector3.Distance(points[i - 1], points[i]); }
    }


    //  ============================================================
    //  경로 위 위치
    //  ============================================================
    // 경로 거리 distance m 지점의 위치와 진행 방향(그 구간의 단위 벡터)을 돌려준다. distance는 0 ~ Length로 자른다.
    /// [호출] ConveyorLine (진행 방향 FlowAt(), 표면 법선 찾기), Validate(), TryPose()
    public void Sample(float distance, out Vector3 position, out Vector3 tangent)
    {
        distance = Mathf.Clamp(distance, 0, Length);

        //  distance가 들어 있는 구간 (i - 1 → i)을 찾는다
        int i = 1;
        while (i < points.Length - 1 && distances[i] < distance) { i++; }

        //  그 구간 안에서 비율만큼 옮긴다 (길이가 거의 0인 구간은 시작점)
        tangent = (points[i] - points[i - 1]).normalized;
        float length = distances[i] - distances[i - 1];
        position = Vector3.Lerp(points[i - 1], points[i], length > .00001f ? (distance - distances[i - 1]) / length : 0);
    }

    // 월드 위치를 경로에 내려 가장 가까운 점의 경로 거리 m를 돌려준다 (Sample()의 반대).
    /// [호출] ConveyorLine.AlongBelt() (표면 경로 모드에서 상자 · 정지점 위치 → 거리)
    public float Project(Vector3 position)
    {
        float best = float.PositiveInfinity;
        float result = 0;
        for (int i = 1; i < points.Length; i++)
        {
            //  구간 위로 내린 점 (0 ~ 1로 잘라 구간 밖으로 나가지 않게)
            Vector3 delta = points[i] - points[i - 1];
            float t = Mathf.Clamp01(Vector3.Dot(position - points[i - 1], delta) / Mathf.Max(delta.sqrMagnitude, .000001f));
            float error = (position - points[i - 1] - delta * t).sqrMagnitude;
            if (error < best)
            {
                best = error;
                result = Mathf.Lerp(distances[i - 1], distances[i], t);
            }
        }
        return result;
    }


    //  ============================================================
    //  표면 찾기
    //  ============================================================
    // 예상 점(expected) 아래의 벨트 윗면을 찾는다. 찾으면 true와 표면 점 · 법선.
    /// 예상 점 위 probeHeight에서 아래로 광선을 쏜다. 못 찾으면 주변 25 mm 원 위 8방향(45°마다)에서 차례로 다시 쏜다 (롤러 사이 틈).
    /// 여러 Collider에 맞으면 높이가 expected에 가장 가까운 것을 쓴다. 경사가 maxSlope를 넘는 면은 건너뛴다.
    /// 돌려주는 점은 expected의 x · z 그대로이고, 높이만 표면 높이다 (옆에서 맞았으면 그 면의 기울기로 expected 자리 높이를 다시 계산한다).
    /// [호출] ConveyorLine (표면 법선), Validate(), TryPose(), 에디터 도구 BonguriConveyorSurfaceSetup
    public bool TrySurface(Vector3 expected, out Vector3 point, out Vector3 normal)
    {
        point = default;
        normal = Vector3.up;
        float best = float.PositiveInfinity;

        //  0번 = 가운데, 1 ~ 8번 = 25 mm 원 위 45°마다. 앞에서 찾았으면 더 쏘지 않는다
        for (int probe = 0; probe < 9; probe++)
        {
            if (probe > 0 && !float.IsPositiveInfinity(best)) { break; }

            Vector3 offset = probe == 0 ? Vector3.zero : new Vector3(Mathf.Cos((probe - 1) * Mathf.PI / 4), 0, Mathf.Sin((probe - 1) * Mathf.PI / 4)) * .025f;
            var ray = new Ray(expected + offset + Vector3.up * probeHeight, Vector3.down);
            foreach (var collider in surfaces)
            {
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger) { continue; }
                if (!collider.Raycast(ray, out RaycastHit hit, probeHeight * 2)) { continue; }
                if (hit.normal.y < Mathf.Cos(maxSlope * Mathf.Deg2Rad)) { continue; }      //  너무 가파른 면 (옆면 · 레일)

                float error = Mathf.Abs(hit.point.y - expected.y);
                if (error < best)
                {
                    best = error;
                    normal = hit.normal;
                    point = new Vector3(expected.x, hit.point.y + (hit.normal.x * offset.x + hit.normal.z * offset.z) / hit.normal.y, expected.z);
                }
            }
        }
        return !float.IsPositiveInfinity(best);
    }


    //  ============================================================
    //  검증
    //  ============================================================
    // 경로를 쓸 수 있는지 확인한다. 문제가 있으면 false와 이유.
    /// 1) 경유점 2개 이상, 길이 1 cm 이상, 벨트 Collider 1개 이상
    /// 2) 구간마다: 겹친 점(1 mm 미만)이 없고, 구간 경사가 maxSlope 이하
    /// 3) 2 cm마다 표면이 있고, 옆 점과 높이 차이가 경사 허용치 + 8 mm 이하 (경사로 없이 높이만 다른 벨트 사이를 지나가지 않게)
    /// [호출] ConveyorLine.ValidateSurfacePath() (결과를 기억해 둔다), 에디터 도구 BonguriConveyorSurfaceSetup
    public bool Validate(out string error)
    {
        error = null;

        //  1) 기본 연결
        if (points.Length < 2 || Length < .01f || surfaces == null || surfaces.Length == 0)
        {
            error = "표면 경유점과 벨트 Collider를 연결하세요.";
            return false;
        }

        //  2) 구간마다 겹친 점 · 경사
        for (int i = 1; i < points.Length; i++)
        {
            Vector3 delta = points[i] - points[i - 1];
            if (delta.magnitude < .001f || Vector3.Angle(delta, Vector3.ProjectOnPlane(delta, Vector3.up)) > maxSlope)
            {
                error = "중복 경유점 또는 허용 경사를 넘는 수직 단차가 있습니다: " + i;
                return false;
            }
        }

        //  3) 2 cm마다 표면 · 단차
        Vector3 previous = default;
        int count = Mathf.CeilToInt(Length / .02f);
        for (int i = 0; i <= count; i++)
        {
            Sample(Length * i / count, out var expected, out _);
            if (!TrySurface(expected, out var surface, out _))
            {
                error = $"벨트 표면이 끊겼거나 경유점 높이가 다릅니다 ({Length * i / count:F2}m, {expected.ToString("F4")}).";
                return false;
            }
            if (i > 0 && Mathf.Abs(surface.y - previous.y) > Vector3.Distance(surface, previous) * Mathf.Sin(maxSlope * Mathf.Deg2Rad) + .008f)
            {
                error = $"연결 경사 없는 높이 단차가 있습니다 ({Length * i / count:F2}m).";
                return false;
            }
            previous = surface;
        }
        return true;
    }


    //  ============================================================
    //  상자 자세
    //  ============================================================
    // 경로 거리 distance m, 옆으로 lateral m 지점에 상자를 놓을 위치 · 회전을 계산한다. 표면 밖이거나 겹침을 못 풀면 false와 이유.
    /// levelRotation = 평평한 벨트 위에서의 상자 회전. 표면 법선만큼 기울여 쓴다.
    /// [호출] ConveyorLine (상자 받기 · 이동 · 정지점 확인), ConveyorLine.TryGetSurfacePose(), 에디터 도구 BonguriConveyorSurfaceSetup (경로 전체 2 cm마다 시험)
    /// [!] 상자 루트에 BoxCollider가 있어야 한다. 크기 · 스케일 · 중심 오프셋을 모두 반영한다.
    public bool TryPose(BoxInfo box, Quaternion levelRotation, float distance, float lateral, out Vector3 position, out Quaternion rotation, out string error)
    {
        position = default;
        rotation = levelRotation;
        error = null;
        var shape = box.GetComponent<BoxCollider>();
        if (shape == null)
        {
            error = "표면 이송에는 상자 루트의 BoxCollider가 필요합니다.";
            return false;
        }

        //  1) 경로 위 점의 표면 → 옆으로 lateral만큼 옮긴 점의 표면
        Sample(distance, out var expected, out var tangent);
        if (!TrySurface(expected, out var surface, out var normal))
        {
            error = "이송 위치에 벨트 표면이 없습니다.";
            return false;
        }
        Vector3 across = Vector3.Cross(normal, tangent).normalized;
        expected = surface + across * lateral;
        if (!TrySurface(expected, out surface, out normal))
        {
            error = "상자가 벨트 표면 밖입니다.";
            return false;
        }

        //  2) 표면 법선만큼 기울이고, 상자 바닥이 표면 + 여유에 닿게 놓는다
        //      support = 법선 방향으로 본 상자 두께의 절반 (반 크기를 법선에 투영해 더한 값)
        rotation = Quaternion.FromToRotation(Vector3.up, normal) * levelRotation;
        Vector3 scale = box.transform.lossyScale;
        Vector3 half = Vector3.Scale(shape.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z))) * .5f;
        float support = Mathf.Abs(Vector3.Dot(rotation * Vector3.right, normal)) * half.x
                      + Mathf.Abs(Vector3.Dot(rotation * Vector3.up, normal)) * half.y
                      + Mathf.Abs(Vector3.Dot(rotation * Vector3.forward, normal)) * half.z;
        position = surface + normal * (support + clearance) - rotation * Vector3.Scale(shape.center, scale);

        //  3) 경사 연결부: 상자 밑이 두 면에 걸치므로, 모서리 8개 아래 표면을 보고 가장 많이 파고든 만큼 올린다
        float lift = 0;
        for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = position + rotation * Vector3.Scale(shape.center + Vector3.Scale(shape.size * .5f, new Vector3(x, y, z)), scale);
                    var probe = new Vector3(corner.x, surface.y, corner.z);
                    if (TrySurface(probe, out var under, out _)) { lift = Mathf.Max(lift, under.y + clearance - corner.y); }
                }
        position += Vector3.up * lift;

        //  4) 모서리 높이만으로는 옆 레일이나 볼록한 경사 꼭대기와의 겹침을 못 찾는다. 물리 겹침(ComputePenetration)으로 풀어 본다 (최대 24번)
        float railLift = 0;
        for (int pass = 0; pass < 24; pass++)
        {
            bool moved = false;
            foreach (var belt in surfaces)
            {
                if (belt == null || !belt.enabled || !belt.gameObject.activeInHierarchy || belt.isTrigger) { continue; }
                if (!Physics.ComputePenetration(shape, position, rotation, belt, belt.transform.position, belt.transform.rotation, out var direction, out float depth) || depth < .0001f) 
                    { continue; }
                if (direction.y < .3f)
                {
                    //  거의 옆으로 밀어내는 겹침: 롤러 모서리는 윗면에 올라앉을 수 있어도 옆 법선을 줄 수 있다. 2 mm씩, 30 mm까지만 위로 올린다
                    if (railLift >= .03f)
                    {
                        error = $"상자와 벨트 측면/프레임이 충돌합니다 ({distance:F3}m). 경로 폭과 코너 경유점을 확인하세요.";
                        return false;
                    }
                    position += Vector3.up * .002f;
                    railLift += .002f;
                    moved = true;
                    break;
                }

                //  위로 밀어내는 겹침: 그 깊이 + 여유만큼 (밀어내는 방향의 위쪽 성분으로 나눠) 올린다
                position += Vector3.up * ((depth + clearance) / direction.y);
                moved = true;
            }
            if (!moved) { return true; }
        }
        error = "경사 연결부에서 상자와 벨트의 겹침을 해소하지 못했습니다.";
        return false;
    }
}
