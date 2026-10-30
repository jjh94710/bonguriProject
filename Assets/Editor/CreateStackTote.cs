using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
[InitializeOnLoad] public static class CreateStackTote {
 static double ready;
 static CreateStackTote(){ready=EditorApplication.timeSinceStartup+3;EditorApplication.update+=Poll;}
 static void Poll(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.timeSinceStartup<ready||!File.Exists("Temp/CreateStackTote.request"))return;File.Delete("Temp/CreateStackTote.request");Create();}
 [MenuItem("Tools/Bonguri/Create Open Six Parcel Container")]
 public static void Create(){try{
 if(EditorApplication.isPlayingOrWillChangePlaymode)return;
 var root=GameObject.Find("ParcelStackContainer_6") ?? GameObject.Find("ParcelContainer_2x3_6");if(root==null)throw new Exception("Existing container missing");
 var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/ParcelBox_ProcessVariant.prefab");if(prefab==null)throw new Exception("Parcel prefab missing");
 var box=prefab.GetComponent<BoxCollider>();if(box==null)throw new Exception("Parcel BoxCollider missing");
 Vector3 size=Vector3.Scale(box.size,prefab.transform.localScale);
 var scene=root.scene;
 string backup="Backups/StackContainer/"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"/bongyuri.unity";Directory.CreateDirectory(Path.GetDirectoryName(backup));if(!EditorSceneManager.SaveScene(scene,backup,true))throw new Exception("Backup failed");
 float gap=.01f,margin=.015f;float width=size.x*2+gap+margin*2,depth=size.z*3+gap*2+margin*2,height=size.y+.02f,wall=.012f;
 Undo.RegisterFullObjectHierarchyUndo(root,"Change container to flat 2x3");
 if(PrefabUtility.IsPartOfPrefabInstance(root))PrefabUtility.UnpackPrefabInstance(root,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
 while(root.transform.childCount>0)Undo.DestroyObjectImmediate(root.transform.GetChild(0).gameObject);
 root.name="ParcelContainer_2x3_6";root.transform.localScale=Vector3.one;
 string matPath="Assets/prefabs/ParcelStackContainer_Material.mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(matPath);
 if(mat==null){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));mat.color=new Color(.16f,.33f,.43f);AssetDatabase.CreateAsset(mat,matPath);}
 Part(root,"Floor",new Vector3(0,wall*.5f,0),new Vector3(width+wall*2,wall,depth+wall*2),mat);
 Part(root,"LeftWall",new Vector3(-(width+wall)*.5f,wall+height*.5f,0),new Vector3(wall,height,depth+wall*2),mat);
 Part(root,"RightWall",new Vector3((width+wall)*.5f,wall+height*.5f,0),new Vector3(wall,height,depth+wall*2),mat);
 Part(root,"FrontWall",new Vector3(0,wall+height*.5f,-(depth+wall)*.5f),new Vector3(width,height,wall),mat);
 Part(root,"BackWall",new Vector3(0,wall+height*.5f,(depth+wall)*.5f),new Vector3(width,height,wall),mat);
 var slots=new GameObject("Slots_2Columns_3Rows");slots.transform.SetParent(root.transform,false);
 for(int i=0;i<6;i++){var slot=new GameObject("Slot_"+(i+1));slot.transform.SetParent(slots.transform,false);int column=i%2,row=i/2;slot.transform.localPosition=new Vector3((column-.5f)*(size.x+gap),wall+size.y*.5f-box.center.y,(row-1)*(size.z+gap));}
 // The old solid placeholder would obstruct filling; keep it inactive for easy recovery.

 string oldPath="Assets/prefabs/ParcelStackContainer_6.prefab",newPath="Assets/prefabs/ParcelContainer_2x3_6.prefab";
 if(AssetDatabase.LoadAssetAtPath<GameObject>(oldPath)!=null){string error=AssetDatabase.MoveAsset(oldPath,newPath);if(!string.IsNullOrEmpty(error))throw new Exception(error);}
 PrefabUtility.SaveAsPrefabAssetAndConnect(root,newPath,InteractionMode.AutomatedAction);
 if(root.GetComponentsInChildren<BoxCollider>().Length!=5||height<size.y||width<size.x*2||depth<size.z*3)throw new Exception("Container geometry validation failed");
 EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new Exception("Save failed");Selection.activeGameObject=root;
 SceneView.lastActiveSceneView?.Frame(new Bounds(root.transform.position+Vector3.up*(height*.5f),new Vector3(.5f,.65f,.5f)),false);
 File.WriteAllText("Temp/StackToteResult.txt","OK inner="+new Vector3(width,height,depth)+" parcel="+size+" capacity=6 slots=6 colliders=5 openTop=True position="+root.transform.position+" layout=2x3 singleLayer=True"+" backup="+backup);
 }catch(Exception e){File.WriteAllText("Temp/StackToteResult.txt","ERROR "+e);Debug.LogException(e);}}
 static void Part(GameObject parent,string name,Vector3 pos,Vector3 scale,Material mat){var part=GameObject.CreatePrimitive(PrimitiveType.Cube);part.name=name;part.transform.SetParent(parent.transform,false);part.transform.localPosition=pos;part.transform.localScale=scale;part.GetComponent<Renderer>().sharedMaterial=mat;}
}
