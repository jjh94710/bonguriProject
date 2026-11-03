using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
[InitializeOnLoad] public static class SuctionProbe {
 static SuctionProbe(){EditorApplication.delayCall+=Dump;}
 static void Dump(){var all=Resources.FindObjectsOfTypeAll<Transform>().Where(t=>t.gameObject.scene.IsValid()).ToArray();var robots=all.Where(t=>t.name.StartsWith("fr5_suction")&&!t.parent?.name.StartsWith("fr5_suction")==true).ToArray();
 File.WriteAllText("Temp/SuctionProbe.txt",string.Join("\n",all.Where(t=>t.name.StartsWith("fr5_suction")||t.name.StartsWith("ParcelContainer")||t.name=="ParcelBox_ProcessVariant").Select(t=>t.name+" pos="+t.position+" rot="+t.eulerAngles+" scale="+t.lossyScale+"\n"+string.Join("\n",t.GetComponentsInChildren<Transform>().Where(c=>c.name.Contains("suction")||c.name=="tcp"||c.name.StartsWith("Slot_")).Select(c=>c.name+" pos="+c.position+" axes="+c.forward+" / "+c.up+" body="+c.GetComponent<ArticulationBody>()))) ));}
}
