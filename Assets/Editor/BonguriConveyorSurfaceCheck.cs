using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class BonguriConveyorSurfaceCheck
{
    const string Key="Bonguri.SurfaceCheck";
    static ConveyorLine[] lines;
    static BoxInfo[] boxes;
    static double began;
    static float penetration;
    static int samples;
    static float low=float.PositiveInfinity,high=float.NegativeInfinity;
    static BonguriConveyorSurfaceCheck(){if(SessionState.GetBool(Key,false))EditorApplication.update+=Tick;}
    [MenuItem("Tools/Bonguri/Integration/Verify Belt Surface Motion")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        SessionState.SetBool(Key,true);EditorApplication.update-=Tick;EditorApplication.update+=Tick;
        boxes=null;EditorApplication.isPlaying=true;
    }
    static void Tick()
    {
        if(!EditorApplication.isPlaying||Time.time<2)return;
        try
        {
            if(boxes==null)
            {
                began=EditorApplication.timeSinceStartup;samples=0;penetration=0;low=float.PositiveInfinity;high=float.NegativeInfinity;
                lines=UnityEngine.Object.FindObjectsByType<ConveyorLine>(FindObjectsSortMode.None).Where(l=>l.name=="TerminalA"||l.name=="TerminalB"||l.name=="TerminalC").OrderBy(l=>l.name).ToArray();
                if(lines.Length!=3)throw new Exception("Missing terminal routes.");
                boxes=new BoxInfo[3];
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/Process/ParcelBox_6x8x12cm_Process.prefab");
                for(int i=0;i<3;i++)
                {
                    if(!lines[i].UsesSurfacePath||!lines[i].ValidateSurfacePath(out string error))throw new Exception("Invalid route: "+lines[i].name);
                    var edit=new SerializedObject(lines[i]);edit.FindProperty("speed").floatValue=2;edit.FindProperty("autoRelease").boolValue=false;edit.ApplyModifiedPropertiesWithoutUndo();
                    boxes[i]=UnityEngine.Object.Instantiate(prefab).GetComponent<BoxInfo>();boxes[i].boxId="SURFACE-"+i;
                    boxes[i].transform.rotation=Quaternion.Euler(0,90*i,0);
                    if(!lines[i].Feed(boxes[i]))throw new Exception("Feed rejected: "+lines[i].name);
                }
            }
            for(int i=0;i<3;i++)
            {
                var box=boxes[i];var body=box.GetComponent<Rigidbody>();var shape=box.GetComponent<BoxCollider>();
                low=Mathf.Min(low,body.position.y);high=Mathf.Max(high,body.position.y);
                var edit=new SerializedObject(lines[i]);var surfaces=edit.FindProperty("beltSurfaces");
                for(int j=0;j<surfaces.arraySize;j++)
                {
                    var belt=(Collider)surfaces.GetArrayElementAtIndex(j).objectReferenceValue;
                    if(Physics.ComputePenetration(shape,body.position,body.rotation,belt,belt.transform.position,belt.transform.rotation,out _,out float depth))penetration=Mathf.Max(penetration,depth);
                }
                samples++;
            }
            if(lines.All(l=>l.HasBoxAtStop)){Finish(null);return;}
            if(EditorApplication.timeSinceStartup-began>45)Finish("Route motion timed out.");
        }
        catch(Exception e){Finish(e.ToString());}
    }
    [Serializable]sealed class Report{public bool passed;public string error;public int samples;public float maximumPenetration,minHeight,maxHeight;public string[] stopped;}
    static void Finish(string error)
    {
        EditorApplication.update-=Tick;SessionState.SetBool(Key,false);
        var report=new Report{passed=error==null&&penetration<.001f&&high-low>.6f,error=error??"",samples=samples,maximumPenetration=penetration,minHeight=low,maxHeight=high,stopped=lines==null?new string[0]:lines.Where(l=>l.HasBoxAtStop).Select(l=>l.name).ToArray()};
        Directory.CreateDirectory("results/conveyor_surface_check");File.WriteAllText("results/conveyor_surface_check/latest.json",JsonUtility.ToJson(report,true));
        Debug.Log("[Surface Check] passed="+report.passed+" samples="+samples+" max penetration="+penetration);
        EditorApplication.isPlaying=false;
    }
}
