using System;
using System.IO;
using System.Reflection;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

[InitializeOnLoad]
public static class ReadLineUIRegression
{
    const string Key="ReadLineUIRegression.Run";
    static double started;
    static ReadLineUIRegression(){if(SessionState.GetBool(Key,false))Hook();}
    static void Hook(){started=EditorApplication.timeSinceStartup;EditorApplication.update-=Tick;EditorApplication.update+=Tick;}
    public static void Run(){EditorSceneManager.OpenScene("Assets/Scenes/bongyuri.unity");SessionState.SetBool(Key,true);Hook();EditorApplication.isPlaying=true;}
    static void Tick()
    {
        if(EditorApplication.timeSinceStartup-started>60){Finish(false,"Play startup timeout");return;}
        if(!EditorApplication.isPlaying||Time.time<1)return;
        EditorApplication.update-=Tick;
        try{
            var panel=UnityEngine.Object.FindFirstObjectByType<BoxInfoPanel>();
            var ps=new SerializedObject(panel);
            var cell=UnityEngine.Object.FindFirstObjectByType<CellController>();
            var read=(ConveyorLine)new SerializedObject(cell).FindProperty("readLine").objectReferenceValue;
            Require(ps.FindProperty("correctionLine").objectReferenceValue==read,"ReadLine reference");
            var boxText=(TMP_Text)ps.FindProperty("boxText").objectReferenceValue;
            var labelText=(TMP_Text)ps.FindProperty("labelText").objectReferenceValue;
            var destination=(TMP_Text)ps.FindProperty("destinationText").objectReferenceValue;
            var arrive=typeof(ConveyorLine).GetMethod("Arrive",BindingFlags.NonPublic|BindingFlags.Instance);
            BoxInfo Make(string id){var go=new GameObject(id);go.transform.position=read.StopPoint.position;var box=go.AddComponent<BoxInfo>();box.boxId=id;box.hasLabel=true;box.state=BoxState.Reading;return box;}
            var first=Make("UI-READ-001");arrive.Invoke(read,new object[]{first});
            Require(boxText.text==first.boxId,"Immediate first arrival display");
            read.TakeAway(first);first.readText="11001";first.readRegion="서울";first.destination=Destination.A;panel.Refresh();
            Require(boxText.text==first.boxId&&labelText.text.Contains("11001"),"Retain first parcel after departure and show recognition update");
            var second=Make("UI-READ-002");arrive.Invoke(read,new object[]{second});read.TakeAway(second);
            Require(boxText.text==second.boxId&&!labelText.text.Contains("11001"),"Same-frame second arrival/departure switches and clears old recognition");
            second.readText="33002";second.readRegion="부산";second.destination=Destination.C;panel.Refresh();
            Require(labelText.text.Contains("33002")&&destination.text.Contains("C"),"Second recognition/destination shown");
            panel.gameObject.SetActive(false);var third=Make("UI-READ-003");arrive.Invoke(read,new object[]{third});read.TakeAway(third);panel.gameObject.SetActive(true);
            Require(boxText.text==third.boxId,"Disabled UI catches last arrival when re-enabled");
            Finish(true,"PASS: correct ReadLine; immediate arrival; same-frame arrival/departure; retained recognition updates; next parcel reset; disabled UI recovery. Physical OCR not exercised.");
        }catch(Exception e){Finish(false,e.ToString());}
    }
    static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Finish(bool ok,string report){SessionState.SetBool(Key,false);EditorApplication.update-=Tick;File.WriteAllText("Logs/ReadLineUIRegression.txt",report);EditorApplication.Exit(ok?0:1);}
}
