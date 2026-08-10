using System;
using System.IO;
using MLOmega.XR.Core;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MLOmega.XR.Editor
{
    /// <summary>
    /// Creates a disposable provider-gate scene as a copy of XrealProduct.
    /// The source product scene is opened read-only and is never saved.
    /// </summary>
    public static class AugmentedRealityGateSceneBuilder
    {
        public const string GateScenePath =
            "Assets/Scenes/Generated/AugmentedRealityProviderGate.unity";

        [MenuItem("MLOmega/Augmented Reality/Build XREAL Provider Gate Scene")]
        public static void BuildXrealProviderGateScene()
        {
            string sourcePath = PhoneOnlySceneBuilder.XrealScenePath;
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException(
                    "Build XrealProduct first; the gate only clones a proven product scene.",
                    sourcePath);

            Directory.CreateDirectory(Path.GetDirectoryName(GateScenePath));
            UnityEngine.SceneManagement.Scene source = EditorSceneManager.OpenScene(
                sourcePath,
                OpenSceneMode.Single);
            if (!EditorSceneManager.SaveScene(source, GateScenePath, true))
                throw new IOException("Could not clone the XREAL product scene.");

            UnityEngine.SceneManagement.Scene gateScene = EditorSceneManager.OpenScene(
                GateScenePath,
                OpenSceneMode.Single);
            var root = new GameObject("AR Provider Gate (disposable)");
            var probe = root.AddComponent<AugmentedRealityCapabilityProbe>();
            var gate = root.AddComponent<AugmentedRealityRuntimeGate>();
            Assign(gate, "_autoStart", true);
            Assign(gate, "_expectedProvider", "xreal_provider");
            Assign(gate, "_requireEyeFrames", true);
            Assign(gate, "_requireTransport", true);
            Assign(gate, "_requireArFoundation", true);
            Assign(gate, "_startArFoundationManagers", true);

            Camera camera = Camera.main;
            if (camera == null)
                throw new MissingComponentException(
                    "XrealProduct clone has no MainCamera.");
            AddOverlay(camera.transform, gate);

            if (!EditorSceneManager.SaveScene(gateScene, GateScenePath))
                throw new IOException("Could not save the provider gate scene.");
            AssetDatabase.Refresh();
            Debug.Log(
                $"[AugmentedRealityGate] Generated isolated scene: {GateScenePath}");
        }

        private static void AddOverlay(
            Transform camera,
            AugmentedRealityRuntimeGate gate)
        {
            var canvasObject = new GameObject("AR Gate Canvas");
            canvasObject.transform.SetParent(camera, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasObject.AddComponent<UnityEngine.UI.CanvasScaler>();
            canvasObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(900f, 650f);
            canvasRect.localScale = Vector3.one * 0.0012f;
            canvasRect.localPosition = new Vector3(-0.55f, 0.25f, 1.5f);

            var textObject = new GameObject("AR Gate Status");
            RectTransform textRect = textObject.AddComponent<RectTransform>();
            textRect.SetParent(canvasRect, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(24f, 24f);
            textRect.offsetMax = new Vector2(-24f, -24f);
            var label = textObject.AddComponent<TextMeshProUGUI>();
            label.fontSize = 30f;
            label.color = new Color(0.75f, 1f, 0.95f, 1f);
            label.alignment = TextAlignmentOptions.TopLeft;
            label.text = "AR PROVIDER GATE\ninitializing...";

            var overlay = canvasObject.AddComponent<AugmentedRealityGateOverlay>();
            Assign(overlay, "_label", label);
            Assign(overlay, "_gate", gate);
        }

        private static void Assign(
            UnityEngine.Object target,
            string property,
            object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty field = serialized.FindProperty(property);
            if (field == null)
                throw new MissingFieldException(target.GetType().Name, property);
            switch (value)
            {
                case bool boolValue:
                    field.boolValue = boolValue;
                    break;
                case string stringValue:
                    field.stringValue = stringValue;
                    break;
                case UnityEngine.Object objectValue:
                    field.objectReferenceValue = objectValue;
                    break;
                default:
                    throw new System.ArgumentException(
                        $"Unsupported serialized gate value: {value?.GetType()}");
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
