#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BoxLab.Editor
{
    /// <summary>Read-only source-asset inventory. Never imports, modifies, moves or deletes art.</summary>
    public static class ArtAssetAudit
    {
        const string ArtRoot = "Assets/BoxLabArt";
        const string ThirdPartyRoot = ArtRoot + "/ThirdParty/";
        const string Output = "BoxLabEvidence";

        [Serializable] sealed class AssetEntry
        {
            public string path, category, recommendation;
            public long sourceBytes, metaBytes;
            public bool model, resourcesRoot, runtimeDependency, generatorInput, generatorDependency, keepDocumentation;
        }
        [Serializable] sealed class PackageEntry
        {
            public string directory;
            public int sourceFileCount, modelCount, runtimeModelCount, generatorOnlyModelCount;
            public long sourceBytes, metaBytes;
            public List<string> unreferencedModels = new List<string>();
        }
        [Serializable] sealed class AuditReport
        {
            public string generatedUtc, unityVersion, projectDirectory;
            public bool generatorManifestAvailable;
            public int assetCount, runtimeAssetCount, generatorOnlyAssetCount, candidateCount, unusedThirdPartyModelsCount;
            public long sourceBytes, metaBytes;
            public List<string> sceneRoots = new List<string>();
            public List<string> resourceRoots = new List<string>();
            public List<string> generatorInputs = new List<string>();
            public List<string> missingGeneratorInputs = new List<string>();
            public List<string> notes = new List<string>();
            public List<PackageEntry> thirdParty = new List<PackageEntry>();
            public List<AssetEntry> assets = new List<AssetEntry>();
        }

        [MenuItem("Tools/BoxLab/玩家工坊/审计美术资源（只读）", priority = 82)]
        public static void Run()
        {
            var report = Collect();
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output, "art-audit.json"), JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(Output, "art-audit.txt"), ToText(report), new UTF8Encoding(false));
            Debug.Log("BOXLAB_ART_AUDIT_COMPLETE: " + report.assetCount + " art assets; " + report.runtimeAssetCount +
                " runtime dependencies, " + report.generatorOnlyAssetCount + " generator-only, " + report.candidateCount +
                " unreferenced candidates; unusedThirdPartyModelsCount=" + report.unusedThirdPartyModelsCount +
                ". Reports: " + Path.GetFullPath(Output + "/art-audit.txt"));
        }

        // -executeMethod BoxLab.Editor.ArtAssetAudit.RunBatch; no player build or asset mutations.
        public static void RunBatch()
        {
            try { Run(); if (Application.isBatchMode) EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); if (Application.isBatchMode) EditorApplication.Exit(1); else throw; }
        }

        static AuditReport Collect()
        {
            var report = new AuditReport { generatedUtc = DateTime.UtcNow.ToString("o"), unityVersion = Application.unityVersion,
                projectDirectory = Path.GetFullPath(".") };
            var scenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sceneObjectAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var scene in EditorBuildSettings.scenes)
                if (scene.enabled && !string.IsNullOrEmpty(scene.path)) scenes.Add(Normalize(scene.path));
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)) continue;
                if (!string.IsNullOrEmpty(scene.path)) scenes.Add(Normalize(scene.path));
                if (scene.isDirty || string.IsNullOrEmpty(scene.path))
                    report.notes.Add("场景有未保存内容，已额外读取其对象引用作保守统计：" + scene.name);
                var roots = scene.GetRootGameObjects().Cast<UnityEngine.Object>().ToArray();
                if (roots.Length == 0) continue;
                foreach (var dependency in EditorUtility.CollectDependencies(roots))
                {
                    string path = Normalize(AssetDatabase.GetAssetPath(dependency));
                    if (RuntimeAssetPath(path)) sceneObjectAssets.Add(path);
                }
            }
            report.sceneRoots = scenes.OrderBy(x => x, StringComparer.Ordinal).ToList();
            foreach (string path in AssetDatabase.GetAllAssetPaths())
            {
                string normalized = Normalize(path);
                if (RuntimeAssetPath(normalized) && IsResources(normalized) && !AssetDatabase.IsValidFolder(normalized))
                    report.resourceRoots.Add(normalized);
            }
            report.resourceRoots.Sort(StringComparer.Ordinal);
            // Resources.Load uses strings (including concatenated prefab names), so every runtime
            // Resources asset is a root. Scene-only dependency analysis would miss these assets.
            var runtimeRoots = new HashSet<string>(scenes, StringComparer.OrdinalIgnoreCase);
            runtimeRoots.UnionWith(sceneObjectAssets); runtimeRoots.UnionWith(report.resourceRoots);
            var runtime = Closure(runtimeRoots);

            ReadGeneratorManifest(report);
            var generator = Closure(report.generatorInputs.Where(File.Exists));
            generator.UnionWith(report.generatorInputs); // Missing direct inputs must still be preserved in the report.
            var directInputs = new HashSet<string>(report.generatorInputs, StringComparer.OrdinalIgnoreCase);
            var resourceRoots = new HashSet<string>(report.resourceRoots, StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(ArtRoot)) report.notes.Add("美术目录不存在，当前使用几何占位表现。");
            else foreach (string file in Directory.GetFiles(ArtRoot, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
            {
                string path = Normalize(file);
                if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) { report.metaBytes += new FileInfo(path).Length; continue; }
                var entry = new AssetEntry { path = path, sourceBytes = new FileInfo(path).Length,
                    metaBytes = File.Exists(path + ".meta") ? new FileInfo(path + ".meta").Length : 0,
                    model = IsModel(path), resourcesRoot = resourceRoots.Contains(path), runtimeDependency = runtime.Contains(path),
                    generatorInput = directInputs.Contains(path), generatorDependency = generator.Contains(path), keepDocumentation = IsDocumentation(path) };
                if (entry.runtimeDependency)
                {
                    entry.category = entry.generatorDependency ? "runtime-and-generator" : "runtime";
                    entry.recommendation = entry.resourcesRoot ? "保留：Resources 资源会进入构建候选，即使仅通过字符串加载。" : "保留：场景或 Resources 的递归依赖。";
                    report.runtimeAssetCount++;
                }
                else if (entry.generatorDependency)
                {
                    entry.category = "generator-only";
                    entry.recommendation = "保留在工程：重新生成美术需要；当前运行资源未引用该源文件。";
                    report.generatorOnlyAssetCount++;
                }
                else if (entry.keepDocumentation)
                {
                    entry.category = "documentation";
                    entry.recommendation = "保留许可证和来源说明；未被运行引用不代表可以删除。";
                }
                else
                {
                    entry.category = report.generatorManifestAvailable ? "unreferenced-candidate" : "needs-generator-review";
                    entry.recommendation = report.generatorManifestAvailable
                        ? "可清理候选：未在已知运行或生成依赖中；确认无额外工具引用后可移出 Assets 归档，连同 .meta 保留。"
                        : "暂不能判断可清理：生成器输入清单尚不可用。";
                    if (report.generatorManifestAvailable) report.candidateCount++;
                }
                report.assets.Add(entry); report.sourceBytes += entry.sourceBytes;
            }
            report.assetCount = report.assets.Count;
            foreach (var group in report.assets.Where(x => x.path.StartsWith(ThirdPartyRoot, StringComparison.OrdinalIgnoreCase))
                .GroupBy(x => ThirdPartyRoot + x.path.Substring(ThirdPartyRoot.Length).Split('/')[0]).OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var package = new PackageEntry { directory = group.Key, sourceFileCount = group.Count(), sourceBytes = group.Sum(x => x.sourceBytes),
                    metaBytes = Directory.GetFiles(group.Key, "*.meta", SearchOption.AllDirectories).Sum(x => new FileInfo(x).Length), modelCount = group.Count(x => x.model),
                    runtimeModelCount = group.Count(x => x.model && x.runtimeDependency), generatorOnlyModelCount = group.Count(x => x.model && !x.runtimeDependency && x.generatorDependency) };
                package.unreferencedModels.AddRange(group.Where(x => x.model && !x.runtimeDependency && !x.generatorDependency).Select(x => x.path));
                report.unusedThirdPartyModelsCount += package.unreferencedModels.Count;
                report.thirdParty.Add(package);
            }
            string actorSource = ThirdPartyRoot + "KenneyMiniDungeon/Models/character-human.fbx";
            if (runtime.Contains(actorSource)) report.notes.Add("需检查：角色源 FBX 仍被运行资源引用。若预期只使用烘焙后的静态 mesh，请检查角色 prefab 是否残留源模型或动画引用。");
            report.notes.Add("范围仅 Assets/BoxLabArt；依赖根包括启用的构建场景、当前已打开场景对象及项目所有非 Editor Resources。未执行构建、导入或删除。");
            report.notes.Add("运行依赖是保守的源文件级闭包，不是最终 BuildReport：Unity 可剥离 FBX 内部未使用的动画/网格；原文件体积不等于包体体积。");
            report.notes.Add("生成器清单来自 ArtSetup.SourceAssetPaths。源 FBX、图集或着色器即使不进运行包，也可能是重新生成所必需；许可证与来源说明独立保留。");
            report.notes.Add("Resources 内文件默认计入构建候选，不能凭场景未引用就判为无用；动态地址系统和未登记的自定义编辑工具需另行确认。");
            return report;
        }

        static void ReadGeneratorManifest(AuditReport report)
        {
            var property = typeof(ArtSetup).GetProperty("SourceAssetPaths", BindingFlags.Public | BindingFlags.Static);
            if (property == null) { report.notes.Add("ArtSetup.SourceAssetPaths 尚未提供；不将未引用文件标为可清理候选。"); return; }
            var paths = property.GetValue(null, null) as IEnumerable<string>;
            if (paths == null) { report.notes.Add("ArtSetup.SourceAssetPaths 不是有效路径集合；请先修复生成器清单。"); return; }
            report.generatorManifestAvailable = true;
            report.generatorInputs = paths.Where(x => !string.IsNullOrWhiteSpace(x)).Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToList();
            foreach (string path in report.generatorInputs)
                if (!File.Exists(path)) { report.missingGeneratorInputs.Add(path); report.notes.Add("生成器缺少源文件：" + path); }
        }

        static HashSet<string> Closure(IEnumerable<string> roots)
        {
            var unique = new HashSet<string>(roots, StringComparer.OrdinalIgnoreCase);
            if (unique.Count > 0) unique.UnionWith(AssetDatabase.GetDependencies(unique.ToArray(), true).Select(Normalize));
            return unique;
        }
        static string Normalize(string path) { return (path ?? "").Replace('\\', '/'); }
        static bool IsResources(string path) { return path.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase) >= 0; }
        static bool RuntimeAssetPath(string path)
        {
            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || path.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            string extension = Path.GetExtension(path).ToLowerInvariant();
            return extension != ".cs" && extension != ".asmdef" && extension != ".asmref" && extension != ".meta";
        }
        static bool IsModel(string path)
        { return new[] { ".fbx", ".obj", ".blend", ".dae", ".3ds", ".max", ".ma", ".mb", ".c4d", ".gltf", ".glb" }.Contains(Path.GetExtension(path).ToLowerInvariant()); }
        static bool IsDocumentation(string path)
        {
            string name = Path.GetFileName(path).ToLowerInvariant(), extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".md" || name.StartsWith("license") || name.StartsWith("licence") || name.StartsWith("copying") || name.StartsWith("copyright") || name.StartsWith("notice") || name.StartsWith("readme");
        }
        static string Bytes(long bytes) { return bytes + " B (" + (bytes / 1048576d).ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + " MiB)"; }
        static string ToText(AuditReport r)
        {
            var text = new StringBuilder("BOXLAB ART ASSET AUDIT\n");
            text.AppendLine("UTC " + r.generatedUtc).AppendLine("Unity " + r.unityVersion).AppendLine("工程：" + r.projectDirectory);
            text.AppendLine("美术源文件：" + r.assetCount + "；运行依赖：" + r.runtimeAssetCount + "；仅生成使用：" + r.generatorOnlyAssetCount + "；待确认清理候选：" + r.candidateCount);
            text.AppendLine("unusedThirdPartyModelsCount=" + r.unusedThirdPartyModelsCount +
                (r.generatorManifestAvailable && r.missingGeneratorInputs.Count == 0 ? "（已排除运行依赖和生成器必需源模型）" : "（生成器清单缺失或输入不完整，需人工复核）"));
            text.AppendLine("源文件体积：" + Bytes(r.sourceBytes) + "；.meta：" + Bytes(r.metaBytes));
            text.AppendLine("\n第三方资源包（源文件统计，不是最终包体）");
            foreach (var package in r.thirdParty)
            {
                text.AppendLine(package.directory + " | " + Bytes(package.sourceBytes + package.metaBytes) + " | 文件 " + package.sourceFileCount + " | 模型 " + package.modelCount +
                    "（运行 " + package.runtimeModelCount + "，仅生成 " + package.generatorOnlyModelCount + "，未引用 " + package.unreferencedModels.Count + "）");
                foreach (string path in package.unreferencedModels) text.AppendLine("  未引用模型" + (r.generatorManifestAvailable ? "候选：" : "（生成清单缺失，待确认）：") + path);
            }
            text.AppendLine("\n生成器输入（即使不进包也要保留）");
            foreach (string path in r.generatorInputs) text.AppendLine((r.missingGeneratorInputs.Contains(path) ? "MISSING " : "KEEP ") + path);
            text.AppendLine("\n全部美术源文件（对应 .meta 体积见 JSON）");
            foreach (var entry in r.assets)
                text.AppendLine("[" + entry.category + "] " + entry.path + " | " + Bytes(entry.sourceBytes) + " | " + entry.recommendation);
            text.AppendLine("\n场景根"); foreach (string path in r.sceneRoots) text.AppendLine(path);
            text.AppendLine("\nResources 根（覆盖字符串加载）"); foreach (string path in r.resourceRoots) text.AppendLine(path);
            text.AppendLine("\n说明"); foreach (string note in r.notes) text.AppendLine("- " + note);
            return text.ToString();
        }
    }
}
#endif
