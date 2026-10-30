using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
[InitializeOnLoad] public static class StackToteProbe {
 static StackToteProbe(){EditorApplication.delayCall+=Dump;}
 static void Dump(){var all=Resources.FindObjectsOfTypeAll<Transform>().Where(t=>t.gameObject.scene.IsValid()).ToArray();
 File.WriteAllText("Temp/StackToteLocations.txt","view="+SceneView.lastActiveSceneView?.pivot+"\n"+string.Join("\n",all.Where(t=>t.name.StartsWith("Cube")||t.name.ToLower().Contains("tote")||t.name.ToLower().Contains("container")).Select(t=>t.name+" p="+t.position+" scale="+t.lossyScale+" coll="+string.Join(";",t.GetComponentsInChildren<Collider>().Select(c=>c.bounds.ToString())))));
 var g=Selection.activeGameObject;var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/ParcelBox_ProcessVariant.prefab");
 File.WriteAllText("Temp/StackToteProbe.txt","play="+EditorApplication.isPlaying+" selection="+(g==null?"none":g.name+" p="+g.transform.position+" rotation="+g.transform.eulerAngles+" scale="+g.transform.lossyScale+"\n"+string.Join("\n",g.GetComponentsInChildren<Collider>().Select(c=>c.name+" "+c.bounds)))+"\nprefab="+(prefab==null?"missing":string.Join("\n",prefab.GetComponentsInChildren<BoxCollider>().Select(c=>c.name+" size="+c.size+" center="+c.center+" scale="+c.transform.lossyScale))));}
}
