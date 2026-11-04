using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Explicit, scene-local 2x2x2 loading check. Uses real suction and physics.</summary>
[InitializeOnLoad]
public static class BonguriEightBoxCheck
{
    const string Key="Bonguri.EightBoxCheck";
    static bool started,oldBackground;
    static double began,nextWrite;
    static Job[] jobs;
    static BoxInfo prefab;
    sealed class Job
    {
        public BoxAttacher loader;public ConveyorLine line;public BoxInfo pending;
        public double since,settleAfter;public int requested;public bool done;
        public Record record;
    }
    [Serializable] public sealed class Record
    {
        public string robot,status,error="",nextAxis;
        public int generated,placed;
        public bool complete,passed,stable,fullRejected;
        public float maximumOverlap;
        public Vector3[] positions;
    }
    [Serializable] sealed class Report{public bool complete,passed;public float elapsed;public string error;public Record[] robots;}
    static BonguriEightBoxCheck(){EditorApplication.update+=Tick;}
    [MenuItem("Tools/Bonguri/Integration/Verify Eight Boxes Per Tote A B C")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        started=false;jobs=null;
        SessionState.SetBool(Key,true);EditorApplication.isPaused=false;EditorApplication.isPlaying=true;
    }
    static void Write(bool complete,string error=null)
    {
        Directory.CreateDirectory("results/eight_box_check");
        var records=jobs==null?new Record[0]:jobs.Select(j=>j.record).ToArray();
        File.WriteAllText("results/eight_box_check/latest.json",JsonUtility.ToJson(new Report{
            complete=complete,passed=complete&&error==null&&records.Length==3&&records.All(r=>r.passed),
            elapsed=(float)(EditorApplication.timeSinceStartup-began),error=error??"",robots=records},true));
    }
    static void Fail(Job job,string error)
    {job.record.error=error;job.record.status=job.loader.Status;job.record.complete=true;job.done=true;}
    static void Tick()
    {
        if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||Time.time<2)return;
        try
        {
            if(!started)
            {
                started=true;began=EditorApplication.timeSinceStartup;nextWrite=0;
                oldBackground=Application.runInBackground;Application.runInBackground=true;
                prefab=UnityEngine.Object.FindFirstObjectByType<BoxFeeder>().ParcelPrefab;
                var loaders=UnityEngine.Object.FindObjectsByType<BoxAttacher>(FindObjectsSortMode.None).Where(l=>l.name=="fr5_suctionA"||l.name=="fr5_suctionB"||l.name=="fr5_suctionC").OrderBy(l=>l.name).ToArray();
                if(loaders.Length!=3)throw new Exception("Expected A/B/C loading robots.");
                jobs=loaders.Select(l=>new Job{loader=l,line=UnityEngine.Object.FindObjectsByType<ConveyorLine>(FindObjectsSortMode.None).Single(c=>c.name=="Terminal"+l.name.Last()),record=new Record{robot=l.name}}).ToArray();
                foreach(var j in jobs)
                    if(!j.loader.IsReady||j.loader.Busy||j.loader.Attached||j.loader.tote.Count!=0||j.line.QueueCount!=0||j.loader.tote.Capacity!=8)
                        throw new Exception(j.loader.name+": start with an empty, idle 8-slot tote.");
            }
            EditorApplication.QueuePlayerLoopUpdate();
            foreach(var job in jobs.Where(j=>!j.done))Step(job);
            if(jobs.All(j=>j.done)){Finish(null);return;}
            if(EditorApplication.timeSinceStartup-began>1500){Finish("Eight-box check timed out.");return;}
            if(EditorApplication.timeSinceStartup>=nextWrite){Write(false);nextWrite=EditorApplication.timeSinceStartup+1;}
        }
        catch(Exception e){Finish(e.ToString());}
    }
    static void Step(Job job)
    {
        var loader=job.loader;var tote=loader.tote;var record=job.record;
        record.status=loader.Status;record.placed=tote.Count;
        if(loader.Busy)
        {
            if(EditorApplication.timeSinceStartup-job.since>180){loader.StopLoading();Fail(job,"Loading one parcel exceeded 180 seconds.");}
            return;
        }
        if(job.pending!=null)
        {
            var box=job.pending.GetComponent<BoxCollider>();
            if(!loader.PlacedBoxes.Contains(box)||tote.Count!=job.requested||loader.Attached||!loader.LastLoadSucceeded)
            {Fail(job,"Parcel "+job.requested+": "+loader.Status);return;}
            Debug.Log("[Eight Box Check] "+loader.name+" placed "+tote.Count+"/8 ("+record.nextAxis+")");
            job.pending=null;job.settleAfter=EditorApplication.timeSinceStartup+1;
        }
        if(EditorApplication.timeSinceStartup<job.settleAfter)return;
        if(tote.Count==8)
        {
            var boxes=tote.Boxes;record.positions=boxes.Select(b=>b.bounds.center).ToArray();
            record.stable=boxes.Select((b,i)=>tote.IsSettledAt(i,b)).All(ok=>ok);
            for(int i=0;i<8;i++)for(int j=i+1;j<8;j++)
                if(Physics.ComputePenetration(boxes[i],boxes[i].transform.position,boxes[i].transform.rotation,boxes[j],boxes[j].transform.position,boxes[j].transform.rotation,out _,out float depth))record.maximumOverlap=Mathf.Max(record.maximumOverlap,depth);
            var ninth=UnityEngine.Object.Instantiate(prefab,new Vector3(100,100,100),Quaternion.identity);
            try
            {
                ninth.GetComponent<Rigidbody>().isKinematic=true;
                record.fullRejected=!loader.TryLoadBox(ninth.GetComponent<BoxCollider>(),out string why)&&why!=null&&why.Contains("가득")&&!loader.Busy&&tote.Count==8;
            }
            finally{UnityEngine.Object.DestroyImmediate(ninth.gameObject);}
            record.passed=record.stable&&record.fullRejected&&record.maximumOverlap<.002f;
            record.complete=true;job.done=true;
            if(!record.passed)record.error="Final stability, overlap or ninth-parcel rejection failed.";
            return;
        }
        var direction=(BonguriIntegrationPanel.PickupDirection)(job.requested%3);
        var rotation=BonguriIntegrationPanel.PickupRotation(direction);
        var parcel=UnityEngine.Object.Instantiate(prefab,new Vector3(100,100,100),rotation);
        parcel.GetComponent<Rigidbody>().isKinematic=true;
        if(!job.line.ValidateSurfacePath(out string error)||!job.line.TryGetSurfacePose(parcel,rotation,job.line.PathLength,out var p,out var r,out error))
        {UnityEngine.Object.DestroyImmediate(parcel.gameObject);Fail(job,error);return;}
        parcel.boxId="EIGHT-"+loader.name.Last()+"-"+(job.requested+1).ToString("D2")+"-"+direction;parcel.name=parcel.boxId;
        parcel.transform.SetPositionAndRotation(p,r);var body=parcel.GetComponent<Rigidbody>();body.position=p;body.rotation=r;Physics.SyncTransforms();
        job.pending=parcel;job.requested++;record.generated=job.requested;record.nextAxis=direction.ToString();job.since=EditorApplication.timeSinceStartup;
        if(!loader.TryLoadBox(parcel.GetComponent<BoxCollider>(),out error))Fail(job,error);
    }
    static void Finish(string error)
    {
        Write(true,error);SessionState.SetBool(Key,false);Application.runInBackground=oldBackground;
        Debug.Log("[Eight Box Check] "+(error??"Finished")+". See results/eight_box_check/latest.json");
        // Keep the actual stack visible for inspection without further physics movement.
        EditorApplication.isPaused=true;
    }
}
