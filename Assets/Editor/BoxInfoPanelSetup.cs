using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;

[InitializeOnLoad]
public static class BoxInfoPanelSetup
{
 const string Request="Temp/BoxInfoPanelSetup.request";
 static BoxInfoPanelSetup(){EditorApplication.update+=Poll;}
 static void Poll(){if(!EditorApplication.isPlayingOrWillChangePlaymode&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating&&File.Exists("Temp/BoxInfoPanelMeshFix.request")){File.Delete("Temp/BoxInfoPanelMeshFix.request");FixMesh();}if(!EditorApplication.isPlayingOrWillChangePlaymode&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating&&File.Exists("Temp/BoxInfoPanelStyle.request")){File.Delete("Temp/BoxInfoPanelStyle.request");ApplyStyle();}if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists(Request))return;File.Delete(Request);if(UnityEngine.Object.FindObjectsByType<BoxInfoPanel>(FindObjectsSortMode.None).Any())Repair();else Setup();}
 static void FixMesh(){
  var panel=UnityEngine.Object.FindFirstObjectByType<BoxInfoPanel>();if(panel==null)return;
  var effects=panel.GetComponentsInChildren<Outline>(true);
  var report="Outline components: "+effects.Length+"\n";
  foreach(var effect in effects){report+=effect.gameObject.name+": enabled="+effect.enabled+"\n";Undo.RecordObject(effect,"UI 정점 수 오류 수정");effect.enabled=false;EditorUtility.SetDirty(effect);}
  foreach(var graphic in panel.GetComponentsInChildren<Graphic>(true))graphic.SetVerticesDirty();
  Canvas.ForceUpdateCanvases();
  EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);EditorSceneManager.SaveScene(panel.gameObject.scene);
  File.WriteAllText("Temp/BoxInfoPanelMeshFixResult.txt",report+"OK: outlines disabled; scene saved");
 }
 static void ApplyStyle(){
  var panel=UnityEngine.Object.FindFirstObjectByType<BoxInfoPanel>();if(panel==null)return;
  Undo.RecordObject(panel.GetComponent<Image>(),"패널 반투명 배경");
  var image=panel.GetComponent<Image>();var color=image.color;color.a=.55f;image.color=color;
  var accent=panel.transform.Find("Accent");if(accent!=null)Undo.DestroyObjectImmediate(accent.gameObject);
  EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);EditorSceneManager.SaveScene(panel.gameObject.scene);
  File.WriteAllText("Temp/BoxInfoPanelStyleResult.txt","OK: accent removed; background alpha 0.55");
 }
 static void Repair(){
  var panel=UnityEngine.Object.FindFirstObjectByType<BoxInfoPanel>();
  var boxes=UnityEngine.Object.FindObjectsByType<BoxInfo>(FindObjectsInactive.Include,FindObjectsSortMode.None);
  var box=boxes.FirstOrDefault(b=>b.name=="ParcelBox_GraspTest_horizontal")??boxes.FirstOrDefault(b=>b.name=="ParcelBox_ProcessVariant");
  foreach(var text in panel.GetComponentsInChildren<TMP_Text>())text.overflowMode=TextOverflowModes.Truncate;
  var so=new SerializedObject(panel);so.FindProperty("previewBox").objectReferenceValue=box;so.ApplyModifiedPropertiesWithoutUndo();panel.Refresh();
  EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);EditorSceneManager.SaveScene(panel.gameObject.scene);
  File.WriteAllText("Temp/BoxInfoPanelSetupResult.txt","OK: source="+(box!=null?box.name:"waiting")+"; BoxInfo objects="+boxes.Length);
 }
 [MenuItem("Tools/Bonguri/Setup Box Info Panel")]
 public static void Setup(){
  if(EditorApplication.isPlayingOrWillChangePlaymode)return;
  try{
   var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
   if(UnityEngine.Object.FindObjectsByType<BoxInfoPanel>(FindObjectsSortMode.None).Any())throw new Exception("BoxInfoPanel이 이미 있습니다.");
   string backup=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)+"/UnityBackups/bonguriProject/BoxInfoPanel/"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"/bongyuri.unity";
   Directory.CreateDirectory(Path.GetDirectoryName(backup));if(!EditorSceneManager.SaveScene(scene,backup,true))throw new Exception("씬 백업 실패");
   var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Scripts/UI/BoxInfoPanelFont.asset");
   if(font==null){
    var source=AssetDatabase.LoadAssetAtPath<Font>("Assets/Project/Parcels/Fonts/NotoSansKR-Regular.otf");
    if(source==null)throw new Exception("한글 폰트를 찾지 못했습니다.");
    font=TMP_FontAsset.CreateFontAsset(source);font.name="BoxInfoPanelFont";font.isMultiAtlasTexturesEnabled=true;
    AssetDatabase.CreateAsset(font,"Assets/_Scripts/UI/BoxInfoPanelFont.asset");
    if(font.material!=null&&!AssetDatabase.Contains(font.material))AssetDatabase.AddObjectToAsset(font.material,font);
    foreach(var atlas in font.atlasTextures)if(atlas!=null&&!AssetDatabase.Contains(atlas))AssetDatabase.AddObjectToAsset(atlas,font);
    font.TryAddCharacters("박스 정보교정테이션수동확인대기중투입현재송장판독목적지상태없음미터널불량배출실패회전측처리예외완료 0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_().·—");
    EditorUtility.SetDirty(font);AssetDatabase.SaveAssets();
   }
   var hud=GameObject.Find("FactoryHUD");
   if(hud==null){hud=new GameObject("FactoryHUD",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));Undo.RegisterCreatedObjectUndo(hud,"공정 HUD 생성");}
   var canvas=hud.GetComponent<Canvas>();if(canvas==null)throw new Exception("FactoryHUD의 Canvas를 확인하세요.");
   canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=210;canvas.targetDisplay=0;
   var scaler=hud.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
   var panel=Node("BoxInfoPanel",hud.transform,new Vector2(0,1),new Vector2(24,-24),new Vector2(580,244));
   Undo.RegisterCreatedObjectUndo(panel,"박스 정보 패널 생성");
   panel.AddComponent<Image>().color=new Color(.035f,.055f,.085f,.55f);
   var outline=panel.AddComponent<Outline>();outline.effectColor=new Color(.28f,.4f,.52f,.75f);outline.effectDistance=new Vector2(1,-1);
   Text("Heading",panel.transform,"박스 정보",font,new Vector2(24,-15),new Vector2(290,42),28,new Color(.95f,.98f,1),FontStyles.Bold);
   var sourceText=Text("Source",panel.transform,"박스 대기",font,new Vector2(340,-23),new Vector2(215,28),17,new Color(.6f,.7f,.8f));sourceText.alignment=TextAlignmentOptions.TopRight;
   var divider=Node("Divider",panel.transform,new Vector2(0,1),new Vector2(24,-64),new Vector2(532,1));divider.AddComponent<Image>().color=new Color(.22f,.29f,.37f);
   string[] names={"CurrentBox","ShippingLabel","Destination","Correction"};string[] captions={"현재 박스","송장 판독","목적지","교정 상태"};var values=new TMP_Text[4];
   for(int i=0;i<4;i++){
    float y=-76-i*38;
    Text(names[i]+"Caption",panel.transform,captions[i],font,new Vector2(24,y),new Vector2(136,34),20,new Color(.58f,.67f,.76f));
    values[i]=Text(names[i]+"Value",panel.transform,"—",font,new Vector2(164,y),new Vector2(392,34),22,new Color(.92f,.95f,1));
   }
   var display=panel.AddComponent<BoxInfoPanel>();var serialized=new SerializedObject(display);
   var cell=UnityEngine.Object.FindObjectsByType<CellController>(FindObjectsSortMode.None).FirstOrDefault(c=>c.gameObject.scene==scene);
   ConveyorLine line=null;
   if(cell!=null){var cellData=new SerializedObject(cell);line=cellData.FindProperty("correctLine")?.objectReferenceValue as ConveyorLine;}
   var sequence=UnityEngine.Object.FindObjectsByType<CorrectionSequence>(FindObjectsSortMode.None).FirstOrDefault(c=>c.name=="fr5_with_gripper_first"&&c.gameObject.scene==scene);
   var preview=sequence!=null&&sequence.targetBox!=null?sequence.targetBox.GetComponent<BoxInfo>():null;
   serialized.FindProperty("correctionLine").objectReferenceValue=line;serialized.FindProperty("previewBox").objectReferenceValue=preview;
   serialized.FindProperty("sourceText").objectReferenceValue=sourceText;
   string[] fields={"boxText","labelText","destinationText","correctionText"};for(int i=0;i<fields.Length;i++)serialized.FindProperty(fields[i]).objectReferenceValue=values[i];
   var cameras=serialized.FindProperty("viewCameras");cameras.arraySize=3; for(int i=0;i<3;i++){var cameraObject=GameObject.Find(i==0?"CameraSideUi":"CameraSideUi"+(i+1));cameras.GetArrayElementAtIndex(i).objectReferenceValue=cameraObject!=null?cameraObject.GetComponent<Camera>():null;}
   serialized.ApplyModifiedPropertiesWithoutUndo();display.Refresh();
   EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new Exception("씬 저장 실패");Selection.activeGameObject=panel;
   File.WriteAllText("Temp/BoxInfoPanelSetupResult.txt","OK: panel top-left 580x244; source="+(line!=null?line.name:preview!=null?"manual: "+preview.name:"waiting (no source)")+" backup="+backup);
  }catch(Exception e){File.WriteAllText("Temp/BoxInfoPanelSetupResult.txt","ERROR "+e);Debug.LogException(e);}
 }
 static GameObject Node(string name,Transform parent,Vector2 anchor,Vector2 position,Vector2 size){var g=new GameObject(name,typeof(RectTransform));g.transform.SetParent(parent,false);var r=g.GetComponent<RectTransform>();r.anchorMin=r.anchorMax=anchor;r.pivot=new Vector2(0,1);r.anchoredPosition=position;r.sizeDelta=size;return g;}
 static TMP_Text Text(string name,Transform parent,string value,TMP_FontAsset font,Vector2 position,Vector2 size,float fontSize,Color color,FontStyles style=FontStyles.Normal){var g=Node(name,parent,new Vector2(0,1),position,size);var t=g.AddComponent<TextMeshProUGUI>();t.font=font;t.fontSize=fontSize;t.fontStyle=style;t.text=value;t.color=color;t.alignment=TextAlignmentOptions.Left;t.overflowMode=TextOverflowModes.Truncate;t.textWrappingMode=TextWrappingModes.NoWrap;t.raycastTarget=false;return t;}
}






