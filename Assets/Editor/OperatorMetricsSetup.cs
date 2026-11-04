using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class OperatorMetricsSetup
{
    public static void Run()
    {
        try {
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/bongyuri.unity");
            var ui=UnityEngine.Object.FindFirstObjectByType<CellOperatorUI>();var panel=ui.transform;
            var so=new SerializedObject(ui);
            foreach(string name in new[]{"decreaseButton","increaseButton","basicButton","policyButton","intervalText"}){
                var c=so.FindProperty(name).objectReferenceValue as Component;if(c!=null)c.gameObject.SetActive(false);
            }
            foreach(var t in panel.GetComponentsInChildren<TMP_Text>(true))if(t.text=="투입 간격"||t.text=="제어 방식")t.gameObject.SetActive(false);
            var font=panel.GetComponentsInChildren<TMP_Text>(true).First().font;
            float width=((RectTransform)panel).rect.width;
            string[] fields={"successText","failText","waitText","alarmText"};
            string[] captions={"분류 성공","분류 실패","대기 시간","알람 상태"};
            for(int i=0;i<4;i++){
                float x=24+(i%2)*(width-48)/2;float y=-132-(i/2)*34;
                Text(fields[i]+"Caption",panel,captions[i],font,x,y,108,22,17,new Color(.75f,.81f,.87f));
                var value=Text(fields[i],panel,"—",font,x+112,y,120,24,19,Color.white);
                so.FindProperty(fields[i]).objectReferenceValue=value;
            }
            var log=Text("LogPath",panel,"미연결 · ProductionLog 없음",font,24,-204,width-178,24,16,new Color(.75f,.81f,.87f));
            so.FindProperty("logPathText").objectReferenceValue=log;
            var button=new GameObject("ResetAlarm",typeof(RectTransform),typeof(Image),typeof(Button));button.transform.SetParent(panel,false);
            var rect=(RectTransform)button.transform;rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(width-146,-200);rect.sizeDelta=new Vector2(122,30);
            var image=button.GetComponent<Image>();image.color=new Color(.45f,.26f,.18f,.95f);
            Text("Label",button.transform,"알람 해제",font,0,0,122,30,18,Color.white).alignment=TextAlignmentOptions.Center;
            so.FindProperty("resetAlarmButton").objectReferenceValue=button.GetComponent<Button>();so.ApplyModifiedPropertiesWithoutUndo();
            ui.Refresh();EditorUtility.SetDirty(ui);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            File.WriteAllText("Logs/OperatorMetricsSetup.txt","PASS: interval/mode hidden; success/fail/alarm connected; reset wired; wait/log explicitly unavailable, not simulated. Compile passed. Play not tested.");EditorApplication.Exit(0);
        }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static TMP_Text Text(string name,Transform parent,string text,TMP_FontAsset font,float x,float y,float w,float h,float size,Color color){
        var g=new GameObject(name,typeof(RectTransform));g.transform.SetParent(parent,false);var r=(RectTransform)g.transform;r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,y);r.sizeDelta=new Vector2(w,h);
        var t=g.AddComponent<TextMeshProUGUI>();t.font=font;t.fontSize=size;t.text=text;t.color=color;t.raycastTarget=false;t.textWrappingMode=TextWrappingModes.NoWrap;t.overflowMode=TextOverflowModes.Ellipsis;return t;
    }
}
