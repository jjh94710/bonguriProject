using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
[InitializeOnLoad] public static class TerminalPhysicalAudit {
 const string Key="TerminalPhysicalAudit";static double began; static LoadSequence[] seqs; static bool launched;static string[] last;static bool[] finished;static bool[] attached;static float[] bottoms,maxLift;static BoxCollider[] parcels;static readonly List<string> log=new List<string>();
 static string Axis {get {var args=Environment.GetCommandLineArgs();var i=Array.IndexOf(args,"-auditAxis");return i>=0&&i+1<args.Length?args[i+1]:"X";}}
 static string Report=>"Logs/TerminalPhysicalAudit-"+Axis+".txt";
 static TerminalPhysicalAudit(){if(SessionState.GetBool(Key,false)){began=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;}}
 public static void Run(){EditorSceneManager.OpenScene("Assets/Scenes/bongyuri.unity");SessionState.SetBool(Key,true);began=EditorApplication.timeSinceStartup;EditorApplication.update-=Tick;EditorApplication.update+=Tick;EditorApplication.isPlaying=true;}
 static void Tick(){try{if(EditorApplication.timeSinceStartup-began>240){End("TIMEOUT");return;}if(!EditorApplication.isPlaying||Time.time<2)return;
 if(!launched){seqs=UnityEngine.Object.FindObjectsByType<LoadSequence>(FindObjectsSortMode.None).OrderBy(x=>x.Terminal).ToArray();if(seqs.Any(s=>!s.Attacher.IsReady))return;last=new string[seqs.Length];finished=new bool[seqs.Length];attached=new bool[seqs.Length];bottoms=new float[seqs.Length];maxLift=new float[seqs.Length];parcels=new BoxCollider[seqs.Length];launched=true;
 log.Add("Current saved scene; no robot/tote relocation; 5x6x7cm parcel; world face direction="+Axis);
 for(int i=0;i<seqs.Length;i++){var s=seqs[i];log.Add(s.Terminal+" robot="+s.Attacher.transform.position.ToString("F4")+" tote="+s.Attacher.tote.transform.position.ToString("F4")+" stop="+s.EndLine.StopPoint.position.ToString("F4"));
 var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name="PhysicalAudit-"+s.Terminal;go.transform.localScale=new Vector3(.05f,.06f,.07f);go.transform.rotation=Quaternion.Euler(Axis=="X"?new Vector3(0,90,0):Axis=="Y"?new Vector3(90,0,0):Vector3.zero);go.transform.position=s.EndLine.StopPoint.position+Vector3.up*(Axis=="Y"?.0355f:.0305f);var rb=go.AddComponent<Rigidbody>();rb.isKinematic=true;rb.mass=.15f;rb.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;var box=go.AddComponent<BoxInfo>();box.boxId=go.name;box.destination=s.Terminal;parcels[i]=go.GetComponent<BoxCollider>();Physics.SyncTransforms();bottoms[i]=parcels[i].bounds.min.y;bool ok=s.Attacher.TryLoadBox(parcels[i],out var error);log.Add(s.Terminal+" started="+ok+" "+error);}}
 for(int i=0;i<seqs.Length;i++){var a=seqs[i].Attacher;attached[i]|=a.Attached;maxLift[i]=Mathf.Max(maxLift[i],parcels[i].bounds.min.y-bottoms[i]);if(last[i]!=a.Status){last[i]=a.Status;log.Add(Time.time.ToString("F2")+" "+seqs[i].Terminal+" "+a.Status+" | "+a.armIK.Status);File.WriteAllLines(Report,log);}if(!a.Busy&&!finished[i]){finished[i]=true;log.Add("RESULT "+seqs[i].Terminal+" axis="+Axis+" attached="+attached[i]+" maxLift="+maxLift[i].ToString("F3")+" placed="+a.PlacedBoxes.Count+" success="+a.LastLoadSucceeded+" tote="+a.tote.Count+" final="+a.Status);}}
 if(finished.All(x=>x))End(seqs.All(s=>s.Attacher.PlacedBoxes.Count>0)?"PASS":"FAIL");
 }catch(Exception e){End(e.ToString());}}
 static void End(string text){log.Add(text);File.WriteAllLines(Report,log);SessionState.SetBool(Key,false);EditorApplication.update-=Tick;EditorApplication.Exit(text=="PASS"||text=="FAIL"?0:1);}
}
