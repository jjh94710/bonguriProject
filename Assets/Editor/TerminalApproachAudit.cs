using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
[InitializeOnLoad] public static class TerminalApproachAudit {
 const string Key="TerminalApproachAudit"; static double began; static LoadSequence[] loads; static readonly List<string> report=new List<string>(); static int index; static bool testing; static BoxInfo box;
 static TerminalApproachAudit(){if(SessionState.GetBool(Key,false)){began=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;}}
 public static void Run(){EditorSceneManager.OpenScene("Assets/Scenes/bongyuri.unity");SessionState.SetBool(Key,true);began=EditorApplication.timeSinceStartup;EditorApplication.update-=Tick;EditorApplication.update+=Tick;EditorApplication.isPlaying=true;}
 static void Tick(){try{if(EditorApplication.timeSinceStartup-began>240){End("TIMEOUT");return;}if(!EditorApplication.isPlaying||Time.time<2)return;
 if(loads==null){loads=UnityEngine.Object.FindObjectsByType<LoadSequence>(FindObjectsSortMode.None).OrderBy(x=>x.Terminal).ToArray();report.Add("Loaded scene: "+EditorSceneManager.GetActiveScene().path);}
 if(index>=loads.Length*3){End("DONE");return;}var seq=loads[index/3];var a=seq.Attacher;if(!a.IsReady)return;
 if(!testing){report.Add(seq.Terminal+" config="+seq.ValidateConfiguration(out var cfg)+" "+cfg+" robot="+a.transform.position.ToString("F4")+" stop="+seq.EndLine.StopPoint.position.ToString("F4")+" tcp="+a.tcp.position.ToString("F4"));
 var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name="ApproachAudit";go.transform.localScale=new Vector3(.05f,.06f,.07f);go.transform.rotation=Quaternion.Euler(index%3==1?new Vector3(0,90,0):index%3==2?new Vector3(90,0,0):Vector3.zero);go.transform.position=seq.EndLine.StopPoint.position+Vector3.up*(index%3==2?.035f:.03f);report.Add("Rotation="+go.transform.eulerAngles);go.AddComponent<Rigidbody>().isKinematic=true;box=go.AddComponent<BoxInfo>();box.boxId="AUDIT-"+seq.Terminal;box.destination=seq.Terminal;testing=true;bool ok=a.TryLoadBox(go.GetComponent<BoxCollider>(),out var error);report.Add("TryLoadBox="+ok+" "+error);if(!ok){Next(a);return;}}
 if(a.armIK.Moving||a.Attached||!a.Busy){report.Add(seq.Terminal+" approachMoving="+a.armIK.Moving+" attached="+a.Attached+" status="+a.Status+" ik="+a.armIK.Status);Next(a);}
 }catch(Exception e){End(e.ToString());}}
 static void Next(BoxAttacher a){a.StopLoading();if(box!=null)UnityEngine.Object.Destroy(box.gameObject);index++;testing=false;File.WriteAllLines("Logs/TerminalApproachAudit.txt",report);}
 static void End(string message){report.Add(message);File.WriteAllLines("Logs/TerminalApproachAudit.txt",report);SessionState.SetBool(Key,false);EditorApplication.update-=Tick;EditorApplication.Exit(message=="DONE"?0:1);}
}
