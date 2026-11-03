using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Explicit Play check of the real CellController/feeder/stations. Does not fake placement.</summary>
[InitializeOnLoad]
public static class BonguriTerminalLoadingCheck
{
    const string Key = "Bonguri.TerminalLoadingCheck";
    static CellController cell;
    static BoxFeeder feeder;
    static bool started;
    static double began;
    static float top, side, bottom, interval;
    static readonly HashSet<string> blocked = new HashSet<string>();
    static readonly List<string> events = new List<string>();
    static ConveyorLine reservedLane;
    static BoxInfo reservedBox, follower;
    static Vector3 originalPosition;
    static float originalSpeed, reservationStarted;
    static bool reservationChecking, reservationPassed;
    static bool routeOnly;
    static float nextSample;
    static readonly List<string> trajectory = new List<string>();
    static Vector3? previousBPosition;
    static float maximumBReverseStep;
    static int bDirectionSamples;
    static BonguriTerminalLoadingCheck()
    {
        if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick;
    }
    [MenuItem("Tools/Bonguri/Integration/Verify Terminal Loading Handoff")]
    public static void Run()
    { Begin(false); }
    [MenuItem("Tools/Bonguri/Integration/Verify Classified Box Conveyor Routes")]
    public static void RunRouteCheck()
    { Begin(true); }
    static void Begin(bool routeCheck)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        cell = UnityEngine.Object.FindFirstObjectByType<CellController>();
        if (cell == null || !cell.ValidateTerminalConfiguration(out _)) throw new InvalidOperationException("Connect and validate terminal loading first.");
        SessionState.SetBool(Key, true);
        SessionState.SetBool(Key+".Route",routeCheck);
        started = false;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }
    static void Log(string message, string stack, LogType type)
    {
        if (message.StartsWith("[Cell") || message.StartsWith("[Feeder") || message.StartsWith("[Conveyor") || message.StartsWith("[Pusher")) events.Add(Time.time.ToString("F3") + " " + message);
        if (message.StartsWith("[Cell Transfer]") && message.Contains("최종 적재 지점 미연결")) blocked.Add(message.Split('|')[1].Trim().Split(' ')[0]);
    }
    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        if (Time.time < 1) return;
        try
        {
            if (!started)
            {
                started = true; began = EditorApplication.timeSinceStartup; blocked.Clear(); events.Clear();
                reservationChecking=false;reservationPassed=false;
                routeOnly=SessionState.GetBool(Key+".Route",false);nextSample=0;trajectory.Clear();
                previousBPosition=null;maximumBReverseStep=0;bDirectionSamples=0;
                trajectory.Add("time,id,state,destination,x,y,z");
                cell = UnityEngine.Object.FindFirstObjectByType<CellController>();
                feeder = UnityEngine.Object.FindFirstObjectByType<BoxFeeder>();
                Application.logMessageReceived += Log;
                if (!cell.ValidateTerminalConfiguration(out string error)) throw new InvalidOperationException(error);
                foreach (var loader in UnityEngine.Object.FindObjectsByType<BoxAttacher>(FindObjectsSortMode.None))
                    if (loader.TryLoadBox(null, out _) || loader.Busy) throw new InvalidOperationException("Invalid parcel must not start loading.");
                var s=cell.Settings;top=s.faceTop;side=s.faceSide;bottom=s.faceBottom;interval=s.feedInterval;
                s.faceTop=1;s.faceSide=0;s.faceBottom=0;s.feedInterval=3;
                UnityEngine.Random.InitState(20261103);
                if(routeOnly)
                {
                    // Isolate conveyor arrival from pickup. Play exit restores these scene-only overrides.
                    var edit=new SerializedObject(cell);edit.FindProperty("terminalStations").arraySize=0;edit.ApplyModifiedPropertiesWithoutUndo();
                    foreach(var line in UnityEngine.Object.FindObjectsByType<ConveyorLine>(FindObjectsSortMode.None).Where(l=>l.UsesSurfacePath))
                    {var lane=new SerializedObject(line);lane.FindProperty("speed").floatValue=1;lane.ApplyModifiedPropertiesWithoutUndo();}
                }
                cell.Run(3);
            }
            if(routeOnly && Time.time>=nextSample)
            {
                nextSample=Time.time+.5f;
                foreach(var box in UnityEngine.Object.FindObjectsByType<BoxInfo>(FindObjectsSortMode.None).Where(b=>!string.IsNullOrEmpty(b.boxId)))
                {
                    var p=box.transform.position;
                    trajectory.Add(FormattableString.Invariant($"{Time.time:F3},{box.boxId},{box.state},{box.destination},{p.x:F4},{p.y:F4},{p.z:F4}"));
                }
            }
            if(routeOnly)
            {
                var terminalLines=UnityEngine.Object.FindObjectsByType<ConveyorLine>(FindObjectsSortMode.None).Where(l=>l.name=="TerminalA"||l.name=="TerminalB"||l.name=="TerminalC").ToArray();
                var bLine=terminalLines.FirstOrDefault(l=>l.name=="TerminalB");
                if(bLine!=null && bLine.QueueCount>0)
                {
                    var b=UnityEngine.Object.FindObjectsByType<BoxInfo>(FindObjectsSortMode.None).FirstOrDefault(box=>box.destination==Destination.B);
                    if(b!=null)
                    {
                        var p=ConveyorLine.PositionOf(b);
                        var edit=new SerializedObject(bLine);var entry=(Transform)edit.FindProperty("entry").objectReferenceValue;
                        var direction=Vector3.ProjectOnPlane(bLine.StopPoint.position-entry.position,Vector3.up).normalized;
                        if(previousBPosition.HasValue)
                        {maximumBReverseStep=Mathf.Max(maximumBReverseStep,-Vector3.Dot(p-previousBPosition.Value,direction));bDirectionSamples++;}
                        previousBPosition=p;
                        if(maximumBReverseStep>.001f){Finish("B parcel reversed after conveyor handoff.");return;}
                    }
                }
                if(terminalLines.Length==3 && terminalLines.All(l=>l.HasBoxAtStop)){Finish(null);return;}
                if(EditorApplication.timeSinceStartup-began>90){Finish("Conveyor route check timed out.");return;}
                return;
            }
            if(reservationChecking)
            {
                if(Time.time-reservationStarted<1.2f)return;
                reservationPassed=reservedLane.CurrentBox==reservedBox && Vector3.Distance(follower.transform.position,reservedLane.StopPoint.position)>.07f;
                RestoreReservation();
                Finish(reservationPassed?null:"Loading station reservation was overwritten by a following box.");
            }
            else if (blocked.Count >= 3) StartReservationCheck();
            else if (cell.Processed >= 3) Finish(null);
            else if (EditorApplication.timeSinceStartup - began > 200) Finish("Handoff check timed out.");
        }
        catch(Exception e) { Finish(e.ToString()); }
    }
    static void StartReservationCheck()
    {
        reservedLane=UnityEngine.Object.FindObjectsByType<ConveyorLine>(FindObjectsSortMode.None).Single(l=>l.name=="TerminalA");
        reservedBox=reservedLane.CurrentBox;originalPosition=reservedBox.transform.position;
        var edit=new SerializedObject(reservedLane);originalSpeed=edit.FindProperty("speed").floatValue;edit.FindProperty("speed").floatValue=3;edit.ApplyModifiedPropertiesWithoutUndo();
        // Simulate the reserved parcel being carried beyond the stop; the next box must still wait.
        Vector3 carried=originalPosition+Vector3.back+Vector3.up*.3f;
        reservedBox.transform.position=carried;reservedBox.GetComponent<Rigidbody>().position=carried;
        follower=UnityEngine.Object.Instantiate(reservedBox);follower.boxId="CHECK-FOLLOWER";
        if(!reservedLane.Feed(follower))throw new InvalidOperationException("Cannot feed reservation-check follower.");
        reservationChecking=true;reservationStarted=Time.time;
    }
    static void RestoreReservation()
    {
        if(reservedBox!=null){reservedBox.transform.position=originalPosition;reservedBox.GetComponent<Rigidbody>().position=originalPosition;}
        if(reservedLane!=null)
        {
            if(follower!=null)reservedLane.TakeAway(follower);
            var edit=new SerializedObject(reservedLane);edit.FindProperty("speed").floatValue=originalSpeed;edit.ApplyModifiedPropertiesWithoutUndo();
        }
        if(follower!=null)UnityEngine.Object.DestroyImmediate(follower.gameObject);
        follower=null;reservationChecking=false;
    }
    static void Finish(string error)
    {
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= Log;
        SessionState.SetBool(Key,false);
        SessionState.EraseBool(Key+".Route");
        if(reservationChecking)RestoreReservation();
        var boxes=UnityEngine.Object.FindObjectsByType<BoxInfo>(FindObjectsSortMode.None).Where(b=>!string.IsNullOrEmpty(b.boxId)).ToArray();
        var lanes=UnityEngine.Object.FindObjectsByType<ConveyorLine>(FindObjectsSortMode.None).Where(l=>l.name.StartsWith("Terminal")).ToArray();
        bool guardPassed=blocked.Count==3 && cell.Processed==0 && boxes.Length==3 && boxes.All(b=>!b.IsFinished && b.state==BoxState.Transferring && b.GetComponent<Rigidbody>().isKinematic) && lanes.All(l=>l.CurrentBox!=null) && !events.Any(e=>e.Contains("[Cell Loading]")||e.Contains("적재 실패"));
        bool physicalPassed=cell.Processed==3 && cell.Success==3;
        if(routeOnly)
        {
            var routes=lanes.OrderBy(l=>l.name).Select(l=>{
                var edit=new SerializedObject(l);
                var entry=(Transform)edit.FindProperty("entry").objectReferenceValue;
                var exit=(Transform)edit.FindProperty("exit").objectReferenceValue;
                var next=(ConveyorLine)edit.FindProperty("nextLine").objectReferenceValue;
                var loader=UnityEngine.Object.FindObjectsByType<BoxAttacher>(FindObjectsSortMode.None).Single(a=>a.name=="fr5_suction"+l.name.Last());
                return new Route {line=l.name,entry=entry.position,exit=exit.position,stop=l.StopPoint.position,nextLine=next==null?"None":next.name,
                    box=l.CurrentBox==null?"None":l.CurrentBox.boxId,boxPosition=l.CurrentBox==null?Vector3.zero:l.CurrentBox.transform.position,
                    robot=loader.name,robotPosition=loader.transform.position,distanceToRobot=l.CurrentBox==null?-1:Vector3.Distance(l.CurrentBox.transform.position,loader.transform.position)};
            }).ToArray();
            Directory.CreateDirectory("results/conveyor_route_check");
            File.WriteAllText("results/conveyor_route_check/latest.json",JsonUtility.ToJson(new RouteReport {passed=error==null&&routes.Length==3&&routes.All(r=>r.box!="None")&&bDirectionSamples>0&&maximumBReverseStep<=.001f,bDirectionSamples=bDirectionSamples,maximumBReverseStep=maximumBReverseStep,fed=feeder.FedCount,processed=cell.Processed,error=error??"",routes=routes},true));
            File.WriteAllLines("results/conveyor_route_check/trajectory.csv",trajectory);
            File.WriteAllLines("results/conveyor_route_check/events.log",events);
            RestoreSettings();
            Debug.Log("[Conveyor Route Check] Reached "+routes.Count(r=>r.box!="None")+"/3 terminal stops. See results/conveyor_route_check.");
            EditorApplication.isPlaying=false;
            return;
        }
        Directory.CreateDirectory("results/terminal_loading_handoff");
        File.WriteAllText("results/terminal_loading_handoff/latest.json",JsonUtility.ToJson(new Report{
            passed=error==null&&((guardPassed&&reservationPassed)||physicalPassed),configurationPassed=true,transferWaitPassed=guardPassed,stopReservationPassed=reservationPassed,physicalLoadingPassed=physicalPassed,
            fed=feeder==null?0:feeder.FedCount,processed=cell==null?0:cell.Processed,error=error??"",detail=guardPassed?"Three real feeder boxes reached the branch checkpoints. All await transfer to the final pickup points; loading was not attempted and no loading error or completion was emitted.":"See events.log."},true));
        File.WriteAllLines("results/terminal_loading_handoff/events.log",events);
        RestoreSettings();
        Debug.Log("[Terminal Handoff Check] guard="+guardPassed+", physicalLoading="+physicalPassed+", error="+error);
        EditorApplication.isPlaying=false;
    }
    static void RestoreSettings()
    {if(cell!=null && cell.Settings!=null){var s=cell.Settings;s.faceTop=top;s.faceSide=side;s.faceBottom=bottom;s.feedInterval=interval;}}
    [Serializable] class RouteReport { public bool passed;public int fed,processed,bDirectionSamples;public float maximumBReverseStep;public string error;public Route[] routes; }
    [Serializable] class Route {public string line,nextLine,box,robot;public Vector3 entry,exit,stop,boxPosition,robotPosition;public float distanceToRobot;}
    [Serializable] class Report
    {
        public bool passed,configurationPassed,transferWaitPassed,stopReservationPassed,physicalLoadingPassed;
        public int fed,processed;
        public string error,detail;
    }
}

// Records actual suction and lift separately from the overall placement result.
[InitializeOnLoad]
public static class BonguriPickupCheck
{
    const string Key="Bonguri.PickupCheck";
    static CellController cell;
    static BoxAttacher[] loaders;
    static Item[] items;
    static double began,nextRecord;
    static bool started;
    static readonly List<string> events=new List<string>();
    static readonly HashSet<BoxInfo> oriented=new HashSet<BoxInfo>();
    [Serializable] sealed class Item
    {
        public string robot,box,status,ikStatus;
        public bool attempted,attached,lifted,placed,busy;
        public float initialBottom,maxLift;
    }
    [Serializable] sealed class Report{public bool complete;public string error;public int processed;public Item[] robots;}
    static BonguriPickupCheck(){EditorApplication.update+=Tick;}
    [MenuItem("Tools/Bonguri/Integration/Verify Actual Suction Pickup")]
    public static void Run() => Run(false);
    public static void Run(bool uniformX)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        SessionState.SetBool("Bonguri.TerminalLoadingCheck",false);
        SessionState.SetBool(Key+".UniformX",uniformX);
        SessionState.SetBool(Key+".DirectZ",false);
        SessionState.SetBool(Key,true);started=false;EditorApplication.isPlaying=true;
    }
    public static void RunDirectZ(bool all=false){Run(false);SessionState.SetBool(Key+".DirectZ",true);SessionState.SetBool(Key+".AllRobots",all);}
    static void Log(string message,string stack,LogType type)
    {if(message.StartsWith("[Cell")||message.StartsWith("[Conveyor")||message.StartsWith("[Pusher")||message.StartsWith("[Pickup Path]"))events.Add(Time.time.ToString("F3")+" "+message);}
    static void Write(bool complete,string error)
    {
        Directory.CreateDirectory("results/pickup_check");
        File.WriteAllText("results/pickup_check/latest.json",JsonUtility.ToJson(new Report{complete=complete,error=error??"",processed=cell==null?0:cell.Processed,robots=items},true));
        File.WriteAllLines("results/pickup_check/events.log",events);
    }
    static void Tick()
    {
        if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||Time.time<2)return;
        try
        {
            if(!started)
            {
                started=true;began=EditorApplication.timeSinceStartup;nextRecord=0;events.Clear();oriented.Clear();Application.logMessageReceived+=Log;
                cell=UnityEngine.Object.FindFirstObjectByType<CellController>();
                if(!cell.ValidateTerminalConfiguration(out string error))throw new Exception(error);
                loaders=UnityEngine.Object.FindObjectsByType<BoxAttacher>(FindObjectsSortMode.None).Where(l=>l.name=="fr5_suctionA"||l.name=="fr5_suctionB"||l.name=="fr5_suctionC").OrderBy(l=>l.name).ToArray();
                if(loaders.Length!=3)throw new Exception("Expected three loading robots.");
                if(SessionState.GetBool(Key+".DirectZ",false)&&!SessionState.GetBool(Key+".AllRobots",false))loaders=loaders.Where(l=>l.name=="fr5_suctionC").ToArray();
                items=loaders.Select(l=>new Item{robot=l.name}).ToArray();
                var s=cell.Settings;s.faceTop=1;s.faceSide=0;s.faceBottom=0;s.feedInterval=3;
                foreach(var line in UnityEngine.Object.FindObjectsByType<ConveyorLine>(FindObjectsSortMode.None).Where(l=>l.UsesSurfacePath))
                {var edit=new SerializedObject(line);edit.FindProperty("speed").floatValue=1;edit.ApplyModifiedPropertiesWithoutUndo();}
                UnityEngine.Random.InitState(20261103);
                if(SessionState.GetBool(Key+".DirectZ",false))
                {
                    foreach(var loader in loaders)
                    {
                        var line=UnityEngine.Object.FindObjectsByType<ConveyorLine>(FindObjectsSortMode.None).Single(l=>l.name=="Terminal"+loader.name.Last());
                        var box=UnityEngine.Object.Instantiate(UnityEngine.Object.FindFirstObjectByType<BoxFeeder>().ParcelPrefab,new Vector3(100,100,100),Quaternion.identity);
                        var body=box.GetComponent<Rigidbody>();body.isKinematic=true;
                        if(!line.ValidateSurfacePath(out string why)||!line.TryGetSurfacePose(box,Quaternion.identity,line.PathLength,out var p,out var r,out why))throw new Exception(why);
                        box.boxId="DIRECT-Z-"+loader.name.Last();box.name=box.boxId;body.position=p;body.rotation=r;box.transform.SetPositionAndRotation(p,r);Physics.SyncTransforms();
                        if(!loader.TryLoadBox(box.GetComponent<BoxCollider>(),out why))throw new Exception(why);
                    }
                }
                else cell.Run(3);
            }
            // Controlled comparison only: every newly fed parcel has its suction face along X.
            if(SessionState.GetBool(Key+".UniformX",false))
                foreach(var box in UnityEngine.Object.FindObjectsByType<BoxInfo>(FindObjectsSortMode.None).Where(b=>!string.IsNullOrEmpty(b.boxId)))
                    if(oriented.Add(box))
                    {var rotation=Quaternion.Euler(0,90,0);var body=box.GetComponent<Rigidbody>();if(body!=null)body.rotation=rotation;box.transform.rotation=rotation;}
            for(int i=0;i<loaders.Length;i++)
            {
                var loader=loaders[i];var item=items[i];item.status=loader.Status;item.ikStatus=loader.armIK.Status;item.busy=loader.Busy;
                var box=loader.targetBox;if(box==null)continue;
                item.box=box.name;
                if(!item.attempted)item.initialBottom=box.bounds.min.y;
                item.attempted=true;
                item.attached|=loader.Attached;
                if(loader.Attached){item.maxLift=Mathf.Max(item.maxLift,box.bounds.min.y-item.initialBottom);item.lifted|=item.maxLift>.05f;}
                item.placed=loader.PlacedBoxes.Contains(box);
            }
            bool settled=SessionState.GetBool(Key+".DirectZ",false)||loaders.All(l=>l.targetBox==null||l.PlacedBoxes.Contains(l.targetBox)||l.targetBox.GetComponent<BoxInfo>().state!=BoxState.Loading);
            bool done=settled&&items.All(i=>i.attempted&&!i.busy)&&(SessionState.GetBool(Key+".DirectZ",false)||cell.Processed>=items.Count(i=>i.placed));
            bool timeout=EditorApplication.timeSinceStartup-began>(SessionState.GetBool(Key+".DirectZ",false)?600:150);
            if(done||timeout)
            {
                Write(true,timeout?"Pickup check timed out.":null);SessionState.SetBool(Key,false);Application.logMessageReceived-=Log;
                EditorApplication.isPlaying=false;return;
            }
            if(EditorApplication.timeSinceStartup>=nextRecord){Write(false,null);nextRecord=EditorApplication.timeSinceStartup+1;}
        }
        catch(Exception e){Write(true,e.ToString());SessionState.SetBool(Key,false);Application.logMessageReceived-=Log;EditorApplication.isPlaying=false;}
    }
}
