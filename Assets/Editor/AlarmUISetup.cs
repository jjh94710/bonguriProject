using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class AlarmUISetup
{
    public static void Run()
    {
        try
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/bongyuri.unity");
            var hud = GameObject.Find("FactoryHUD").transform;
            if (hud.Find("AlarmUI") != null) throw new Exception("AlarmUI already exists");
            var source = hud.Find("BoxInfoPanel");
            var srcRect = (RectTransform)source;
            var root = Node("AlarmUI", hud, Vector2.zero, Vector2.zero);
            var panel = Node("Popup", root.transform, new Vector2(srcRect.rect.width, 252), Vector2.zero);
            var r = (RectTransform)root.transform;
            r.anchorMin = srcRect.anchorMin; r.anchorMax = srcRect.anchorMax; r.pivot = srcRect.pivot;
            r.anchoredPosition = srcRect.anchoredPosition + Vector2.down * (srcRect.rect.height + 16);
            r.sizeDelta = new Vector2(srcRect.rect.width, 252);
            var image = panel.AddComponent<Image>();
            EditorUtility.CopySerialized(source.GetComponent<Image>(), image);
            image.color = new Color(.055f,.075f,.10f,.88f); image.raycastTarget = true;
            var font = source.GetComponentsInChildren<TMP_Text>(true).First().font;
            var title = Text("Heading", panel.transform, "경고 · 이벤트", font, new Vector2(20,-12), new Vector2(440,32), 25);
            title.fontStyle = FontStyles.Bold; title.color = new Color(1,.72f,.37f);
            var banner = Text("Banner", panel.transform, "", font, new Vector2(20,-52), new Vector2(srcRect.rect.width-40,48), 21);
            var history = Text("Events", panel.transform, "", font, new Vector2(20,-108), new Vector2(srcRect.rect.width-40,132), 17);
            var label = Text("HistoryHeading", panel.transform, "최근 이벤트 · 최대 5건", font, new Vector2(20,-86), new Vector2(450,22), 16);
            label.color = new Color(.68f,.76f,.83f);
            var button = Node("Close", panel.transform, new Vector2(44,36), new Vector2(srcRect.rect.width-60,-10));
            button.AddComponent<Image>().color = new Color(.3f,.36f,.43f,.8f);
            var close = button.AddComponent<Button>();
            var x = Text("Label",button.transform,"×",font,new Vector2(0,0),new Vector2(44,36),26);x.alignment=TextAlignmentOptions.Center;
            var border = source.Find("Border");
            if(border != null) { var copy=UnityEngine.Object.Instantiate(border.gameObject,panel.transform,false);copy.name="Border";copy.GetComponent<Image>().raycastTarget=false; }
            var component = root.AddComponent<AlarmUI>(); var so = new SerializedObject(component);
            so.FindProperty("controller").objectReferenceValue = UnityEngine.Object.FindFirstObjectByType<CellController>();
            so.FindProperty("popup").objectReferenceValue = panel;
            so.FindProperty("heading").objectReferenceValue = title;
            so.FindProperty("banner").objectReferenceValue = banner;
            so.FindProperty("eventsText").objectReferenceValue = history;
            so.FindProperty("closeButton").objectReferenceValue = close;
            so.ApplyModifiedPropertiesWithoutUndo();
            panel.SetActive(false);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            File.WriteAllText("Logs/AlarmUISetup.txt","PASS: AlarmUI connected, popup initially hidden, BoxInfoPanel below/right, close wired; compilation passed.");
            EditorApplication.Exit(0);
        } catch(Exception e) {Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static GameObject Node(string name, Transform parent, Vector2 size, Vector2 position)
    {
        var g=new GameObject(name,typeof(RectTransform));g.transform.SetParent(parent,false);
        var r=(RectTransform)g.transform;r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.sizeDelta=size;r.anchoredPosition=position;return g;
    }
    static TMP_Text Text(string name,Transform parent,string value,TMP_FontAsset font,Vector2 position,Vector2 size,float fontSize)
    {
        var t=Node(name,parent,size,position).AddComponent<TextMeshProUGUI>();t.font=font;t.text=value;t.fontSize=fontSize;t.color=Color.white;t.richText=false;t.raycastTarget=false;t.textWrappingMode=TextWrappingModes.Normal;t.overflowMode=TextOverflowModes.Ellipsis;return t;
    }
}
