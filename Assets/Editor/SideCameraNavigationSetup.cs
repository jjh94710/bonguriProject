using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

[InitializeOnLoad]
public static class SideCameraNavigationSetup
{
    const string Request="Temp/SideCameraNavigationSetup.request";
    static SideCameraNavigationSetup(){EditorApplication.update+=Poll;}
    static void Poll(){
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists(Request))return;
        File.Delete(Request);Setup();
    }
    [MenuItem("Tools/Bonguri/Setup Side Camera Navigation")]
    public static void Setup(){
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        try{
            var names=new[]{"CameraSideUi","CameraSideUi2","CameraSideUi3"};
            var cameras=UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            var views=names.Select(n=>cameras.Single(c=>c.name==n)).ToArray();
            var scene=views[0].gameObject.scene;
            if(views.Any(c=>c.gameObject.scene!=scene))throw new Exception("세 카메라는 같은 씬에 있어야 합니다.");
            if(GameObject.Find("CameraNavigationUI")!=null)throw new Exception("CameraNavigationUI가 이미 있습니다.");
            string backup=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)+"/UnityBackups/bonguriProject/CameraNavigation/"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"/bongyuri.unity";
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            if(!EditorSceneManager.SaveScene(scene,backup,true))throw new Exception("씬 백업 실패");
            var root=new GameObject("CameraNavigationUI",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster),typeof(SideCameraNavigation));
            Undo.RegisterCreatedObjectUndo(root,"카메라 전환 UI 만들기");
            var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=200;canvas.targetDisplay=0;
            var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            var buttonGo=new GameObject("LeftViewButton",typeof(RectTransform),typeof(Image),typeof(Button));buttonGo.transform.SetParent(root.transform,false);
            var rect=buttonGo.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=new Vector2(0,.5f);rect.pivot=new Vector2(0,.5f);rect.anchoredPosition=new Vector2(24,0);rect.sizeDelta=new Vector2(64,88);
            var image=buttonGo.GetComponent<Image>();image.color=new Color(.04f,.07f,.11f,.88f);
            var button=buttonGo.GetComponent<Button>();var colors=button.colors;colors.highlightedColor=new Color(.5f,.8f,1f);colors.pressedColor=new Color(.3f,.65f,.9f);colors.disabledColor=new Color(1,1,1,.25f);button.colors=colors;
            var textGo=new GameObject("Arrow",typeof(RectTransform),typeof(Text));textGo.transform.SetParent(buttonGo.transform,false);
            var textRect=textGo.GetComponent<RectTransform>();textRect.anchorMin=Vector2.zero;textRect.anchorMax=Vector2.one;textRect.offsetMin=textRect.offsetMax=Vector2.zero;
            var text=textGo.GetComponent<Text>();text.text="<";text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");text.fontSize=48;text.alignment=TextAnchor.MiddleCenter;text.color=Color.white;text.raycastTarget=false;
            var other=cameras.Where(c=>!views.Contains(c)&&c.transform.parent==views[0].transform.parent&&c.targetTexture==null).ToArray();
            var nav=root.GetComponent<SideCameraNavigation>();var so=new SerializedObject(nav);
            var v=so.FindProperty("views");v.arraySize=3;for(int i=0;i<3;i++)v.GetArrayElementAtIndex(i).objectReferenceValue=views[i];
            var o=so.FindProperty("otherViewCameras");o.arraySize=other.Length;for(int i=0;i<other.Length;i++)o.GetArrayElementAtIndex(i).objectReferenceValue=other[i];
            so.FindProperty("leftButton").objectReferenceValue=button;so.ApplyModifiedPropertiesWithoutUndo();
            foreach(var c in views.Concat(other)){
                Undo.RecordObject(c,"초기 카메라 선택");c.enabled=c==views[0];
                if(views.Contains(c))c.targetDisplay=0;
                var listener=c.GetComponent<AudioListener>();if(listener!=null){Undo.RecordObject(listener,"카메라 오디오 선택");listener.enabled=c==views[0];}
            }
            var events=UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
            if(events.Length==0){var e=new GameObject("EventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule));Undo.RegisterCreatedObjectUndo(e,"UI 이벤트 생성");}
            else if(!events.Any(e=>e.GetComponent<BaseInputModule>()!=null))throw new Exception("기존 EventSystem 입력 모듈을 확인하세요.");
            EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new Exception("씬 저장 실패");
            Selection.activeGameObject=root;
            File.WriteAllText("Temp/SideCameraNavigationSetupResult.txt","OK: CameraSideUi -> CameraSideUi2 -> CameraSideUi3, 3번 왼쪽 버튼 비활성화. backup="+backup);
        }catch(Exception e){File.WriteAllText("Temp/SideCameraNavigationSetupResult.txt","ERROR "+e);Debug.LogException(e);}
    }
}
