using System;
using UnityEditor;
using UnityEngine;

/// <summary>실제 URP 렌더링으로 송장 TextMesh의 앞/뒤 가림을 확인한다.</summary>
public static class LabelTextOcclusionCheck
{
    [MenuItem("Tools/Factory ML/송장 글자 가림 검증")]
    static void Check()
    {
        if (Application.isPlaying) { Debug.LogWarning("글자 가림 검증은 Play를 정지한 뒤 실행하세요."); return; }
        GameObject root = null;
        Material bodyMaterial = null;
        RenderTexture rt = null;
        Texture2D pixels = null;
        var previous = RenderTexture.active;
        try
        {
            root = new GameObject("LabelTextOcclusionCheck") { hideFlags = HideFlags.HideAndDontSave };
            root.transform.position = new Vector3(10000, 10000, 10000);
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.transform.SetParent(root.transform, false);
            box.transform.localScale = new Vector3(0.4f, 0.4f, 0.2f);
            bodyMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            bodyMaterial.SetColor("_BaseColor", new Color(0.65f, 0.4f, 0.2f));
            box.GetComponent<Renderer>().sharedMaterial = bodyMaterial;
            var cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(root.transform, false);
            cameraObject.transform.localPosition = new Vector3(0, 0, -1);
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 0.3f;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 2;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            rt = new RenderTexture(128, 128, 24);
            rt.Create();
            camera.targetTexture = rt;
            pixels = new Texture2D(128, 128, TextureFormat.RGB24, false);
            Func<Color32[]> capture = () =>
            {
                camera.Render();
                RenderTexture.active = rt;
                pixels.ReadPixels(new Rect(0, 0, 128, 128), 0, 0);
                pixels.Apply();
                return pixels.GetPixels32();
            };
            Color32[] baseline = capture();
            var textObject = new GameObject("Text");
            textObject.transform.SetParent(root.transform, false);
            var text = textObject.AddComponent<TextMesh>();
            text.font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Project/Parcels/Fonts/NotoSansKR-Regular.otf");
            if (text.font == null) throw new InvalidOperationException("송장 글꼴이 없습니다.");
            text.fontSize = 80;
            text.characterSize = 0.1f;
            text.anchor = TextAnchor.MiddleCenter;
            text.color = Color.black;
            text.text = "88";
            text.font.RequestCharactersInTexture(text.text, text.fontSize, text.fontStyle);
            LabelTextOcclusion.Prepare(textObject);
            var renderer = textObject.GetComponent<MeshRenderer>();
            var material = renderer.sharedMaterial;
            for (int i = 0; i < 100; i++) LabelTextOcclusion.Prepare(textObject);
            bool reused = renderer.sharedMaterial == material && textObject.GetComponents<LabelTextOcclusion>().Length == 1;
            if (!reused) throw new InvalidOperationException("Prepare 반복 호출 시 재질/컴포넌트가 중복 생성됩니다.");
            textObject.transform.localPosition = new Vector3(0, 0, 0.11f);
            int behind = Difference(baseline, capture());
            textObject.transform.localPosition = new Vector3(0, 0, -0.11f);
            int front = Difference(baseline, capture());
            if (behind > 4 || front < 20)
                throw new InvalidOperationException($"가림 검증 실패: 뒤쪽 변화 {behind}px, 앞쪽 변화 {front}px");
            UnityEngine.Object.DestroyImmediate(textObject.GetComponent<LabelTextOcclusion>());
            bool disposed = material == null;
            if (!disposed) throw new InvalidOperationException("글자 가림 컴포넌트 제거 후 임시 재질이 남았습니다.");
            string directory = System.IO.Path.Combine(Application.dataPath, "..", "results", "text_occlusion");
            System.IO.Directory.CreateDirectory(directory);
            System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "latest.json"),
                $"{{\"behindChangedPixels\":{behind},\"frontChangedPixels\":{front},\"prepareCalls\":100,\"materialReused\":true,\"materialDisposed\":true,\"passed\":true}}");
            Debug.Log($"[LabelTextOcclusionCheck] PASS: 뒤쪽 글자 {behind}px (가려짐), 앞쪽 글자 {front}px (표시됨), Prepare 100회 재사용, 임시 재질 해제 통과");
        }
        finally
        {
            RenderTexture.active = previous;
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            if (bodyMaterial != null) UnityEngine.Object.DestroyImmediate(bodyMaterial);
            if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
        }
    }

    static int Difference(Color32[] baseline, Color32[] sample)
    {
        int count = 0;
        for (int i = 0; i < baseline.Length; i++)
            if (Math.Abs(baseline[i].r - sample[i].r) + Math.Abs(baseline[i].g - sample[i].g) + Math.Abs(baseline[i].b - sample[i].b) > 60) count++;
        return count;
    }
}
