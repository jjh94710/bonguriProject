using UnityEngine;

/// <summary>TextMesh 글자에 깊이 검사를 적용해 반대쪽 송장이 상자를 뚫고 보이지 않게 한다.</summary>
[DisallowMultipleComponent]
[ExecuteAlways]
[RequireComponent(typeof(TextMesh), typeof(MeshRenderer))]
public class LabelTextOcclusion : MonoBehaviour
{
    TextMesh text;
    MeshRenderer mesh;
    Material original;
    Material depthMaterial;

    public static void Prepare(GameObject label)
    {
        if (label == null) return;
        foreach (var item in label.GetComponentsInChildren<TextMesh>(true))
        {
            var occlusion = item.GetComponent<LabelTextOcclusion>();
            if (occlusion == null) occlusion = item.gameObject.AddComponent<LabelTextOcclusion>();
            occlusion.Configure();
        }
    }

    void OnEnable() => Configure();

    void Configure()
    {
        if (depthMaterial != null) return;
        text = GetComponent<TextMesh>();
        mesh = GetComponent<MeshRenderer>();
        var shader = Resources.Load<Shader>("DepthTestedLabelText");
        if (shader == null || !shader.isSupported)
        {
            Debug.LogError("[LabelTextOcclusion] 깊이 검사 글자 셰이더를 사용할 수 없습니다.", this);
            return;
        }
        original = mesh.sharedMaterial;
        depthMaterial = new Material(shader)
        {
            name = "Label Text (Depth Tested)",
            hideFlags = HideFlags.HideAndDontSave
        };
        // TextMesh가 생성한 정점 색상(검정 글씨)을 그대로 사용한다.
        depthMaterial.color = Color.white;
        SyncFontTexture();
        mesh.sharedMaterial = depthMaterial;
    }

    void SyncFontTexture()
    {
        // 동적 글꼴 아틀라스가 갱신돼도 새 텍스처를 사용한다. 원본 글꼴 재질은 수정하지 않는다.
        if (depthMaterial != null && text != null && text.font != null && text.font.material != null)
            depthMaterial.mainTexture = text.font.material.mainTexture;
    }

    void OnWillRenderObject() => SyncFontTexture();

    void OnDestroy()
    {
        if (mesh != null && mesh.sharedMaterial == depthMaterial) mesh.sharedMaterial = original;
        if (depthMaterial == null) return;
        if (Application.isPlaying) Destroy(depthMaterial); else DestroyImmediate(depthMaterial);
    }
}
