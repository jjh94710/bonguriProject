using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Explicit conveyor/LoadSequence retry and full-tote check. Scene overrides apply only in Play.</summary>
[InitializeOnLoad]
public static class BonguriLoadSequenceCheck
{
    const string Key="Bonguri.LoadSequenceCheck";
    static bool started,oldBackground;
    static double began,nextWrite;
    static Job[] jobs;
    static BoxInfo prefab;
    static readonly System.Collections.Generic.List<string> events=new System.Collections.Generic.List<string>();
    static void Log(string message,string stack,LogType type)
    {
        if(message.StartsWith("[Cell")||message.StartsWith("[Conveyor")||message.StartsWith("[Pickup Path]")) events.Add(Time.time.ToString("F3")+" "+message);
        if(message.Contains("SEQ-A-01")&&message.Contains("적재 재시도"))
        {
            var job=jobs.Single(j=>j.loader.name.EndsWith("A"));
            var edit=new SerializedObject(job.sequence);edit.FindProperty("loadTimeLimit").floatValue=180;edit.ApplyModifiedPropertiesWithoutUndo();
            job.record.automaticRetryPassed=true;
        }
    }
    sealed class Job
    {
        public BoxAttacher loader;public LoadSequence sequence;public ConveyorLine line;public BoxInfo pending;
        public bool stableChecked,waitingFull,emptied,manualRetry;public double fullSince;
        public double since,settleAfter;public int requested;public bool done;
        public Record record;
    }
    [Serializable] public sealed class Record
    {
        public string robot,status,error="",nextAxis;
        public int generated,placed;
        public bool complete,passed,stable,fullRejected,fullResumed,manualRetryPassed,automaticRetryPassed;
        public int sequenceLoaded,emptied;
        public float maximumOverlap;
        public Vector3[] positions;
    }
    [Serializable] sealed class Report{public bool complete,passed;public float elapsed;public string error;public Record[] robots;}
    static BonguriLoadSequenceCheck(){EditorApplication.update+=Tick;}
    [MenuItem("Tools/Bonguri/Integration/Verify LoadSequence Transfer Retry And Full Totes")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        started=false;jobs=null;
        SessionState.SetBool(Key,true);EditorApplication.isPaused=false;EditorApplication.isPlaying=true;
    }
    static void Write(bool complete,string error=null)
    {
        Directory.CreateDirectory("results/load_sequence_check");
        File.WriteAllLines("results/load_sequence_check/events.log",events);
        var records=jobs==null?new Record[0]:jobs.Select(j=>j.record).ToArray();
        File.WriteAllText("results/load_sequence_check/latest.json",JsonUtility.ToJson(new Report{
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
                events.Clear();Application.logMessageReceived+=Log;
                started=true;began=EditorApplication.timeSinceStartup;nextWrite=0;
                oldBackground=Application.runInBackground;Application.runInBackground=true;
                prefab=UnityEngine.Object.FindFirstObjectByType<BoxFeeder>().ParcelPrefab;
                var loaders=UnityEngine.Object.FindObjectsByType<BoxAttacher>(FindObjectsSortMode.None).Where(l=>l.name=="fr5_suctionA"||l.name=="fr5_suctionB"||l.name=="fr5_suctionC").OrderBy(l=>l.name).ToArray();
                if(loaders.Length!=3)throw new Exception("Expected A/B/C loading robots.");
                jobs=loaders.Select(l=>new Job{loader=l,sequence=l.GetComponent<LoadSequence>(),line=UnityEngine.Object.FindObjectsByType<ConveyorLine>(FindObjectsSortMode.None).Single(c=>c.name=="Terminal"+l.name.Last()),record=new Record{robot=l.name}}).ToArray();
                foreach(var j in jobs)
                    if(!j.loader.IsReady||j.loader.Busy||j.loader.Attached||j.loader.tote.Count!=0||j.line.QueueCount!=0||j.loader.tote.Capacity!=8)
                        throw new Exception(j.loader.name+": start with an empty, idle 8-slot tote.");
                foreach(var j in jobs)
                {
                    // Standalone sequence exercise: bypass upstream only; use real conveyor Feed and real loading.
                    if(j.sequence==null||!j.sequence.ValidateConfiguration(out var why))throw new Exception("Missing validated sequence.");
                    var edit=new SerializedObject(j.sequence);edit.FindProperty("cell").objectReferenceValue=null;
                    if(j.loader.name.EndsWith("A"))edit.FindProperty("loadTimeLimit").floatValue=0; // first approach times out before attachment
                    edit.ApplyModifiedPropertiesWithoutUndo();
                    var laneEdit=new SerializedObject(j.line);laneEdit.FindProperty("speed").floatValue=1;laneEdit.FindProperty("lateralRange").floatValue=0;laneEdit.FindProperty("stopErrorZ").floatValue=0;laneEdit.ApplyModifiedPropertiesWithoutUndo();
                    j.sequence.StartLoading();
                }
            }
            EditorApplication.QueuePlayerLoopUpdate();
            foreach(var job in jobs.Where(j=>!j.done))Step(job);
            if(jobs.All(j=>j.done)){Finish(null);return;}
            if(EditorApplication.timeSinceStartup-began>1500){Finish("LoadSequence check timed out.");return;}
            if(EditorApplication.timeSinceStartup>=nextWrite){Write(false);nextWrite=EditorApplication.timeSinceStartup+1;}
        }
        catch(Exception e){Finish(e.ToString());}
    }
    static void Step(Job job)
    {
        var loader=job.loader;var tote=loader.tote;var record=job.record;
        record.status=job.sequence.Status+" / "+loader.Status;record.placed=tote.Count;record.sequenceLoaded=job.sequence.LoadedCount;record.emptied=job.sequence.EmptiedCount;
        if(loader.Busy)
        {
            if(EditorApplication.timeSinceStartup-job.since>180){loader.StopLoading();Fail(job,"Loading one parcel exceeded 180 seconds.");}
            return;
        }
        if(job.pending!=null)
        {
            var box=job.pending.GetComponent<BoxCollider>();
            if(!loader.PlacedBoxes.Contains(box))
            {
                if(job.requested==1&&loader.name.EndsWith("B")&&!job.manualRetry&&job.sequence.Status.Contains("작업자 확인"))
                {
                    if(!job.pending.GetComponent<Rigidbody>().isKinematic||job.sequence.LoadedCount!=0)throw new Exception("Failed parcel moved/counted.");
                    job.pending.destination=Destination.B;job.sequence.RetryFailed();job.manualRetry=true;record.manualRetryPassed=true;
                }
                if(job.requested==9&&!job.emptied&&job.sequence.Status.Contains("가득 참"))
                {
                    if(!job.waitingFull){job.waitingFull=true;job.fullSince=EditorApplication.timeSinceStartup;}
                    if(EditorApplication.timeSinceStartup-job.fullSince<1)return;
                    record.fullRejected=!loader.Busy&&tote.Count==8&&job.line.CurrentBox==job.pending&&job.pending.GetComponent<Rigidbody>().isKinematic&&job.sequence.LoadedCount==8;
                    if(!record.fullRejected)throw new Exception("Full tote must retain ninth parcel without loading/counting.");
                    job.sequence.EmptyContainer();job.emptied=true;
                }
                if(EditorApplication.timeSinceStartup-job.since>240){Fail(job,"Sequence parcel timeout: "+record.status);}
                return;
            }
            if(loader.Attached||!loader.LastLoadSucceeded||job.sequence.LoadedCount!=job.requested)return;
            if(job.line.CurrentBox==job.pending)throw new Exception("Loaded parcel still reserves line.");
            Debug.Log("[Sequence Check] "+loader.name+" completed "+job.requested);
            job.pending=null;job.settleAfter=EditorApplication.timeSinceStartup+1;
        }
        if(EditorApplication.timeSinceStartup<job.settleAfter)return;
        if(job.requested==9)
        {
            record.fullResumed=job.emptied&&tote.Count==1&&job.sequence.LoadedCount==9&&job.sequence.EmptiedCount==8;
            record.passed=record.stable&&record.fullRejected&&record.fullResumed&&record.maximumOverlap<.002f&&
                (!loader.name.EndsWith("A")||record.automaticRetryPassed)&&(!loader.name.EndsWith("B")||record.manualRetryPassed);
            record.complete=true;job.done=true;
            if(!record.passed)record.error="Sequence retry, stability or full/resume check failed.";
            return;
        }
        if(tote.Count==8&&!job.stableChecked)
        {
            var boxes=tote.Boxes;record.positions=boxes.Select(b=>b.bounds.center).ToArray();
            record.stable=boxes.Select((b,i)=>tote.IsSettledAt(i,b)).All(ok=>ok);
            for(int i=0;i<8;i++)for(int j=i+1;j<8;j++)
                if(Physics.ComputePenetration(boxes[i],boxes[i].transform.position,boxes[i].transform.rotation,boxes[j],boxes[j].transform.position,boxes[j].transform.rotation,out _,out float depth))record.maximumOverlap=Mathf.Max(record.maximumOverlap,depth);
            job.stableChecked=true;
        }
        var direction=(BonguriIntegrationPanel.PickupDirection)(job.requested%3);
        var rotation=BonguriIntegrationPanel.PickupRotation(direction);
        var parcel=UnityEngine.Object.Instantiate(prefab,new Vector3(100,100,100),rotation);
        parcel.GetComponent<Rigidbody>().isKinematic=true;
        parcel.boxId="SEQ-"+loader.name.Last()+"-"+(job.requested+1).ToString("D2")+"-"+direction;parcel.name=parcel.boxId;
        parcel.destination=(Destination)(loader.name.Last()-'A'+(int)Destination.A);
        if(loader.name.EndsWith("B")&&job.requested==0)parcel.destination=Destination.NG;
        job.pending=parcel;job.requested++;record.generated=job.requested;record.nextAxis=direction.ToString();job.since=EditorApplication.timeSinceStartup;
        if(!job.line.Feed(parcel))Fail(job,"Conveyor Feed rejected parcel.");
    }
    static void Finish(string error)
    {
        Application.logMessageReceived-=Log;
        Write(true,error);SessionState.SetBool(Key,false);Application.runInBackground=oldBackground;
        Debug.Log("[LoadSequence Check] "+(error??"Finished")+". See results/load_sequence_check/latest.json");
        // Pause to preserve the final test state for inspection; stop Play to restore scene references.
        EditorApplication.isPaused=true;
    }
}
