using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;

[InitializeOnLoad]
public static class PanelBorderSetup
{
 const string Request="Temp/PanelBorderSetup.request";
 const string Folder="Assets/_Scripts/UI/";
 static PanelBorderSetup(){EditorApplication.update+=Poll;}
 static void Poll(){if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists(Request))return;File.Delete(Request);Setup();}
 [MenuItem("Tools/Bonguri/Add Rounded Panel Borders")]
 public static void Setup(){
  if(EditorApplication.isPlayingOrWillChangePlaymode)return;
  try{
   var hud=GameObject.Find("FactoryHUD");if(hud==null)throw new Exception("FactoryHUD 없음");
   string[] names={"DashboardUI","CellOperatorUI","BoxInfoPanel"};
   foreach(var name in names)if(hud.transform.Find(name)==null)throw new Exception(name+" 없음");
   var scene=hud.scene;string backup=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)+"/UnityBackups/bonguriProject/PanelBorders/"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"/bongyuri.unity";
   Directory.CreateDirectory(Path.GetDirectoryName(backup));if(!EditorSceneManager.SaveScene(scene,backup,true))throw new Exception("백업 실패");
   var stroke=MakeSprite("PanelRoundedBorder",true);var fill=MakeSprite("PanelRoundedFill",false);
   foreach(string name in names){
    var panel=hud.transform.Find(name);var background=panel.GetComponent<Image>();Undo.RecordObject(background,"둥근 패널 배경");background.sprite=fill;background.type=Image.Type.Sliced;background.pixelsPerUnitMultiplier=1;background.color=new Color(0.055f,0.075f,0.10f,0.65f);background.material=null;background.raycastTarget=false;
    foreach(var outline in panel.GetComponents<Outline>()){Undo.RecordObject(outline,"Outline 비활성화");outline.enabled=false;}
    var existing=panel.Find("Border");GameObject border;
    if(existing==null){border=new GameObject("Border",typeof(RectTransform),typeof(Image));Undo.RegisterCreatedObjectUndo(border,"패널 테두리 생성");border.transform.SetParent(panel,false);}else border=existing.gameObject;
    var rect=border.GetComponent<RectTransform>();Undo.RecordObject(rect,"테두리 정렬");rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.pivot=new Vector2(.5f,.5f);rect.offsetMin=rect.offsetMax=Vector2.zero;rect.localScale=Vector3.one;rect.localRotation=Quaternion.identity;
    var image=border.GetComponent<Image>();Undo.RecordObject(image,"테두리 스타일");image.material=null;image.sprite=stroke;image.type=Image.Type.Sliced;image.fillCenter=false;image.pixelsPerUnitMultiplier=1;image.color=new Color(168f/255,194f/255,216f/255,.4f);image.raycastTarget=false;border.transform.SetAsLastSibling();EditorUtility.SetDirty(background);EditorUtility.SetDirty(image);
   }
   Canvas.ForceUpdateCanvases();EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new Exception("씬 저장 실패");
   Selection.activeGameObject=hud.transform.Find("DashboardUI/Border").gameObject;
   File.WriteAllText("Temp/PanelBorderSetupResult.txt","OK: 3 borders; Sliced; stroke 1.5px; radius 12px; #A8C2D8 alpha 0.40; raycast off; backup="+backup);
  }catch(Exception e){File.WriteAllText("Temp/PanelBorderSetupResult.txt","ERROR "+e);Debug.LogException(e);}
 }
 static Sprite MakeSprite(string name,bool border){
  const int size=64;const float radius=12,thickness=1.5f;var texture=new Texture2D(size,size,TextureFormat.RGBA32,false);var pixels=new Color[size*size];
  for(int y=0;y<size;y++)for(int x=0;x<size;x++){
   float qx=Mathf.Abs(x+.5f-size*.5f)-(size*.5f-radius);float qy=Mathf.Abs(y+.5f-size*.5f)-(size*.5f-radius);
   float distance=new Vector2(Mathf.Max(qx,0),Mathf.Max(qy,0)).magnitude+Mathf.Min(Mathf.Max(qx,qy),0)-radius;
   float alpha=Mathf.Clamp01(.5f-distance);if(border)alpha*=Mathf.Clamp01(distance+thickness+.5f);pixels[y*size+x]=new Color(1,1,1,alpha);
  }
  texture.SetPixels(pixels);texture.Apply();string path=Folder+name+".png";File.WriteAllBytes(path,texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);
  AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
  var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.spritePixelsPerUnit=100;importer.spriteBorder=new Vector4(14,14,14,14);importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.filterMode=FilterMode.Bilinear;importer.wrapMode=TextureWrapMode.Clamp;importer.textureCompression=TextureImporterCompression.Uncompressed;
  var settings=new TextureImporterSettings();importer.ReadTextureSettings(settings);settings.spriteMeshType=SpriteMeshType.FullRect;importer.SetTextureSettings(settings);importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Sprite>(path);
 }
}

