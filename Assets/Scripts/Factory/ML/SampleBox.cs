using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 상자 Prefab 이 아직 없을 때 쓰는 임시 상자.
/// 미리보기(FaceCameraRig)와 학습 이미지 생성(LabelDatasetGenerator)이
/// 같은 상자를 쓰도록 한 곳에 모았다. 실제 Prefab 이 나오면 이 파일은 안 써도 된다.
/// </summary>
public static class SampleBox
{
    public const string DefaultLayerName = "ScanTarget";

    /// <summary>갈색 큐브 + 송장 스티커(+Z 면). temps 에 담긴 임시 에셋은 호출한 쪽이 지운다.</summary>
    public static GameObject Create(Vector3 size, int layer, List<Object> temps)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = "SampleBox (temp)";
        box.hideFlags = HideFlags.DontSave;
        box.transform.localScale = size;
        Kill(box.GetComponent<Collider>());

        var boxMat = MakeMaterial(new Color(0.62f, 0.47f, 0.32f), null);
        temps?.Add(boxMat);
        box.GetComponent<MeshRenderer>().sharedMaterial = boxMat;

        var labelTex = MakeLabelTexture(new Color32(245, 240, 220, 255));
        temps?.Add(labelTex);
        var labelMat = MakeMaterial(Color.white, labelTex);
        temps?.Add(labelMat);

        var label = GameObject.CreatePrimitive(PrimitiveType.Cube);
        label.name = "Label";
        label.hideFlags = HideFlags.DontSave;
        Kill(label.GetComponent<Collider>());
        label.GetComponent<MeshRenderer>().sharedMaterial = labelMat;
        label.transform.SetParent(box.transform, false);

        // 부모가 size 로 스케일돼 있으므로 로컬 스케일은 비율로 넣는다.
        label.transform.localScale = new Vector3(0.62f, 0.62f, 0.012f);
        label.transform.localPosition = new Vector3(0f, 0f, 0.5f);

        SetLayerRecursively(box, layer);
        return box;
    }

    /// <summary>송장 묶음을 찾는다. 이름이 정확히 맞는 자손을 먼저 보고, 없으면
    /// 이름에 포함된 자손 중 가장 얕은 것을 고른다.
    /// 송장은 Renderer 가 없는 빈 묶음일 수 있어(조원 Prefab 의 ShippingLabel)
    /// Renderer 가 아니라 GameObject 를 돌려준다. 끌 때는 SetActive 로 묶음째 끈다.</summary>
    public static GameObject FindLabel(GameObject box, string childName = "Label")
    {
        if (box == null) return null;

        string needle = string.IsNullOrWhiteSpace(childName) ? "label" : childName.ToLowerInvariant();
        GameObject exact = null, loose = null;
        int exactDepth = int.MaxValue, looseDepth = int.MaxValue;

        foreach (var t in box.GetComponentsInChildren<Transform>(true))
        {
            if (t.gameObject == box) continue;

            int depth = 0;
            for (Transform p = t.parent; p != null && p != box.transform; p = p.parent) depth++;

            string n = t.name.ToLowerInvariant();
            if (n == needle)
            {
                if (depth < exactDepth) { exact = t.gameObject; exactDepth = depth; }
            }
            else if (n.Contains(needle) && depth < looseDepth)
            {
                loose = t.gameObject; looseDepth = depth;
            }
        }

        // 얕은 쪽이 묶음이고 깊은 쪽은 그 안의 종이·바코드 막대다.
        // 'Label' 로 찾아도 LabelBar_0 이 아니라 ShippingLabel 이 잡혀야 한다.
        return exact != null ? exact : loose;
    }

    public static void SetLayerRecursively(GameObject go, int layer)
    {
        if (go == null || layer < 0 || layer > 31) return;
        go.layer = layer;
        foreach (Transform child in go.transform) SetLayerRecursively(child.gameObject, layer);
    }

    /// <summary>이름으로 레이어를 찾는다. 없으면 -1.</summary>
    public static int ResolveLayer(string layerName)
    {
        if (string.IsNullOrWhiteSpace(layerName)) return -1;
        return LayerMask.NameToLayer(layerName);
    }

    static Material MakeMaterial(Color color, Texture tex)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Standard");

        var m = new Material(sh) { hideFlags = HideFlags.DontSave };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color); else m.color = color;
        if (tex != null)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            else m.mainTexture = tex;
        }
        return m;
    }

    /// <summary>임시 송장 무늬. 바코드를 왼쪽 위에 둬서 뒤에 Align 기준으로 쓸 수 있게 한다.</summary>
    public static Texture2D MakeLabelTexture(Color32 paper)
    {
        const int W = 64, H = 48;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false) { hideFlags = HideFlags.DontSave };
        var px = new Color32[W * H];
        var ink = new Color32(28, 28, 32, 255);

        for (int i = 0; i < px.Length; i++) px[i] = paper;

        // 바코드 (왼쪽 위)
        for (int x = 4; x < 28; x += 3)
            for (int y = H - 16; y < H - 4; y++)
                for (int k = 0; k < 2 && x + k < W; k++) px[y * W + x + k] = ink;

        // 번호 5 칸 (아래쪽)
        for (int c = 0; c < 5; c++)
        {
            int x0 = 4 + c * 12;
            for (int x = x0; x < x0 + 8 && x < W; x++)
                for (int y = 6; y < 20; y++)
                    if (x == x0 || x == x0 + 7 || y == 6 || y == 19 || y == 13) px[y * W + x] = ink;
        }

        tex.SetPixels32(px);
        tex.Apply();
        tex.filterMode = FilterMode.Point;
        return tex;
    }

    public static void Kill(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Object.Destroy(o);
        else Object.DestroyImmediate(o);
    }
}
