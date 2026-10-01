using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BoxLab
{
    /// <summary>Opt-in player smoke test. A normal launch never creates this component.</summary>
    public sealed class StandaloneVerification : MonoBehaviour
    {
        private GameController controller;
        private readonly List<string> results = new List<string>();
        private string evidenceDirectory;
        private double startedAt;
        private bool completed;
        private string reportedRuntimeError;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InitializeWhenRequested()
        {
            if (Application.isEditor) return;
            bool requested = false;
            foreach (string argument in Environment.GetCommandLineArgs())
                if (string.Equals(argument, "-boxlabVerify", StringComparison.OrdinalIgnoreCase)) requested = true;
            if (!requested) return;
            Application.runInBackground = true;
            var host = new GameObject("BoxLab standalone verification");
            DontDestroyOnLoad(host);
            host.AddComponent<StandaloneVerification>();
        }

        private IEnumerator Start()
        {
            startedAt = Time.realtimeSinceStartupAsDouble;
            evidenceDirectory = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "BoxLabEvidence"));
            var stack = new Stack<IEnumerator>();
            stack.Push(Run());
            // Flatten nested iterators so failures from every test stage reach one exit handler.
            while (stack.Count > 0 && !completed)
            {
                object wait = null;
                bool advanceFrame = false;
                Exception failure = null;
                try
                {
                    if (Time.realtimeSinceStartupAsDouble - startedAt > 180) throw new TimeoutException("Standalone verification exceeded 180 seconds.");
                    for (int transitions = 0; transitions < 100 && stack.Count > 0; transitions++)
                    {
                        IEnumerator top = stack.Peek();
                        if (!top.MoveNext()) { stack.Pop(); continue; }
                        var nested = top.Current as IEnumerator;
                        if (nested != null) { stack.Push(nested); continue; }
                        wait = top.Current;
                        advanceFrame = true;
                        break;
                    }
                }
                catch (Exception exception) { failure = exception; }
                if (failure != null) { Finish(false, failure.ToString()); yield break; }
                if (advanceFrame) yield return wait;
                else if (stack.Count > 0) yield return null;
            }
            if (!completed) Finish(true, "");
        }

        private void Awake() { Application.logMessageReceived += OnRuntimeLog; }
        private void OnDestroy() { Application.logMessageReceived -= OnRuntimeLog; }
        private void OnRuntimeLog(string condition, string stackTrace, LogType type)
        {
            if (!completed && (type == LogType.Exception || type == LogType.Error || type == LogType.Assert))
                reportedRuntimeError = condition + "\n" + stackTrace;
        }
        private void Update()
        {
            if (completed || string.IsNullOrEmpty(evidenceDirectory)) return;
            if (!string.IsNullOrEmpty(reportedRuntimeError)) Finish(false, "Runtime error log:\n" + reportedRuntimeError);
            else if (Time.realtimeSinceStartupAsDouble - startedAt > 180) Finish(false, "Standalone verification exceeded 180 seconds.");
        }

        private IEnumerator Run()
        {
            Directory.CreateDirectory(evidenceDirectory);
            Debug.Log("BOXLAB_STANDALONE_START");
            Require(SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null,
                "A real graphics device is required. Do not use -nographics for this verification.");
            double loadDeadline = Time.realtimeSinceStartupAsDouble + 15;
            while (!controller || controller.State == null)
            {
                controller = FindObjectOfType<GameController>();
                if (Time.realtimeSinceStartupAsDouble > loadDeadline) throw new TimeoutException("GameController did not initialize within 15 seconds.");
                yield return null;
            }
            Require(controller.project && controller.project.levels != null && controller.project.levels.Count > 0, "No authored levels found.");
            Require(controller.view, "The real BoardView is missing.");
            string originalProject = JsonUtility.ToJson(controller.project);
            controller.view.animationSeconds = .04f;
            controller.LoadLevel(0);
            yield return Settle();
            CheckRendering();
            yield return Capture("standalone-initial.png");
            Pass("Player booted with an active graphics device and visible, supported materials: " + SystemInfo.graphicsDeviceName);

            int completedLevels = 0;
            for (int index = 0; index < controller.project.levels.Count; index++)
            {
                LevelData level = controller.project.levels[index];
                var solver = new SokobanSolver(level, controller.project.book, 250000, 20000);
                while (solver.Status == SolveStatus.Searching)
                {
                    solver.Step(500);
                    yield return null;
                }
                Require(solver.Status == SolveStatus.Solved, level.name + ": solver did not find a path: " + solver.Message);
                controller.LoadLevel(index);
                Require(controller.State != null, level.name + ": runtime load failed.");
                foreach (Direction direction in solver.Solution)
                {
                    var expected = RuleEngine.Step(level, controller.project.book, controller.State, direction).state;
                    controller.TryMove(direction);
                    double moveDeadline = Time.realtimeSinceStartupAsDouble + 12;
                    yield return null;
                    while (controller.IsBusy)
                    {
                        if (Time.realtimeSinceStartupAsDouble > moveDeadline) throw new TimeoutException(level.name + ": animation did not settle within 12 seconds.");
                        yield return null;
                    }
                    Require(JsonUtility.ToJson(controller.State) == JsonUtility.ToJson(expected), level.name + ": player controller diverged from the deterministic rule result.");
                }
                Require(controller.State.status == GameStatus.Won, level.name + ": actual animated playback did not win.");
                yield return Settle();
                CheckRendering();
                completedLevels++;
                Pass("Real animated player completed " + level.name + " / " + solver.Solution.Count + " commands / " + solver.Visited + " searched states.");
            }
            Require(completedLevels == controller.project.levels.Count, "Some levels were skipped.");
            Require(JsonUtility.ToJson(controller.project) == originalProject, "Standalone playback modified authored project data.");
            yield return Capture("standalone-final.png");
            Pass("All " + completedLevels + " authored levels passed; project data and selection remained unchanged.");
        }

        private IEnumerator Settle()
        {
            // Two complete updates plus a rendered frame let deferred Destroy and IMGUI settle.
            yield return null;
            yield return null;
            yield return new WaitForEndOfFrame();
        }

        private IEnumerator Capture(string fileName)
        {
            yield return Settle();
            string path = Path.Combine(evidenceDirectory, fileName);
            // A hidden player window can return a valid-sized, uniformly black swap-chain texture.
            // Inspect actual pixels before treating an encoded PNG as evidence.
            Texture2D texture = null;
            string source = "screen capture including HUD";
            string pixelEvidence = "";
            try
            {
                try { texture = ScreenCapture.CaptureScreenshotAsTexture(); }
                catch (Exception exception) { results.Add("NOTE Screen capture unavailable: " + exception.Message); }
                if (!HasVisibleVariation(texture, out pixelEvidence))
                {
                    results.Add("NOTE Screen capture is blank/invalid (" + pixelEvidence + "); rendering the actual BoardView camera offscreen. This fallback does not capture IMGUI/HUD.");
                    if (texture) Destroy(texture);
                    texture = RenderBoardCamera();
                    source = "offscreen BoardView camera capture (HUD excluded; HUD requires separate visible-launch verification)";
                }
                Require(HasVisibleVariation(texture, out pixelEvidence), "Rendered evidence is still black/uniform: " + pixelEvidence);
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally { if (texture) Destroy(texture); }
            Require(new FileInfo(path).Length > 1000, "Screenshot file was unexpectedly empty.");
            Pass("Nonblank " + source + ": " + path + " / " + pixelEvidence);
        }

        private Texture2D RenderBoardCamera()
        {
            Camera camera = controller.view.boardCamera;
            Require(camera, "Cannot render fallback evidence without the BoardView camera.");
            RenderTexture originalTarget = camera.targetTexture;
            RenderTexture originalActive = RenderTexture.active;
            Rect originalRect = camera.rect;
            float originalAspect = camera.aspect;
            int width = Mathf.Clamp(camera.pixelWidth, 640, 1920);
            int height = Mathf.Clamp(Mathf.RoundToInt(width / Mathf.Max(.25f, originalAspect)), 240, 1920);
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            Texture2D texture = null;
            try
            {
                Require(target.Create(), "Could not create a real offscreen RenderTexture.");
                camera.targetTexture = target;
                camera.rect = new Rect(0, 0, 1, 1);
                camera.aspect = width / (float)height;
                camera.Render();
                RenderTexture.active = target;
                texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                texture.Apply(false, false);
                return texture;
            }
            catch
            {
                if (texture) Destroy(texture);
                throw;
            }
            finally
            {
                camera.targetTexture = originalTarget;
                camera.rect = originalRect;
                camera.aspect = originalAspect;
                RenderTexture.active = originalActive;
                target.Release();
                Destroy(target);
            }
        }

        private static bool HasVisibleVariation(Texture2D texture, out string evidence)
        {
            evidence = "no readable texture";
            if (!texture || texture.width < 100 || texture.height < 100) return false;
            Color32[] pixels = texture.GetPixels32();
            if (pixels.Length == 0) return false;
            Color32 reference = pixels[0];
            int stride = Mathf.Max(1, pixels.Length / 12000);
            int minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0;
            int samples = 0, distinct = 0;
            for (int i = 0; i < pixels.Length; i += stride)
            {
                Color32 pixel = pixels[i];
                minR = Math.Min(minR, pixel.r); minG = Math.Min(minG, pixel.g); minB = Math.Min(minB, pixel.b);
                maxR = Math.Max(maxR, pixel.r); maxG = Math.Max(maxG, pixel.g); maxB = Math.Max(maxB, pixel.b);
                if (Math.Abs(pixel.r - reference.r) > 18 || Math.Abs(pixel.g - reference.g) > 18 || Math.Abs(pixel.b - reference.b) > 18) distinct++;
                samples++;
            }
            int channelRange = Math.Max(maxR - minR, Math.Max(maxG - minG, maxB - minB));
            evidence = texture.width + "x" + texture.height + ", channel variation=" + channelRange + ", nonbackground samples=" + distinct + "/" + samples;
            return channelRange >= 24 && distinct >= Math.Max(8, samples / 200);
        }

        private void CheckRendering()
        {
            Require(controller.view.boardCamera && controller.view.boardCamera.enabled, "Board camera is missing or disabled.");
            var renderers = controller.view.GetComponentsInChildren<Renderer>();
            Require(renderers.Length > 10, "Board did not create its expected visible geometry.");
            foreach (Renderer renderer in renderers)
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                foreach (Material material in renderer.sharedMaterials)
                {
                    Require(material && material.shader, "Missing material or shader on " + renderer.name);
                    Require(material.shader.isSupported, "Unsupported shader on " + renderer.name + ": " + material.shader.name);
                    Require(material.shader.name != "Hidden/InternalErrorShader", "Pink error shader detected on " + renderer.name);
                }
            }
        }

        private void Pass(string message)
        {
            results.Add("PASS " + message);
            Debug.Log("BOXLAB_STANDALONE_CHECK " + message);
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        private void Finish(bool success, string error)
        {
            if (completed) return;
            completed = true;
            string marker = success ? "BOXLAB_STANDALONE_SUCCESS" : "BOXLAB_STANDALONE_FAILURE";
            results.Insert(0, marker);
            results.Add("Elapsed seconds: " + (Time.realtimeSinceStartupAsDouble - startedAt).ToString("F2"));
            if (!string.IsNullOrEmpty(error)) results.Add(error);
            try
            {
                Directory.CreateDirectory(evidenceDirectory);
                File.WriteAllLines(Path.Combine(evidenceDirectory, "standalone-report.txt"), results);
            }
            catch (Exception writeError) { Debug.LogError("Could not write standalone report: " + writeError.Message); }
            if (success) Debug.Log(marker);
            else Debug.LogError(marker + "\n" + error);
            Application.Quit(success ? 0 : 1);
        }
    }
}
