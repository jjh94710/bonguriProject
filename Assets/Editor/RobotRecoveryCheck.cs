using System;using System.IO;using System.Linq;using UnityEditor;using UnityEngine;
[InitializeOnLoad] public static class RobotRecoveryCheck{
 static double ready=EditorApplication.timeSinceStartup+4, since;static CorrectionSequence arm;static int phase;
 static RobotRecoveryCheck(){EditorApplication.update+=Tick;}
 static void Tick(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.timeSinceStartup<ready)return;
 if(phase==0){if(EditorApplication.isPlaying){EditorApplication.isPlaying=false;ready=EditorApplication.timeSinceStartup+3;return;}File.WriteAllText("Temp/RobotRecoveryReport.txt",string.Join("\n",UnityEngine.Object.FindObjectsByType<CorrectionSequence>().Select(a=>a.name+" pos="+a.transform.position+" box="+a.targetBox.bounds.center+" scale="+a.transform.lossyScale)));phase=1;EditorApplication.isPaused=false;EditorApplication.isPlaying=true;since=EditorApplication.timeSinceStartup;return;}
 if(!EditorApplication.isPlaying)return;EditorApplication.isPaused=false;Time.timeScale=1;Application.runInBackground=true;EditorApplication.QueuePlayerLoopUpdate();
 if(phase==1&&Time.time>.6f){arm=UnityEngine.Object.FindObjectsByType<CorrectionSequence>().Single(a=>a.name=="fr5_with_gripper_first");Selection.activeGameObject=arm.gameObject;arm.faceToRaise=CorrectionSequence.ParcelFace.Bottom;arm.RaiseSelectedFaceAndPlace();phase=2;}
 if(phase!=2)return;File.WriteAllText("Temp/RobotRecoveryProgress.txt","time="+Time.time+" phase="+arm.CycleStep+" status="+arm.Status+" angle="+Vector3.Angle(arm.SelectedFaceNormal,Vector3.up));
 if((!arm.SequenceRunning&&!arm.Moving)||EditorApplication.timeSinceStartup-since>210){File.AppendAllText("Temp/RobotRecoveryReport.txt","\nEND "+arm.Status+" angle="+Vector3.Angle(arm.SelectedFaceNormal,Vector3.up));EditorApplication.update-=Tick;EditorApplication.isPlaying=false;}
 }}
