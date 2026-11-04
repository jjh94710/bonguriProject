using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
[InitializeOnLoad] public static class TerminalStackAudit {
 const string Key="TerminalStackAudit";static double began;static LoadSequence[] seqs;static string[] last;static bool[] finished,pending;static float[] nextAt;static int[] counts;static readonly List<string> log=new List<string>();
 static TerminalStackAudit(){if(SessionState.GetBool(Key,false)){began=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;}}
 public static void Run(){EditorSceneManager.OpenScene("Assets/Scenes/bongyuri.unity");SessionState.SetBool(Key,true);began=EditorApplication.timeSinceStartup;EditorApplication.update-=Tick;EditorApplication.update+=Tick;EditorApplication.isPlaying=true;}
 static void Tick(){try{if(EditorApplication.timeSinceStartup-began>600){End("TIMEOUT");return;}if(!EditorApplication.isPlaying||Time.time<2)return;
 if(seqs==null){var found=UnityEngine.Object.FindObjectsByType<LoadSequence>(FindObjectsSortMode.None).OrderBy(x=>x.Terminal).ToArray();if(found.Length!=3||found.Any(s=>!s.Attacher.IsReady))return;seqs=found;last=new string[3];finished=new bool[3];pending=new bool[3];nextAt=new float[3];counts=new int[3];log.Add("Eight parcels per robot, X/Y/Z cycle; saved layout; no simulated attachment/registration.");}
 for(int i=0;i<3;i++){if(finished[i])continue;var a=seqs[i].Attacher;
 if(last[i]!=a.Status){last[i]=a.Status;log.Add(Time.time.ToString("F2")+" "+seqs[i].Terminal+" "+a.Status);File.WriteAllLines("Logs/TerminalStackAudit.txt",log);}
 if(pending[i]&&!a.Busy){pending[i]=false;int count=a.PlacedBoxes.Count;log.Add("RESULT "+seqs[i].Terminal+" box="+(counts[i]+1)+" count="+count+" success="+a.LastLoadSucceeded+" status="+a.Status);if(count!=counts[i]+1||!a.LastLoadSucceeded){finished[i]=true;continue;}counts[i]=count;if(count>=8){finished[i]=true;continue;}nextAt[i]=Time.time+1;}
 if(!finished[i]&&!pending[i]&&Time.time>=nextAt[i]){
 int axis=counts[i]%3;var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name="StackAudit-"+seqs[i].Terminal+"-"+(counts[i]+1);go.transform.localScale=new Vector3(.05f,.06f,.07f);go.transform.rotation=Quaternion.Euler(axis==0?new Vector3(0,90,0):axis==1?new Vector3(90,0,0):Vector3.zero);go.transform.position=seqs[i].EndLine.StopPoint.position+Vector3.up*(axis==1?.0355f:.0305f);var rb=go.AddComponent<Rigidbody>();rb.mass=.15f;rb.isKinematic=true;rb.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;var box=go.AddComponent<BoxInfo>();box.boxId=go.name;box.destination=seqs[i].Terminal;Physics.SyncTransforms();pending[i]=a.TryLoadBox(go.GetComponent<BoxCollider>(),out var error);log.Add("START "+seqs[i].Terminal+" box="+(counts[i]+1)+" axis="+axis+" "+error);if(!pending[i])finished[i]=true;
 }}
 if(finished.All(x=>x))End(counts.All(x=>x==8)?"PASS":"FAIL");
 }catch(Exception e){End(e.ToString());}}
 static void End(string result){if(counts!=null)log.Add("FINAL A="+counts[0]+" B="+counts[1]+" C="+counts[2]);log.Add(result);File.WriteAllLines("Logs/TerminalStackAudit.txt",log);SessionState.SetBool(Key,false);EditorApplication.update-=Tick;EditorApplication.Exit(result=="PASS"||result=="FAIL"?0:1);}
}
