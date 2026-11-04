using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class FixOperatorMetricText
{
    public static void Run()
    {
        try {
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/bongyuri.unity");
            var ui=UnityEngine.Object.FindFirstObjectByType<CellOperatorUI>();
            ui.Refresh();
            var report=new System.Collections.Generic.List<string>();
            foreach(string field in new[]{"successText","failText","waitText","alarmText"})
            foreach(string name in new[]{field,field+"Caption"}) {
                var text=ui.transform.Find(name).GetComponent<TMP_Text>();
                text.gameObject.SetActive(true);text.enabled=true;
                var size=text.rectTransform.sizeDelta;size.y=32;text.rectTransform.sizeDelta=size;
                text.alignment=TextAlignmentOptions.MidlineLeft;
                text.overflowMode=TextOverflowModes.Overflow;
                text.textWrappingMode=TextWrappingModes.NoWrap;
                text.ForceMeshUpdate();
                if(text.textInfo.characterCount==0)throw new Exception(name+" no generated characters");
                report.Add(name+": "+text.text+"; characters="+text.textInfo.characterCount);
                EditorUtility.SetDirty(text);
            }
            Canvas.ForceUpdateCanvases();
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            File.WriteAllLines("Logs/OperatorMetricTextFix.txt",report);EditorApplication.Exit(0);
        }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
}
