using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ConnectFactoryUI
{
    public static void Run()
    {
        try
        {
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/bongyuri.unity");
            var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MonoBehaviour>(true)).Where(c=>c!=null).ToArray();
            var cell=all.OfType<CellController>().Single();
            var line=new SerializedObject(cell).FindProperty("readLine").objectReferenceValue;
            if(line==null)throw new Exception("CellController readLine missing");
            var report=new System.Collections.Generic.List<string>();
            foreach(var type in new[]{typeof(DashboardUI),typeof(CellOperatorUI),typeof(BoxInfoPanel),typeof(AlarmUI)})
            {
                var panel=all.Single(c=>c.GetType()==type);
                var so=new SerializedObject(panel);
                var controller=so.FindProperty("controller");
                if(controller!=null)controller.objectReferenceValue=cell;
                if(panel is BoxInfoPanel)so.FindProperty("correctionLine").objectReferenceValue=line;
                so.ApplyModifiedPropertiesWithoutUndo();
                var property=so.GetIterator();
                while(property.NextVisible(true))
                {
                    if(property.propertyType!=SerializedPropertyType.ObjectReference||property.name=="previewBox"||property.propertyPath.StartsWith("viewCameras"))continue;
                    if(property.objectReferenceValue==null)throw new Exception(type.Name+" missing: "+property.propertyPath);
                    report.Add(type.Name+"."+property.propertyPath+" -> "+property.objectReferenceValue.name);
                }
                EditorUtility.SetDirty(panel);
            }
            var alarm=all.OfType<AlarmUI>().Single();
            ((GameObject)new SerializedObject(alarm).FindProperty("popup").objectReferenceValue).SetActive(false);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            File.WriteAllLines("Logs/FactoryUIConnections.txt",new[]{"PASS: four panels; controller/source/text/button references; popup initially hidden; Play not exercised"}.Concat(report));
            EditorApplication.Exit(0);
        }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
}
