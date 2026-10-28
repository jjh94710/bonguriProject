using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Bonguri.ParcelTools
{
    [InitializeOnLoad]
    public static class CreateParcelBox
    {
        const string Folder = "Assets/Project/Parcels";
        const string Prefab = Folder + "/ParcelBox_6x8x12cm.prefab";
        const string Report = "Temp/ParcelBoxCreation.json";
        const string Once = "Bonguri.ParcelBox.Created.v1";
        static readonly Vector3 Size = new Vector3(.06f, .08f, .12f);

        static CreateParcelBox() { EditorApplication.delayCall += AutoCreate; }

        static void AutoCreate()
        {
            if (SessionState.GetBool(Once, false) || File.Exists(Report)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            { EditorApplication.delayCall += AutoCreate; return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (SceneManager.GetActiveScene().path != "Assets/Scenes/bongyuri.unity") return;
            Create();
        }

        [MenuItem("Tools/Bonguri/Create Parcel Test Box")]
        public static void Create()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != "Assets/Scenes/bongyuri.unity")
            { Debug.LogWarning("Open Assets/Scenes/bongyuri.unity in Edit mode first."); return; }
            var existing = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "ParcelBox_GraspTest");
            if (existing != null) { Selection.activeGameObject = existing; WriteReport(existing, "Existing box selected"); return; }

            SessionState.SetBool(Once, true);
            try
            {
                string backup = "Assets/SceneBackups/ParcelBox/" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "/bongyuri.unity";
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                if (!EditorSceneManager.SaveScene(scene, backup, true)) throw new IOException("Scene backup failed");
                Directory.CreateDirectory(Folder);
                AssetDatabase.Refresh();
                var cardboard = Material("Cardboard", new Color(.56f, .35f, .16f));
                var tape = Material("PackingTape", new Color(.75f, .58f, .32f));
                var paper = Material("ShippingLabel", new Color(.94f, .93f, .87f));
                var ink = Material("LabelInk", new Color(.06f, .065f, .07f));

                if (AssetDatabase.LoadAssetAtPath<GameObject>(Prefab) == null)
                {
                    var template = new GameObject("ParcelBox_6x8x12cm");
                    try
                    {
                        var collider = template.AddComponent<BoxCollider>();
                        collider.size = Size;
                        var friction = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(Folder + "/CardboardFriction.physicMaterial");
                        if (friction == null)
                        {
                            friction = new PhysicsMaterial("CardboardFriction") { dynamicFriction = .7f, staticFriction = .8f, bounciness = 0f, frictionCombine = PhysicsMaterialCombine.Maximum };
                            AssetDatabase.CreateAsset(friction, Folder + "/CardboardFriction.physicMaterial");
                        }
                        collider.sharedMaterial = friction;
                        var rigidbody = template.AddComponent<Rigidbody>();
                        rigidbody.mass = .15f;
                        rigidbody.useGravity = true;
                        rigidbody.isKinematic = false;
                        rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
                        rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                        Visual(template.transform, "CardboardBody", Vector3.zero, Size, cardboard);
                        Visual(template.transform, "TopPackingTape", new Vector3(0,.04015f,0), new Vector3(.013f,.0003f,.12f), tape);
                        Visual(template.transform, "BottomPackingTape", new Vector3(0,-.04015f,0), new Vector3(.013f,.0003f,.12f), tape);
                        Visual(template.transform, "EndTapeFront", new Vector3(0,0,.06015f), new Vector3(.013f,.08f,.0003f), tape);
                        Visual(template.transform, "EndTapeBack", new Vector3(0,0,-.06015f), new Vector3(.013f,.08f,.0003f), tape);
                        Visual(template.transform, "ShippingLabel", new Vector3(.008f,.0405f,.014f), new Vector3(.036f,.0003f,.045f), paper);
                        for (int i=0; i<11; i++)
                            Visual(template.transform, "LabelBar_"+i, new Vector3(-.006f+i*.0026f,.04075f,.018f), new Vector3(i%3==0?.0013f:.0007f,.00015f,.022f), ink);
                        Visual(template.transform, "LabelAddressLine", new Vector3(.008f,.04075f,-.001f), new Vector3(.025f,.00015f,.0012f), ink);
                        PrefabUtility.SaveAsPrefabAsset(template, Prefab);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(template); }
                }

                var robot = scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true))
                    .FirstOrDefault(t=>t.name == "gripper_base_link");
                var target = robot != null ? robot.position : (SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero);
                string surface;
                var position = FindSurface(scene, target, out surface);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab), scene);
                Undo.RegisterCreatedObjectUndo(instance, "Create parcel test box");
                instance.name = "ParcelBox_GraspTest";
                instance.transform.position = position;
                instance.transform.rotation = Quaternion.identity;
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Saving scene failed");
                AssetDatabase.SaveAssets();
                Selection.activeGameObject = instance;
                if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Frame(new Bounds(position, Vector3.one * .7f), false);
                WriteReport(instance, surface + "; backup=" + backup);
                Debug.Log("Created ParcelBox_GraspTest: 6 x 8 x 12 cm, 0.15 kg. " + surface);
            }
            catch (Exception e) { File.WriteAllText(Report, "ERROR: " + e); Debug.LogException(e); }
        }

        static Vector3 FindSurface(Scene scene, Vector3 target, out string note)
        {
            Physics.SyncTransforms();
            float best = float.MaxValue;
            Vector3 selected = target + Vector3.right * .15f;
            note = "Fallback near gripper; verify support before Play";
            var renderers = scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>());
            foreach (var renderer in renderers)
            {
                if (!renderer.gameObject.activeInHierarchy || renderer.GetComponentInParent<ArticulationBody>() != null) continue;
                string path = AnimationUtility.CalculateTransformPath(renderer.transform, null).ToLowerInvariant();
                if (!path.Contains("conveyor") || path.Contains("legs") || path.Contains("control")) continue;
                Bounds bounds = renderer.bounds;
                if (bounds.size.x < Size.x + .015f || bounds.size.z < Size.z + .015f) continue;
                Vector3 p = new Vector3(Mathf.Clamp(target.x, bounds.min.x+.04f, bounds.max.x-.04f), bounds.max.y, Mathf.Clamp(target.z,bounds.min.z+.07f,bounds.max.z-.07f));
                float distance = Vector2.Distance(new Vector2(p.x,p.z),new Vector2(target.x,target.z));
                if (distance > 1.2f) continue;
                var hits = Physics.RaycastAll(new Vector3(p.x,bounds.max.y+.4f,p.z),Vector3.down,3f,~0,QueryTriggerInteraction.Ignore)
                    .Where(h=>h.collider.gameObject.scene==scene && h.collider.GetComponentInParent<ArticulationBody>()==null && h.normal.y>.8f
                        && AnimationUtility.CalculateTransformPath(h.collider.transform,null).ToLowerInvariant().Contains("conveyor"))
                    .OrderBy(h=>h.distance).ToArray();
                if (hits.Length == 0) continue;
                float score = distance + Mathf.Abs(hits[0].point.y-target.y)*.2f;
                if (score >= best) continue;
                best=score; selected=hits[0].point+Vector3.up*(Size.y*.5f+.003f);
                note="Supported by " + hits[0].collider.name;
            }
            if (best == float.MaxValue)
            {
                var hits=Physics.RaycastAll(selected+Vector3.up*2f,Vector3.down,10f,~0,QueryTriggerInteraction.Ignore)
                    .Where(h=>h.collider.gameObject.scene==scene && h.collider.GetComponentInParent<ArticulationBody>()==null && h.normal.y>.8f)
                    .OrderBy(h=>h.distance).ToArray();
                if(hits.Length>0) { selected=hits[0].point+Vector3.up*(Size.y*.5f+.003f); note="Supported by "+hits[0].collider.name; }
            }
            return selected;
        }

        static Material Material(string name, Color color)
        {
            var path=Folder+"/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material!=null) return material;
            var shader=Shader.Find("Universal Render Pipeline/Lit");
            if(shader==null) throw new InvalidOperationException("URP Lit shader missing");
            material=new Material(shader){name=name}; material.SetColor("_BaseColor",color);material.SetFloat("_Smoothness",.12f);
            AssetDatabase.CreateAsset(material,path);return material;
        }

        static void Visual(Transform parent,string name,Vector3 position,Vector3 scale,Material material)
        {
            var part=GameObject.CreatePrimitive(PrimitiveType.Cube);part.name=name;part.transform.SetParent(parent,false);
            part.transform.localPosition=position;part.transform.localScale=scale;
            UnityEngine.Object.DestroyImmediate(part.GetComponent<BoxCollider>());
            part.GetComponent<MeshRenderer>().sharedMaterial=material;
        }

        [Serializable] class Result { public string name, scene, prefab, note; public Vector3 position,size; public float mass;public bool collider,gravity; }
        static void WriteReport(GameObject box,string note)
        {
            Directory.CreateDirectory("Temp");var body=box.GetComponent<Rigidbody>();
            File.WriteAllText(Report,JsonUtility.ToJson(new Result{name=box.name,scene=box.scene.path,prefab=Prefab,note=note,position=box.transform.position,size=box.GetComponent<BoxCollider>().size,mass=body.mass,collider=box.GetComponent<BoxCollider>()!=null,gravity=body.useGravity},true));
        }
    }
}
