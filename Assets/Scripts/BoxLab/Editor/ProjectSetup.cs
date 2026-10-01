#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BoxLab.Editor
{
    public static class ProjectSetup
    {
        public const string ProjectPath = "Assets/BoxLab/BoxProject.asset";
        public const string ScenePath = "Assets/Scenes/BoxLab.unity";
        public const string MaterialPath = "Assets/BoxLab/Materials/Surface.mat";

        [InitializeOnLoadMethod]
        private static void ScheduleFirstSetup()
        {
            if (Application.isBatchMode) return;
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
                if (!File.Exists(ScenePath))
                {
                    var current = SceneManager.GetActiveScene();
                    if (current.isDirty && current.name != "SampleScene")
                    { Debug.Log("BoxLab 已就绪。请从 Tools/BoxLab/初始化示例工程 创建演示场景。"); return; }
                    try { InitializeMenu(); }
                    catch (Exception exception) { Debug.LogException(exception); }
                }
            };
        }

        public static BoxProject EnsureProject()
        {
            Directory.CreateDirectory("Assets/BoxLab/Materials");
            Directory.CreateDirectory("Assets/Scenes");
            AssetDatabase.Refresh();
            var project = AssetDatabase.LoadAssetAtPath<BoxProject>(ProjectPath);
            if (!project)
            {
                project = ScriptableObject.CreateInstance<BoxProject>();
                project.book = Presets.CreateBook();
                project.levels = DemoLevels.Create();
                project.selectedLevel = 0;
                AssetDatabase.CreateAsset(project, ProjectPath);
            }
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!material)
            {
                var shader = Shader.Find("Standard");
                if (!shader) throw new InvalidOperationException("Built-in Standard Shader 不可用，请检查渲染管线。");
                material = new Material(shader) { name = "BoxLab Surface" };
                material.SetFloat("_Glossiness", .22f);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            AssetDatabase.SaveAssets();
            return project;
        }

        [MenuItem("Tools/BoxLab/初始化示例工程", priority = 1)]
        public static void InitializeMenu()
        {
            var project = EnsureProject();
            if (!File.Exists(ScenePath)) CreateScene(project);
            else EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var view = UnityEngine.Object.FindObjectOfType<BoardView>();
            if (view) { view.levelIndex = project.selectedLevel; view.RefreshPreview(); }
            if (!Application.isBatchMode)
            {
                BoxLabWindow.Open();
                if (SceneView.lastActiveSceneView != null)
                {
                    var level = project.SelectedLevel;
                    SceneView.lastActiveSceneView.LookAt(new Vector3((level.width - 1) * .5f, 0, (level.height - 1) * .5f),
                        Quaternion.Euler(64, 0, 0), Mathf.Max(level.width, level.height) * .8f, true);
                }
            }
        }

        public static void CreateScene(BoxProject project)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("BoxLab");
            var view = root.AddComponent<BoardView>();
            var controller = root.AddComponent<GameController>();
            var cameraObject = new GameObject("Board Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            camera.orthographic = true;
            camera.allowHDR = false;
            var light = new GameObject("Key Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = .9f;
            light.color = new Color(1, .94f, .86f);
            light.transform.rotation = Quaternion.Euler(52, -28, 0);
            light.shadows = LightShadows.Soft;
            light.shadowStrength = .40f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.34f, .36f, .39f);
            RenderSettings.skybox = null;
            view.project = project;
            view.levelIndex = Mathf.Clamp(project.selectedLevel, 0, project.levels.Count - 1);
            view.boardCamera = camera;
            view.surfaceMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            controller.project = project;
            controller.levelIndex = view.levelIndex;
            controller.view = view;
            view.RefreshPreview();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("保存 BoxLab 场景失败。");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "BoxLab";
            PlayerSettings.productName = "BoxLab - Rule Workshop";
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 800;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.runInBackground = true;
            AssetDatabase.SaveAssets();
        }

        public static void BatchVerify()
        {
            try
            {
                var project = EnsureProject();
                if (!File.Exists(ScenePath)) CreateScene(project);
                else EditorSceneManager.OpenScene(ScenePath);
                Verification.RunAll();
                if (Environment.GetCommandLineArgs().Contains("-boxlabBuild")) Verification.BuildWindows();
                Debug.Log("BOXLAB_BATCH_SUCCESS");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }
    }
}
#endif
