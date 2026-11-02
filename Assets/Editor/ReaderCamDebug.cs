using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Bonguri.ParcelTools
{
    /// <summary>
    /// 판독 카메라가 왜 빈 그림을 찍는지 알아보는 일회용 진단.
    /// 상자를 띄우고 카메라를 두 번 찍는다. 한 번은 전부 보이게 넓게, 한 번은 실제 설정으로.
    /// 결과는 Temp/ 에 PNG 로 남기고 숫자는 콘솔에 찍는다.
    /// </summary>
    public static class ReaderCamDebug
    {
        [MenuItem("Tools/Bonguri/Debug Reader Camera")]
        public static void Run()
        {
            var layout = AssetDatabase.FindAssets("t:LabelLayout")
                .Select(g => AssetDatabase.LoadAssetAtPath<LabelLayout>(AssetDatabase.GUIDToAssetPath(g)))
                .FirstOrDefault();
            if (layout == null) { Debug.LogError("[진단] LabelLayout 에셋이 없다."); return; }

            string prefabPath = AssetDatabase.FindAssets("t:Prefab")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => p.Contains("_Process.prefab"));
            if (prefabPath == null) { Debug.LogError("[진단] *_Process.prefab 을 못 찾았다."); return; }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            GameObject box = null;
            Camera cam = null;

            try
            {
                box = Object.Instantiate(prefab);
                box.name = "DebugBox (temp)";

                var info = box.GetComponent<BoxInfo>();
                if (info == null || info.label == null)
                { Debug.LogError("[진단] BoxInfo · label 이 없다: " + prefabPath); return; }

                layout.Print(info, "22137", "대전");

                int layer = LayerMask.NameToLayer("ScanTarget");
                Debug.Log($"[진단] prefab = {prefabPath}\n"
                        + $"  ScanTarget 레이어 번호 = {layer}  (-1 이면 레이어가 없다는 뜻)");

                // 레이어를 바꾸기 전에 renderer 를 훑는다.
                var rends = box.GetComponentsInChildren<Renderer>(true);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"[진단] renderer {rends.Length} 개");
                Bounds all = default; bool first = true;
                foreach (var r in rends.Take(14))
                {
                    sb.AppendLine($"   {r.gameObject.name,-26} layer {r.gameObject.layer,2}  "
                                + $"enabled {r.enabled,-5} active {r.gameObject.activeInHierarchy,-5} "
                                + $"bounds c{r.bounds.center} s{r.bounds.size}");
                }
                foreach (var r in rends)
                {
                    if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                    if (first) { all = r.bounds; first = false; } else all.Encapsulate(r.bounds);
                }
                sb.AppendLine($"   켜진 renderer 전체 bounds: center {all.center} size {all.size}");
                Debug.Log(sb.ToString());

                var label = info.label;
                Debug.Log($"[진단] label '{label.name}'\n"
                        + $"  world pos {label.position}\n"
                        + $"  up {label.up}  right {label.right}\n"
                        + $"  lossyScale {label.lossyScale}");

                var tmps = box.GetComponentsInChildren<TextMeshPro>(true);
                foreach (var t in tmps)
                    Debug.Log($"[진단] TMP '{t.name}' text='{t.text}' fontSize={t.fontSize:F5} "
                            + $"enabled={t.enabled} active={t.gameObject.activeInHierarchy} "
                            + $"layer={t.gameObject.layer} bounds={t.bounds.size} "
                            + $"rendererBounds={(t.GetComponent<Renderer>() != null ? t.GetComponent<Renderer>().bounds.size.ToString() : "없음")}");

                var camGo = new GameObject("DebugCam (temp)");
                cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(.5f, .5f, .5f);
                cam.allowHDR = false; cam.allowMSAA = false;

                // 1) 모든 레이어를 보고 송장 전체가 들어오게 넓게 찍는다.
                cam.cullingMask = ~0;
                cam.orthographic = true;
                cam.transform.position = label.position + label.up * .05f;
                cam.transform.rotation = Quaternion.LookRotation(-label.up, label.right);
                cam.orthographicSize = .03f;
                cam.ResetAspect();
                cam.nearClipPlane = .001f;
                cam.farClipPlane = .2f;
                Save(cam, 256, 256, "Temp/readercam_wide.png");

                // 2) 실제 설정 그대로.
                cam.cullingMask = layer >= 0 ? 1 << layer : ~0;
                layout.AimAtDigit(cam, label, 0, false);
                Debug.Log($"[진단] 실제 조준 (숫자 칸 0)\n"
                        + $"  cam pos {cam.transform.position}  forward {cam.transform.forward}\n"
                        + $"  orthoSize {cam.orthographicSize:F5}  near {cam.nearClipPlane:F4} far {cam.farClipPlane:F4}\n"
                        + $"  cullingMask {cam.cullingMask}  (ScanTarget 만이면 {(layer >= 0 ? 1 << layer : -1)})\n"
                        + $"  label 까지 거리 {Vector3.Distance(cam.transform.position, label.position) * 1000:F2} mm");
                Save(cam, 32, 32, "Temp/readercam_cell.png");

                // 3) 조준은 그대로 두고 레이어 제한만 풀어 본다.
                cam.cullingMask = ~0;
                Save(cam, 32, 32, "Temp/readercam_cell_alllayers.png");

                // 4) 조준은 그대로, 시야만 10 배 넓혀 본다.
                cam.orthographicSize *= 10f;
                cam.farClipPlane = .5f;
                Save(cam, 256, 256, "Temp/readercam_cell_wide.png");

                Debug.Log("[진단] Temp/readercam_*.png 네 장을 남겼다.");
            }
            finally
            {
                if (cam != null) Object.DestroyImmediate(cam.gameObject);
                if (box != null) Object.DestroyImmediate(box);
            }
        }

        static void Save(Camera cam, int w, int h, string path)
        {
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            var prevActive = RenderTexture.active;
            cam.aspect = (float)w / h;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;

            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());

            var px = tex.GetPixels32();
            int lo = 255, hi = 0;
            foreach (var p in px) { if (p.r < lo) lo = p.r; if (p.r > hi) hi = p.r; }
            Debug.Log($"[진단] {path}  {w}x{h}  밝기 {lo} ~ {hi}  {(lo == hi ? "← 단색(아무것도 안 보임)" : "")}");
            Object.DestroyImmediate(tex);
        }
    }
}
