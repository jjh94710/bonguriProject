using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Unity.InferenceEngine;

/// <summary>실제 Unity 렌더 → OCR ONNX → FR-07 결과를 고정 시드로 기록한다.</summary>
public static class LabelReaderCheck
{
    [MenuItem("Tools/Factory ML/OCR 판독 및 규칙 검증")]
    static void Check()
    {
        if(Application.isPlaying){Debug.LogError("Play 정지 후 실행하세요.");return;}
        foreach(string kind in new[]{"Number","Region"})
        {
            string source="results/label_reader/Label"+kind+".onnx";
            if(!File.Exists(source)){Debug.LogError("scripts/train_label_reader.py를 먼저 실행하세요.");return;}
            string target="Assets/FactoryMLModels/Label"+kind+".onnx";
            File.Copy(source,target,true);AssetDatabase.ImportAsset(target,ImportAssetOptions.ForceSynchronousImport);
        }
        GameObject root=null,box=null;LabelReader reader=null;
        var layout=AssetDatabase.LoadAssetAtPath<LabelLayout>("Assets/_Factory/LabelLayout.asset");
        var settings=AssetDatabase.LoadAssetAtPath<CellSettings>("Assets/_Factory/CellSettings.asset");
        var report=new Report();var csv=new StringBuilder("sample,truth,region,prediction,predicted_region,unknown,check\n");
        string folder="results/label_reader_unity";Directory.CreateDirectory(folder);
        var randomState=UnityEngine.Random.state;UnityEngine.Random.InitState(20261107);
        try
        {
            root=new GameObject("OCR validation (temporary)"){hideFlags=HideFlags.HideAndDontSave};
            box=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/Process/ParcelBox_6x8x12cm_Process.prefab"));
            box.hideFlags=HideFlags.HideAndDontSave;box.transform.position=new Vector3(10,0,10);
            foreach(var rb in box.GetComponentsInChildren<Rigidbody>()){rb.isKinematic=true;rb.position=rb.transform.position;rb.rotation=rb.transform.rotation;}
            foreach(var renderer in box.GetComponentsInChildren<Renderer>()){var block=new MaterialPropertyBlock();block.SetFloat("_FaceCaptureDynamic",1);renderer.SetPropertyBlock(block);}
            SampleBox.SetLayerRecursively(box,LayerMask.NameToLayer(SampleBox.DefaultLayerName));
            var info=box.GetComponent<BoxInfo>();
            var nc=new GameObject("OCR number camera").AddComponent<Camera>();nc.transform.SetParent(root.transform);nc.enabled=false;
            nc.clearFlags=CameraClearFlags.SolidColor;nc.backgroundColor=Color.gray;nc.cullingMask=1<<box.layer;nc.allowHDR=false;nc.allowMSAA=false;
            var rc=new GameObject("OCR region camera").AddComponent<Camera>();rc.transform.SetParent(root.transform);rc.enabled=false;
            LabelReaderDataset.Calibrate(layout,info,nc,32,"서울");
            EditorUtility.SetDirty(layout);AssetDatabase.SaveAssetIfDirty(layout);
            reader=root.AddComponent<LabelReader>();var edit=new SerializedObject(reader);
            Set(edit,"layout",layout);Set(edit,"settings",settings);Set(edit,"numberCamera",nc);Set(edit,"regionCamera",rc);
            Set(edit,"numberModel",AssetDatabase.LoadAssetAtPath<ModelAsset>("Assets/FactoryMLModels/LabelNumber.onnx"));
            Set(edit,"regionModel",AssetDatabase.LoadAssetAtPath<ModelAsset>("Assets/FactoryMLModels/LabelRegion.onnx"));edit.ApplyModifiedPropertiesWithoutUndo();
            var rigObject=new GameObject("Reader presence rig");rigObject.transform.SetParent(root.transform);
            var rig=rigObject.AddComponent<FaceCameraRig>();var rigEdit=new SerializedObject(rig);
            rigEdit.FindProperty("cullingMask").intValue=1<<box.layer;rigEdit.ApplyModifiedPropertiesWithoutUndo();
            var presence=rigObject.AddComponent<LabelPresenceDetector>();var presenceEdit=new SerializedObject(presence);
            Set(presenceEdit,"model",AssetDatabase.LoadAssetAtPath<ModelAsset>("Assets/FactoryMLModels/LabelPresence.onnx"));presenceEdit.ApplyModifiedPropertiesWithoutUndo();
            edit.Update();Set(edit,"presence",presence);Set(edit,"faceRig",rig);edit.ApplyModifiedPropertiesWithoutUndo();
            var rng=new System.Random(20261107);
            for(int i=0;i<50;i++)
            {
                int r=i%3;string number=settings.regionCodes[r].ToString("D2")+rng.Next(1000).ToString("D3");string region=settings.regionNames[r];
                layout.Print(info,number,region);
                // 정답 데이터를 제거해도 같은 픽셀로 판독해야 한다.
                info.labelNumber="DO NOT READ";info.labelRegion="DO NOT READ";
                if(!reader.TryReadOnce(info,true,out string error))throw new InvalidOperationException(error);
                report.total++;if(info.readText==number)report.fullCorrect++;
                if(info.readText.Length>=2 && info.readText.Substring(0,2)==number.Substring(0,2))report.prefixCorrect++;
                if(info.readRegion==region)report.regionCorrect++;
                if(info.unknownCount>0)report.unknown++;
                LabelValidator.Apply(info,settings);
                csv.AppendLine($"{i},{number},{region},{info.readText},{info.readRegion},{info.unknownCount},{info.check}");
                if(i<5){layout.AimAtNumber(nc,info.label,false);var shot=LabelReaderDataset.Shoot(nc,186,32);File.WriteAllBytes(folder+"/number_"+i+".png",shot.EncodeToPNG());UnityEngine.Object.DestroyImmediate(shot);}
            }
            string[] numbers={"11137","22137","33137","23137","12137","11137","2213","22A37","２２１３７"};
            string[] regions={"서울","대전","부산","서울","대전","부산","대전","대전","대전"};
            LabelCheck[] expected={LabelCheck.OK,LabelCheck.OK,LabelCheck.OK,LabelCheck.RegionError,LabelCheck.RegionError,LabelCheck.AddressMismatch,LabelCheck.DigitError,LabelCheck.DigitError,LabelCheck.DigitError};
            for(int i=0;i<numbers.Length;i++){report.ruleTests++;if(LabelValidator.Validate(numbers[i],regions[i],settings,out _,out _)==expected[i])report.rulePassed++;}
            // 빈 영상은 확실한 번호로 받아들이면 안 된다.
            info.numberText.gameObject.SetActive(false);info.regionText.gameObject.SetActive(false);
            if(!reader.TryReadOnce(info,false,out string blankError))throw new InvalidOperationException(blankError);
            report.blankRejected=info.unknownCount>0;
            info.numberText.gameObject.SetActive(true);info.regionText.gameObject.SetActive(true);
            if(!FaceJudgeAudit.TryGetPaperNormal(box,info.label.gameObject,out Vector3 normal))throw new InvalidOperationException("종이 방향을 찾지 못했습니다.");
            box.transform.rotation=Quaternion.FromToRotation(normal,Vector3.up)*box.transform.rotation;
            foreach(var rb in box.GetComponentsInChildren<Rigidbody>()){rb.position=rb.transform.position;rb.rotation=rb.transform.rotation;}
            Physics.SyncTransforms();
            string[] readNumbers={"22137","23137","12137","11137","2213","22137","22137"};
            string[] readRegions={"대전","서울","대전","부산","대전","대전","대전"};
            ExceptionReason[] reasons={ExceptionReason.None,ExceptionReason.RuleViolation,ExceptionReason.RuleViolation,ExceptionReason.RuleViolation,ExceptionReason.ReadFail,ExceptionReason.ReadFail,ExceptionReason.NoLabel};
            var full=new StringBuilder("truth,region,read,read_region,reason,retries,passed\n");
            for(int i=0;i<readNumbers.Length;i++)
            for(int repeat=0;repeat<(i>=1 && i<=4?3:1);repeat++)
            {
                info.reason=ExceptionReason.None;info.destination=Destination.None;info.label.gameObject.SetActive(true);
                info.numberText.gameObject.SetActive(true);info.regionText.gameObject.SetActive(true);layout.Print(info,readNumbers[i],readRegions[i]);
                if(i==5){info.numberText.gameObject.SetActive(false);info.regionText.gameObject.SetActive(false);}
                if(i==6)info.label.gameObject.SetActive(false);
                info.labelNumber="DO NOT READ";info.labelRegion="DO NOT READ";
                var routine=reader.Read(info);while(routine.MoveNext()){}
                report.maxReadMilliseconds=Math.Max(report.maxReadMilliseconds,reader.LastMilliseconds);
                bool ok=info.reason==reasons[i] && (i!=0 || info.destination==Destination.B) && (i!=5 || info.retryCount==2);
                report.readTests++;if(ok)report.readPassed++;
                full.AppendLine($"{readNumbers[i]},{readRegions[i]},{info.readText},{info.readRegion},{info.reason},{info.retryCount},{ok}");
            }
            File.WriteAllText(folder+"/read_pipeline.csv",full.ToString(),Encoding.UTF8);
            // 공정에서 배치할 수 있는 연결 완료 프리팹. 교정 카메라와 별도 리그다.
            reader.Dispose();rigObject.transform.localPosition=Vector3.zero;rig.BuildRig();root.hideFlags=HideFlags.None;root.name="LabelReaderStation";root.transform.position=Vector3.zero;
            PrefabUtility.SaveAsPrefabAsset(root,"Assets/prefabs/Process/LabelReaderStation.prefab");
            report.passed=report.prefixCorrect>=49 && report.fullCorrect>=48 && report.rulePassed==report.ruleTests && report.blankRejected && report.readTests==report.readPassed;
        }
        catch(Exception e){report.error=e.ToString();Debug.LogException(e);}
        finally
        {
            reader?.Dispose();if(root!=null)foreach(var detector in root.GetComponentsInChildren<LabelPresenceDetector>(true))detector.Release();
            if(box!=null)UnityEngine.Object.DestroyImmediate(box);if(root!=null)UnityEngine.Object.DestroyImmediate(root);UnityEngine.Random.state=randomState;
            File.WriteAllText(folder+"/latest.json",JsonUtility.ToJson(report,true));File.WriteAllText(folder+"/predictions.csv",csv.ToString(),Encoding.UTF8);
        }
        Debug.Log("[OCR 검증] "+JsonUtility.ToJson(report));
    }
    static void Set(SerializedObject edit,string name,UnityEngine.Object value)=>edit.FindProperty(name).objectReferenceValue=value;
    [Serializable] class Report {public int total,fullCorrect,prefixCorrect,regionCorrect,unknown,ruleTests,rulePassed,readTests,readPassed;public double maxReadMilliseconds;public bool blankRejected,passed;public string error;}
}
