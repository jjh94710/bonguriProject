using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
[InitializeOnLoad] public static class PusherSceneProbe {
 static PusherSceneProbe() { EditorApplication.delayCall += Dump; }
 static void Dump() {
 var all=Resources.FindObjectsOfTypeAll<Transform>().Where(t=>t.gameObject.scene.IsValid()).ToArray();
 File.WriteAllText("Temp/PusherSceneProbe.txt", string.Join("\n",all.Where(t=>t.name.StartsWith("ConveyorBeltStraightMixed.029")||t.name.StartsWith("Separator")||t.name=="PusherTestParcel").Select(t=>t.name+" p="+t.position+" scale="+t.lossyScale+" rot="+t.eulerAngles+" coll="+string.Join(";",t.GetComponents<Collider>().Select(c=>c.bounds.ToString()))+" render="+string.Join(";",t.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.name.Contains("LOD0")).Select(r=>r.name+" "+r.bounds)))));
 }
}
