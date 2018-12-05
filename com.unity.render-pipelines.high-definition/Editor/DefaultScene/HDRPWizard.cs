using UnityEditor;
using UnityEngine;
using System;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine.Experimental.Rendering.HDPipeline;
using UnityEngine.Rendering;
using System.Linq;

namespace UnityEditor.Experimental.Rendering.HDPipeline
{
    public class HDWizard : EditorWindow
    {

        //reflect internal legacy enum
        enum LightmapEncodingQualityCopy
        {
            Low = 0,
            Normal = 1,
            High = 2
        }

        static class Style
        {
            public static readonly GUIContent ok = EditorGUIUtility.TrTextContent("OK");
            public static readonly GUIContent resolve = EditorGUIUtility.TrTextContent("Resolve");
            public static readonly GUIContent resolveAll = EditorGUIUtility.TrTextContent("Resolve All");
            public static readonly GUIContent allConfigurationLabel = EditorGUIUtility.TrTextContent("HDRP configuration:");
            public static readonly GUIContent allConfigurationError = EditorGUIUtility.TrTextContent("There is issue in your configuration. (See below for detail)");
            public static readonly GUIContent colorSpaceLabel = EditorGUIUtility.TrTextContent("Color space:");
            public static readonly GUIContent colorSpaceError = EditorGUIUtility.TrTextContent("Only linear color space supported!");
            public static readonly GUIContent lightmapLabel = EditorGUIUtility.TrTextContent("Lightmap encoding:");
            public static readonly GUIContent lightmapError = EditorGUIUtility.TrTextContent("Only high quality lightmap supported!");
            public static readonly GUIContent shadowLabel = EditorGUIUtility.TrTextContent("Shadows:");
            public static readonly GUIContent shadowError = EditorGUIUtility.TrTextContent("Shadow must be set to activated! (either on hard or soft)");
            public static readonly GUIContent shadowMaskLabel = EditorGUIUtility.TrTextContent("Shadowmask mode:");
            public static readonly GUIContent shadowMaskError = EditorGUIUtility.TrTextContent("Only distance shadowmask supported at the project level! (You can still change this per light which is supported.)");
            public static readonly GUIContent scriptingRuntimeVersionLabel = EditorGUIUtility.TrTextContent("Script runtime version:");
            public static readonly GUIContent scriptingRuntimeVersionError = EditorGUIUtility.TrTextContent("Script runtime version must be .Net 4.x or earlier!");
            public static readonly GUIContent hdrpAssetLabel = EditorGUIUtility.TrTextContent("HDRP asset configuration:");
            public static readonly GUIContent hdrpAssetError = EditorGUIUtility.TrTextContent("There are issues in the HDRP asset configuration. (see below)");
            public static readonly GUIContent hdrpAssetUsedLabel = EditorGUIUtility.TrTextContent("Assigned:");
            public static readonly GUIContent hdrpAssetUsedError = EditorGUIUtility.TrTextContent("There is no HDRP asset assigned to the renderpipeline!");
            public static readonly GUIContent hdrpAssetRuntimeResourcesLabel = EditorGUIUtility.TrTextContent("Runtime resources:");
            public static readonly GUIContent hdrpAssetRuntimeResourcesError = EditorGUIUtility.TrTextContent("There is an issue with the runtime resources!");
            public static readonly GUIContent hdrpAssetEditorResourcesLabel = EditorGUIUtility.TrTextContent("Runtime resources:");
            public static readonly GUIContent hdrpAssetEditorResourcesError = EditorGUIUtility.TrTextContent("There is an issue with the editor resources!");
            public static readonly GUIContent hdrpAssetDiffusionProfileLabel = EditorGUIUtility.TrTextContent("Diffusion profile:");
            public static readonly GUIContent hdrpAssetDiffusionProfileError = EditorGUIUtility.TrTextContent("There is no diffusion profile assigned in the HDRP asset!");
            public static readonly GUIContent defaultVolumeProfileLabel = EditorGUIUtility.TrTextContent("Default volume profile:");
            public static readonly GUIContent defaultVolumeProfileError = EditorGUIUtility.TrTextContent("Default volume profile must be set to save disk space and share settings!");
        }

        static VolumeProfile s_DefaultVolumeProfile;

        Vector2 scrollPos;

        static Func<BuildTargetGroup, LightmapEncodingQualityCopy> GetLightmapEncodingQualityForPlatformGroup;
        static Action<BuildTargetGroup, LightmapEncodingQualityCopy> SetLightmapEncodingQualityForPlatformGroup;

        static HDWizard()
        {
            Type playerSettingsType = typeof(PlayerSettings);
            Type LightEncodingQualityType = playerSettingsType.Assembly.GetType("UnityEditor.LightmapEncodingQuality");
            var qualityVariable = Expression.Variable(LightEncodingQualityType, "quality_internal");
            var buildTargetGroupParameter = Expression.Parameter(typeof(BuildTargetGroup), "platformGroup");
            var qualityParameter = Expression.Parameter(typeof(LightmapEncodingQualityCopy), "quality");
            var getLightmapEncodingQualityForPlatformGroupInfo = playerSettingsType.GetMethod("GetLightmapEncodingQualityForPlatformGroup", BindingFlags.Static | BindingFlags.NonPublic);
            var setLightmapEncodingQualityForPlatformGroupInfo = playerSettingsType.GetMethod("SetLightmapEncodingQualityForPlatformGroup", BindingFlags.Static | BindingFlags.NonPublic);
            var getLightmapEncodingQualityForPlatformGroupBlock = Expression.Block(
                new[] { qualityVariable },
                Expression.Assign(qualityVariable, Expression.Call(getLightmapEncodingQualityForPlatformGroupInfo, buildTargetGroupParameter)),
                Expression.Convert(qualityVariable, typeof(LightmapEncodingQualityCopy))
                );
            var setLightmapEncodingQualityForPlatformGroupBlock = Expression.Block(
                new[] { qualityVariable },
                Expression.Assign(qualityVariable, Expression.Convert(qualityParameter, LightEncodingQualityType)),
                Expression.Call(setLightmapEncodingQualityForPlatformGroupInfo, buildTargetGroupParameter, qualityVariable)
                );
            var getLightmapEncodingQualityForPlatformGroupLambda = Expression.Lambda<Func<BuildTargetGroup, LightmapEncodingQualityCopy>>(getLightmapEncodingQualityForPlatformGroupBlock, buildTargetGroupParameter);
            var setLightmapEncodingQualityForPlatformGroupLambda = Expression.Lambda<Action<BuildTargetGroup, LightmapEncodingQualityCopy>>(setLightmapEncodingQualityForPlatformGroupBlock, buildTargetGroupParameter, qualityParameter);
            GetLightmapEncodingQualityForPlatformGroup = getLightmapEncodingQualityForPlatformGroupLambda.Compile();
            SetLightmapEncodingQualityForPlatformGroup = setLightmapEncodingQualityForPlatformGroupLambda.Compile();
        }

        [MenuItem("Window/Analysis/HDRP Wizard", priority = 113)]
        static void OpenWindow()
        {
            var window = GetWindow<HDWizard>("HDRP Wizard");
        }

        void OnGUI()
        {
            scrollPos = GUILayout.BeginScrollView(scrollPos);
            DrawConfigInfo();
            GUILayout.EndScrollView();
        }

        void DrawConfigInfo()
        {
            GUILayout.BeginVertical("box", GUILayout.MinWidth(300), GUILayout.MaxWidth(800), GUILayout.ExpandWidth(true));
            DrawConfigInfoLine(
                Style.allConfigurationLabel,
                Style.allConfigurationError,
                Style.ok,
                Style.resolveAll,
                colorSpaceTester,
                colorSpaceResolver
                );
            ++EditorGUI.indentLevel;
            DrawConfigInfoLine(
                Style.colorSpaceLabel,
                Style.colorSpaceError,
                Style.ok,
                Style.resolve,
                colorSpaceTester,
                colorSpaceResolver
                );
            
            --EditorGUI.indentLevel;
            GUILayout.EndVertical();
        }

        void DrawConfigInfoLine(GUIContent label, GUIContent error, GUIContent ok, GUIContent button, Func<bool> tester, Action resolver)
        {
            const float k_IndentOffsetFactor = 15f;
            const float k_EndOffset = 5f;
            const float k_ResolveButtonWidth = 75;
            float indentOffset = EditorGUI.indentLevel * k_IndentOffsetFactor;
            
            Rect lineRect = GUILayoutUtility.GetRect(1, EditorGUIUtility.singleLineHeight);
            Rect labelRect = new Rect(lineRect.x + indentOffset, lineRect.y, EditorGUIUtility.labelWidth, lineRect.height);
            Rect resolveRect = new Rect(lineRect.x + lineRect.width - k_ResolveButtonWidth - k_EndOffset, lineRect.y + 2, k_ResolveButtonWidth, lineRect.height - 4);
            Rect statusRect = new Rect(lineRect.x + labelRect.width, lineRect.y, lineRect.width - labelRect.width - resolveRect.width - k_EndOffset, lineRect.height);

            GUI.Label(labelRect, label);
            Color previous = GUI.color;
            if (tester())
            {
                GUI.color = Color.green;
                GUI.Label(statusRect, ok);
            }
            else
            {
                GUI.color = Color.red;
                GUI.Label(statusRect, error);
            }
            GUI.color = previous;
            if (GUI.Button(resolveRect, button, EditorStyles.miniButton))
                resolver();
        }

        bool allTester() =>
            colorSpaceTester()
            | lightmapTester()
            | shadowTester()
            | shadowmaskTester()
            | scriptRuntimeVersionTester()
            | hdrpAssetTester()
            | defaultVolumeProfileTester();
        void allResolver()
        {
            if (colorSpaceTester())
                colorSpaceResolver();
            if (lightmapTester())
                lightmapResolver();
            if (shadowTester())
                shadowResolver();
            if (shadowmaskTester())
                shadowmaskResolver();
            if (scriptRuntimeVersionTester())
                scriptRuntimeVersionResolver();
            if (hdrpAssetTester())
                hdrpAssetResolver();
            if (defaultVolumeProfileTester())
                defaultVolumeProfileResolver();
        }

        bool hdrpAssetTester() =>
            hdrpAssetUsedTester()
            | hdrpAssetRuntimeResourcesTester()
            | hdrpAssetEditorResourcesTester()
            | hdrpAssetDiffusionProfileTester();
        void hdrpAssetResolver()
        {
            if (hdrpAssetUsedTester())
                hdrpAssetUsedResolver();
            if (hdrpAssetRuntimeResourcesTester())
                hdrpAssetRuntimeResourcesResolver();
            if (hdrpAssetEditorResourcesTester())
                hdrpAssetEditorResourcesResolver();
            if (hdrpAssetDiffusionProfileTester())
                hdrpAssetDiffusionProfileResolver();
        }

        bool colorSpaceTester() => PlayerSettings.colorSpace == ColorSpace.Linear;
        void colorSpaceResolver() => PlayerSettings.colorSpace = ColorSpace.Linear;

        bool lightmapTester()
        {
            return false;
        }
        void lightmapResolver() => PlayerSettings.colorSpace = ColorSpace.Linear; //TODO

        bool shadowTester()
        {
            bool result = true;
            int currentQuality = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length && result; ++i)
            {
                QualitySettings.SetQualityLevel(i, applyExpensiveChanges: false);
                result = QualitySettings.shadows == ShadowQuality.All;
            }
            QualitySettings.SetQualityLevel(currentQuality, applyExpensiveChanges: false);
            return result;
        }
        void shadowResolver()
        {
            int currentQuality = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; ++i)
            {
                QualitySettings.SetQualityLevel(i, applyExpensiveChanges: false);
                QualitySettings.shadows = ShadowQuality.All;
            }
            QualitySettings.SetQualityLevel(currentQuality, applyExpensiveChanges: false);
        }

        bool shadowmaskTester()
        {
            bool result = true;
            int currentQuality = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length && result; ++i)
            {
                QualitySettings.SetQualityLevel(i, applyExpensiveChanges: false);
                result = QualitySettings.shadowmaskMode == ShadowmaskMode.DistanceShadowmask;
            }
            QualitySettings.SetQualityLevel(currentQuality, applyExpensiveChanges: false);
            return result;
        }
        void shadowmaskResolver()
        {
            int currentQuality = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; ++i)
            {
                QualitySettings.SetQualityLevel(i, applyExpensiveChanges: false);
                QualitySettings.shadowmaskMode = ShadowmaskMode.DistanceShadowmask;
            }
            QualitySettings.SetQualityLevel(currentQuality, applyExpensiveChanges: false);
        }

        bool scriptRuntimeVersionTester() => PlayerSettings.scriptingRuntimeVersion == ScriptingRuntimeVersion.Latest;
        void scriptRuntimeVersionResolver() => PlayerSettings.scriptingRuntimeVersion = ScriptingRuntimeVersion.Latest;

        bool hdrpAssetUsedTester() => GraphicsSettings.renderPipelineAsset != null && GraphicsSettings.renderPipelineAsset is HDRenderPipelineAsset;
        void hdrpAssetUsedResolver()
        {
            //ask to use one or create one
        }

        bool hdrpAssetRuntimeResourcesTester() =>
            hdrpAssetUsedTester()
            || (GraphicsSettings.renderPipelineAsset as HDRenderPipelineAsset).renderPipelineResources != null;
        void hdrpAssetRuntimeResourcesResolver()
        {
            if (!hdrpAssetUsedTester())
                hdrpAssetUsedResolver();
            (GraphicsSettings.renderPipelineAsset as HDRenderPipelineAsset).renderPipelineResources
                = AssetDatabase.LoadAssetAtPath<RenderPipelineResources>(HDUtils.GetHDRenderPipelinePath() + "Runtime/RenderPipelineResources/HDRenderPipelineResources.asset");
        }

        bool hdrpAssetEditorResourcesTester() =>
            hdrpAssetUsedTester()
            || (GraphicsSettings.renderPipelineAsset as HDRenderPipelineAsset).renderPipelineEditorResources != null;
        void hdrpAssetEditorResourcesResolver()
        {
            if (!hdrpAssetUsedTester())
                hdrpAssetUsedResolver();
            (GraphicsSettings.renderPipelineAsset as HDRenderPipelineAsset).renderPipelineEditorResources
                = AssetDatabase.LoadAssetAtPath<HDRenderPipelineEditorResources>(HDUtils.GetHDRenderPipelinePath() + "Editor/RenderPipelineResources/HDRenderPipelineEditorResources.asset");
        }

        bool hdrpAssetDiffusionProfileTester() =>
            hdrpAssetUsedTester()
            || (GraphicsSettings.renderPipelineAsset as HDRenderPipelineAsset).diffusionProfileSettings != null;
        void hdrpAssetDiffusionProfileResolver()
        {
            if (!hdrpAssetUsedTester())
                hdrpAssetUsedResolver();
            //ask to use one or create one
        }
    }
}
