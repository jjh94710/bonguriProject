using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

[InitializeOnLoad]
public static class MergeDevelopVerification
{
    const string Key="MergeDevelopVerification.Play";
    static readonly List<string> report=new List<string>();
    static readonly List<string> errors=new List<string>();
    static int phase=-1;
    static BoxInfo parcel;
    static float phaseStarted;
    static double wallStarted;
    static readonly Destination[] cases={Destination.NG,Destination.A,Destination.B,Destination.C};
    static MergeDevelopVerification(){if(SessionState.GetBool(Key,false))Hook();}
    public static void Run()
    {
        try{
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/bongyuri.unity");
            var cell=UnityEngine.Object.FindFirstObjectByType<CellController>();
            var so=new SerializedObject(cell);
            var log=cell.GetComponent<ProductionLog>();if(log==null)log=cell.gameObject.AddComponent<ProductionLog>();
            so.FindProperty("log").objectReferenceValue=log;so.ApplyModifiedPropertiesWithoutUndo();
            foreach(string field in new[]{"correctLine","readLine","feeder","faceRig","faceDetector","reader","correction","log"})
                Require(so.FindProperty(field).objectReferenceValue!=null,"Cell "+field);
            Require(Mathf.Abs(so.FindProperty("correctTimeLimit").floatValue-90)<.01f,"Correction time preserved");
            var ui=UnityEngine.Object.FindFirstObjectByType<BoxInfoPanel>();
            Require(new SerializedObject(ui).FindProperty("correctionLine").objectReferenceValue==so.FindProperty("readLine").objectReferenceValue,"BoxInfoPanel uses ReadLine");
            foreach(var panel in new MonoBehaviour[]{ui,UnityEngine.Object.FindFirstObjectByType<CellOperatorUI>(),UnityEngine.Object.FindFirstObjectByType<DashboardUI>(),UnityEngine.Object.FindFirstObjectByType<AlarmUI>()}){
                Require(panel!=null,"UI present");var ps=new SerializedObject(panel);var p=ps.GetIterator();while(p.NextVisible(true)){
                    if(p.propertyType!=SerializedPropertyType.ObjectReference||p.name=="previewBox"||p.propertyPath.StartsWith("viewCameras"))continue;
                    Require(p.objectReferenceValue!=null,panel.GetType().Name+"."+p.propertyPath);
                }
            }
            var corrections=UnityEngine.Object.FindObjectsByType<CorrectionSequence>(FindObjectsSortMode.None);
            foreach(var front in corrections){Require(front.GetComponent<FrontArmIK>()!=null,"FrontArmIK preserved");Require(new SerializedObject(front).FindProperty("ik").objectReferenceValue==front.GetComponent<FrontArmIK>(),"Front IK reference");}
            var sequences=UnityEngine.Object.FindObjectsByType<LoadSequence>(FindObjectsSortMode.None);
            Require(sequences.Length==3,"Three LoadSequences");
            foreach(var sequence in sequences){Require(sequence.ValidateConfiguration(out string error),error??"Sequence valid");Require(sequence.Cell==cell,"Sequence cell");Require(Vector3.Distance(sequence.EndLine.StopPoint.position,sequence.Attacher.transform.position)<1,"Robot remains near stop "+sequence.Terminal);}
            var tote=GameObject.Find("RejectCollectionBox").GetComponent<NgTote>();Require(tote!=null,"NG tote");Require(new SerializedObject(tote).FindProperty("cell").objectReferenceValue==cell,"NG cell");
            var ng=GameObject.Find("SeparatorC").GetComponent<BoxPusher>();Require(new SerializedObject(ng).FindProperty("toTote").objectReferenceValue==tote,"NG pusher tote");
            var correct=(ConveyorLine)so.FindProperty("correctLine").objectReferenceValue;Require(new SerializedObject(correct).FindProperty("stationApproachClearance").floatValue>=.35f,"Robot queue clearance preserved");
            var op=UnityEngine.Object.FindFirstObjectByType<CellOperatorUI>();op.Refresh();
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            File.WriteAllLines("Logs/MergeDevelop20261104.txt",new[]{"PASS compile, UI references, ReadLine display, NG, front IK, three suction stations; ProductionLog connected; queue clearance preserved. Remote not pushed."}.Concat(report));
            EditorApplication.Exit(0);
        }catch(Exception e){Debug.LogException(e);File.WriteAllText("Logs/MergeDevelop20261104.txt","FAIL\n"+e);EditorApplication.Exit(1);}
    }
    static void Require(bool ok,string text){if(!ok)throw new Exception(text);report.Add(text+": OK");}
    public static void RunPlay(){EditorSceneManager.OpenScene("Assets/Scenes/bongyuri.unity");SessionState.SetBool(Key,true);Hook();EditorApplication.isPlaying=true;}
    static void Hook(){wallStarted=EditorApplication.timeSinceStartup;EditorApplication.update-=Tick;EditorApplication.update+=Tick;Application.logMessageReceived-=Capture;Application.logMessageReceived+=Capture;}
    static void Capture(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message);}
    static void Tick()
    {
        if(EditorApplication.timeSinceStartup-wallStarted>150){Finish(false,"Test timed out");return;}
        if(!EditorApplication.isPlaying||Time.time<2)return;
        try{
            if(errors.Count>0)throw new Exception(string.Join("\n",errors));
            var cell=UnityEngine.Object.FindFirstObjectByType<CellController>();
            var sort=UnityEngine.Object.FindObjectsByType<ConveyorLine>(FindObjectsSortMode.None).Single(l=>l.name=="SortLine");
            if(phase<0){
                var alarm=UnityEngine.Object.FindFirstObjectByType<AlarmUI>();Require(!alarm.IsVisible,"Alarm initially hidden");
                cell.RaiseAlarm("통합 연결 시험","CHECK");Require(alarm.IsVisible,"Alarm event shows popup");alarm.Dismiss();Require(!alarm.IsVisible,"Alarm dismiss");
                var nav=UnityEngine.Object.FindFirstObjectByType<FirstConveyorViewSwitch>();
                nav.ShowContainerA();nav.ShowContainerAArm();nav.GoBack();nav.ShowContainerB();nav.ShowContainerBArm();nav.GoBack();nav.ShowContainerC();nav.ShowContainerCArm();nav.GoBack();nav.ShowFirst();
                Require((bool)typeof(CellController).GetMethod("Check",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(cell,null),"Cell runtime configuration/models ready");
                phase=0;
            }
            if(phase>=cases.Length){Finish(true,"PASS Play startup, alarm, navigation, runtime model configuration, NG/A/B/C conveyor routing (injected parcels; OCR and robot grasp not exercised).");return;}
            if(parcel==null){
                if(!sort.CanAccept)return;
                var obj=new GameObject("MergeRouteTest_"+cases[phase]);var collider=obj.AddComponent<BoxCollider>();collider.size=new Vector3(.05f,.06f,.07f);obj.AddComponent<Rigidbody>().isKinematic=true;
                parcel=obj.AddComponent<BoxInfo>();parcel.boxId=obj.name;parcel.destination=cases[phase];parcel.state=BoxState.Sorting;
                if(cases[phase]==Destination.NG)parcel.Mark(ExceptionReason.NoLabel);
                Require(sort.Feed(parcel),"Inject "+cases[phase]);phaseStarted=Time.time;return;
            }
            if(Time.time-phaseStarted>25)throw new Exception("Routing timeout "+cases[phase]);
            bool arrived;
            if(cases[phase]==Destination.NG){var tote=GameObject.Find("RejectCollectionBox").GetComponent<NgTote>();arrived=parcel.IsFinished&&parcel.transform.IsChildOf(tote.transform);if(arrived){int before=cell.Processed;tote.Add(parcel);Require(cell.Processed==before,"NG duplicate Finish ignored");Require(cell.Log!=null&&File.Exists(cell.Log.BoxFilePath),"CSV box log created");}}
            else {var line=UnityEngine.Object.FindObjectsByType<ConveyorLine>(FindObjectsSortMode.None).Single(l=>l.name=="Terminal"+cases[phase]);var list=(System.Collections.IList)typeof(ConveyorLine).GetField("boxes",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(line);arrived=list.Contains(parcel);if(arrived)line.TakeAway(parcel);}
            if(arrived){report.Add(cases[phase]+" actual conveyor handoff PASS");UnityEngine.Object.Destroy(parcel.gameObject);parcel=null;phase++;}
        }catch(Exception e){Finish(false,e.ToString());}
    }
    static void Finish(bool ok,string result){SessionState.SetBool(Key,false);EditorApplication.update-=Tick;File.WriteAllLines("Logs/MergeDevelopPlay20261104.txt",new[]{result}.Concat(report));EditorApplication.Exit(ok?0:1);}
}
