using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Avatars.Components;
using EUI = FZTools.EditorUtils.UI;
using ELayout = FZTools.EditorUtils.Layout;
using UnityEditor.Animations;
using UnityEngine.Experimental.Rendering;
using static FZTools.FZToolsConstants;

namespace FZTools
{
    public class FZAutoExpressionCreator : EditorWindow
    {
        [SerializeField]
        public GameObject avatar;

        string TargetAvatarName => avatar?.gameObject?.name;
        string ExpressionsOutputPath => $"{AssetUtils.OutputRootPath(TargetAvatarName)}/Expressions";
        string AnimatorControllerOutputPath => $"{AssetUtils.OutputRootPath(TargetAvatarName)}/AnimatorController";
        string AnimationClipOutputPath => $"{AssetUtils.OutputRootPath(TargetAvatarName)}/AnimationClip";
        VRCAvatarDescriptor AvatarDescriptor => previewRenderer.Scene.GetRootGameObjects().Select(obj => obj.GetComponent<VRCAvatarDescriptor>()).FirstOrDefault();
        SkinnedMeshRenderer FaceMesh => AvatarDescriptor.GetVRCAvatarFaceMeshRenderer();
        List<Renderer> Renderers => AvatarDescriptor.GetComponentsInChildren<Renderer>(true).ToList();
        List<GameObject> ClothAndAccessoryRootObject => RenderersObjPath.Select(n => n.Split('/')).Where(n => n.Count() >= 2)
                                                                        .Select(n => string.Join("/", n.Take(n.Length - 1)))
                                                                        .Distinct().Select(o => AvatarDescriptor.transform.Find(o).gameObject).ToList();
        List<string> RenderersObjPath => Renderers.Select(e => e.gameObject.GetGameObjectPath(true)).ToList();
        List<string> ClothAndAccessoryRootObjPath => ClothAndAccessoryRootObject.Select(o => o.GetGameObjectPath(true)).ToList();
        int PreviewSize => (int)Math.Round(position.size.x / 4);

        bool[] ignoreShapeFlags = new bool[] { };
        bool[] onOffAnimationCreateTarget = new bool[] { };
        string[] expressionMenuNameGroup = new string[] { };
        Vector2 animClipScrollPos;
        Vector2 faceAnimScrollPos;
        Vector2 meshOnOffScrollPos;
        bool isAutoSet = false;
        FZPreviewRenderer previewRenderer;


        [MenuItem("FZTools/AutoExpressionCreator")]
        private static void OpenWindow()
        {
            var window = GetWindow<FZAutoExpressionCreator>();
            window.titleContent = new GUIContent("AutoExpressionCreator");
        }

        private void OnGUI()
        {
            ELayout.Horizontal(() =>
            {
                EUI.Space();
                ELayout.Vertical(() =>
                {
                    BaseUI();
                    ELayout.Scroll(ref animClipScrollPos, () =>
                    {
                        FaceAnimationCreateGUI();
                        EUI.Space();
                        EUI.Button(FZToolsConstants.LabelText.CreateFaceAnimationTemplate, CreateTemplateFaceAnimations);
                        MeshOnOffAnimationCreateGUI();
                        EUI.Space();
                        EUI.Button(FZToolsConstants.LabelText.CreateMeshOnOffAnimation, CreateMeshOnOffAnimations);
                    });
                    EUI.Space(2);
                    ELayout.Horizontal(() =>
                    {
                        EUI.Toggle(ref isAutoSet, GUILayout.Width(24));
                        EUI.LabelButton("作成されたFX・Expressionをアバターにセットする", () =>
                        {
                            isAutoSet = !isAutoSet;
                        });
                    });
                    EUI.Space(2);
                    EUI.Button("作成", CreateAll);
                    EUI.Space();
                });
                EUI.Space();
            });

        }

        private void BaseUI()
        {
            EUI.Space(2);
            EUI.Label("Target Avatar");
            EUI.Space();
            EUI.ChangeCheck(
                    () => EUI.ObjectField<GameObject>(ref avatar),
                    () =>
                    {
                        ResetParams();
                    });
            EUI.Space();
            var tex = "・現在の表情シェイプをテンプレートにして表情アニメーションを一括作成\n"
                    + "・衣装や小物を個別にオンオフするアニメーションの作成\n"
                    + "・作成したアニメーションでFXやExpressionの作成\n"
                    + "・作成されたFXやExpressionをアバターにセット";
            EUI.InfoBox(tex);
        }

        private void FaceAnimationCreateGUI()
        {
            if (avatar != null)
            {
                EUI.Space(2);
                EUI.Label("Face Preset");
                EUI.Space();

                ELayout.Horizontal(() =>
                {
                    Preview(PreviewSize);
                    ELayout.Scroll(ref faceAnimScrollPos, () =>
                    {
                        int count = FaceMesh.sharedMesh.blendShapeCount;
                        for (int i = 0; i < count; i++)
                        {
                            var shapeName = FaceMesh.sharedMesh.GetBlendShapeName(i);
                            ELayout.Horizontal(() =>
                            {
                                var temp = !ignoreShapeFlags[i];
                                EUI.ToggleWithLabel(ref temp, shapeName, GUILayout.Width(PreviewSize));
                                ignoreShapeFlags[i] = !temp;
                                float bsw = FaceMesh.GetBlendShapeWeight(i);
                                EUI.Slider(ref bsw, min: 0, max: 100, GUILayout.Width(PreviewSize * 1.5f));
                                FaceMesh.SetBlendShapeWeight(i, bsw);
                            });
                        }
                    }, 300);
                });

            }

            void Preview(int previewSize)
            {
                if (previewRenderer == null)
                {
                    previewRenderer = new FZPreviewRenderer(Instantiate(avatar));
                    InitTogglesParamList();
                }
                var headBone = AvatarDescriptor.gameObject.GetBoneRootObject().GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name.ToLower().Contains("head"));
                var headPosition = headBone.position + new Vector3(0, headBone.position.y * 0.04f, 1 * AvatarDescriptor.transform.localScale.z * headBone.localScale.z);
                previewRenderer.SetCameraPosition(headPosition);

                previewRenderer.RenderPreview(previewSize, previewSize);
                EditorGUILayout.LabelField(new GUIContent(previewRenderer.renderTexture), GUILayout.Width(previewSize), GUILayout.Height(previewSize));
                Repaint();
            }
        }

        private void MeshOnOffAnimationCreateGUI()
        {
            if (avatar != null)
            {
                EUI.Space(2);
                EUI.Label("Face Preset");
                EUI.Space();
                ELayout.Scroll(ref meshOnOffScrollPos, () =>
                {
                    EUI.Label("Meshes");
                    EUI.Space();
                    var countAcc = 0;
                    var meshCount = Renderers.Count();
                    for (int i = countAcc; i < meshCount; i++)
                    {
                        var mesh = Renderers[i];
                        ELayout.Horizontal(() =>
                        {
                            var temp = onOffAnimationCreateTarget[i];
                            EUI.ToggleWithLabel(ref temp, mesh.gameObject.GetGameObjectPath(true), GUILayout.Width(PreviewSize * 2.625f));
                            onOffAnimationCreateTarget[i] = temp;
                            var tempGn = expressionMenuNameGroup[i];
                            EUI.TextField(ref tempGn, GUILayout.Width(PreviewSize * 0.875f));
                            expressionMenuNameGroup[i] = tempGn;
                        });
                    }
                    countAcc += meshCount;

                    EUI.Space();
                    EUI.Label("GameObjects");
                    EUI.Space();
                    var carObj = ClothAndAccessoryRootObject;
                    var carObjCount = carObj.Count();
                    for (int i = countAcc; i < countAcc + carObjCount; i++)
                    {
                        var gameObject = carObj[i - countAcc];
                        ELayout.Horizontal(() =>
                        {
                            var temp = onOffAnimationCreateTarget[i];
                            EUI.ToggleWithLabel(ref temp, gameObject.GetGameObjectPath(true));
                            onOffAnimationCreateTarget[i] = temp;
                            var tempGn = expressionMenuNameGroup[i];
                            EUI.TextField(ref tempGn, GUILayout.MaxWidth(100));
                            expressionMenuNameGroup[i] = tempGn;
                        });
                    }
                    countAcc += carObjCount;
                }, 300);
            }
        }

        private void InitTogglesParamList()
        {
            ignoreShapeFlags = new bool[FaceMesh.sharedMesh.blendShapeCount];
            onOffAnimationCreateTarget = new bool[Renderers.Count + ClothAndAccessoryRootObject.Count];
            expressionMenuNameGroup = new string[Renderers.Count + ClothAndAccessoryRootObject.Count];

            ignoreShapeFlags = Enumerable.Range(0, ignoreShapeFlags.Length).Select(i =>
            {
                var shapeName = FaceMesh.sharedMesh.GetBlendShapeName(i);
                var usesViseme = AvatarDescriptor.lipSync == VRCAvatarDescriptor.LipSyncStyle.VisemeBlendShape;
                var isBlendshapeEyelids = AvatarDescriptor.customEyeLookSettings.eyelidType == VRCAvatarDescriptor.EyelidType.Blendshapes;

                var visemeBlendShapes = usesViseme ? AvatarDescriptor?.VisemeBlendShapes?.ToList() : null;
                var eyelidsBlendShapes = isBlendshapeEyelids ? AvatarDescriptor.customEyeLookSettings.eyelidsBlendshapes.Select(index => FaceMesh.sharedMesh.GetBlendShapeName(index)).ToList() : null;
                return visemeBlendShapes.Contains(shapeName) || eyelidsBlendShapes.Contains(shapeName);
            }).ToArray();
            onOffAnimationCreateTarget = Renderers.Select(r => r.gameObject.activeSelf).Concat(ClothAndAccessoryRootObject.Select(c => c.activeSelf)).ToArray();
            expressionMenuNameGroup = Renderers.Select(_ => "").Concat(ClothAndAccessoryRootObject.Select(_ => "")).ToArray();
        }

        private void ResetParams()
        {
            if (previewRenderer != null)
            {
                InitTogglesParamList();

                previewRenderer.EndPreview();
                previewRenderer = null;
            }

            if (avatar == null)
            {
                previewRenderer.EndPreview();
                previewRenderer = null;
            }

            Repaint();
        }

        private void CreateAll()
        {
            AssetUtils.CreateDirectoryRecursive(AnimatorControllerOutputPath);
            CreateTemplateFaceAnimations();
            CreateMeshOnOffAnimations();

            var controllerFilePath = $"{AnimatorControllerOutputPath}/{TargetAvatarName}_FX.controller";
            CreateAnimatorController(controllerFilePath);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerFilePath);

            CreateAnimatorLayers(controller);
            CreateExpressions(controller);

            avatar = null;
            previewRenderer.EndPreview();
            previewRenderer = null;
        }

        private void CreateTemplateFaceAnimations()
        {
            var dirPath = $"{AnimationClipOutputPath}/Face";
            AssetUtils.DeleteAndCreateDirectoryRecursive(dirPath);

            var ignoreShapeNames = ignoreShapeFlags.Select((ignore, index) => ignore ? FaceMesh.sharedMesh.GetBlendShapeName(index) : null)
                                                    .Where(e => e != null)
                                                    .ToList();

            EUI.ShowProgress(0, "", "Create Face Animation");
            float denominator = FZToolsConstants.VRChat.HandGestures.Length * 2f;
            float progressCounter = 0;
            FZToolsConstants.VRChat.HandGestures.ToList().ForEach(gesture =>
            {
                new List<String>() { "L", "R" }.ForEach(hand =>
                {
                    if (gesture == FZToolsConstants.VRChat.HandGesture.Neutral && hand.Equals("R")) return;
                    var ac = new AnimationClip();
                    ac.AddBlendShape(FaceMesh, ignoreShapeNames);
                    var fileName = $"{gesture}" + (gesture == FZToolsConstants.VRChat.HandGesture.Neutral ? ".anim" : $"_{hand}.anim");
                    AssetUtils.CreateAsset(ac, $"{dirPath}/{fileName}");

                    EUI.ShowProgress(progressCounter / denominator, "", "Create Face Animation");
                    progressCounter++;
                });
            });
            EUI.DismissProgress();
        }

        private void CreateMeshOnOffAnimations()
        {
            var dirPath = $"{AnimationClipOutputPath}/Expression";
            AssetUtils.DeleteAndCreateDirectoryRecursive(dirPath);

            var countAcc = 0;
            float denominator = RenderersObjPath.Count + ClothAndAccessoryRootObjPath.Count;
            EUI.ShowProgress(0, "", "Create OnOff Animation");
            countAcc += ExecCreateProcIfChecked(RenderersObjPath, dirPath, typeof(Renderer), countAcc);
            countAcc += ExecCreateProcIfChecked(ClothAndAccessoryRootObjPath, dirPath, typeof(GameObject), countAcc);
            CreateOnOffAllInOneAnimationClip(dirPath);
            EUI.DismissProgress();
        }

        private int ExecCreateProcIfChecked(List<string> meshPathList, string outputDir, Type type, int beginIndex)
        {
            float denominator = RenderersObjPath.Count + ClothAndAccessoryRootObjPath.Count;
            EUI.ShowProgress(0, "", "Create OnOff Animation");

            var mplCount = meshPathList.Count();
            for (int i = beginIndex; i < beginIndex + mplCount; i++)
            {
                var index = beginIndex > 0 ? i - beginIndex : i;
                if (onOffAnimationCreateTarget[i])
                {
                    CreateOnOffAnimationClip(outputDir, meshPathList[index], expressionMenuNameGroup[i], type);
                }

                EUI.ShowProgress(((float)i) / denominator, "", "Create OnOff Animation");
            }
            return mplCount;
        }

        private void CreateOnOffAnimationClip(string dirPath, string meshObjName, string dirName, Type meshType)
        {
            new List<(float kfVal, string onOff)>() { (1, "on"), (0, "off") }.ForEach(o =>
            {
                var ac = new AnimationClip();
                var tp = new List<(Type type, string propName)>()
                {
                    (typeof(GameObject), FZToolsConstants.AnimClipParam.GameObjectIsActive)
                };
                if (meshType == typeof(Renderer))
                    tp.Add((meshType, FZToolsConstants.AnimClipParam.MeshEnabled));

                tp.ForEach(t =>
                {
                    ac.AddAnimationCurve(new Keyframe(0, o.kfVal), meshObjName, t.propName, t.type);
                });

                var parts = meshObjName.Split('/');
                var fileBaseName = parts[parts.Count() - 1];

                dirName = dirName.isNullOrEmpty() ? "EmptyDirectory" : dirName;
                AssetUtils.CreateDirectoryRecursive($"{dirPath}/{dirName}");
                AssetUtils.CreateAsset(ac, $"{dirPath}/{dirName}/{fileBaseName}_{o.onOff}.anim");
            });
        }

        private void CreateOnOffAllInOneAnimationClip(string dirPath)
        {
            var propPair = new List<(Type type, string propName)>()
            {
                (typeof(GameObject), FZToolsConstants.AnimClipParam.GameObjectIsActive),
                (typeof(Renderer), FZToolsConstants.AnimClipParam.MeshEnabled)
            };
            var onOffPair = new List<(float kfVal, string onOff)>() { (1, "on"), (0, "off") };

            onOffPair.ForEach(o =>
            {
                var ac = new AnimationClip();
                var meshPaths = new List<List<string>>() { RenderersObjPath, ClothAndAccessoryRootObjPath };

                for (int i = 0; i < meshPaths.Count(); i++)
                {
                    var pathList = meshPaths[i];
                    pathList.ForEach(meshObjPath =>
                    {
                        propPair.ForEach(t =>
                        {
                            if (i == meshPaths.Count() - 1 && t.propName.Equals(FZToolsConstants.AnimClipParam.MeshEnabled)) return;
                            ac.AddAnimationCurve(new Keyframe(0, o.kfVal), meshObjPath, t.propName, t.type);
                        });
                    });
                }
                AssetUtils.CreateAsset(ac, $"{dirPath}/AUTOCREATE_ALL_{o.onOff}.anim");
            });
        }

        private void CreateAnimatorController(string outputFilePath)
        {
            var controller = AnimatorController.CreateAnimatorControllerAtPath(outputFilePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            while (!AssetDatabase.Contains(controller))
            {
                Debug.LogWarning("wait");
            }
            if (isAutoSet)
            {
                var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
                var fx = descriptor.baseAnimationLayers.First(item => item.type == VRCAvatarDescriptor.AnimLayerType.FX);
                descriptor.baseAnimationLayers[descriptor.baseAnimationLayers.ToList().IndexOf(fx)].animatorController = controller;
            }
        }

        private void CreateAnimatorLayers(AnimatorController controller)
        {
            float denominator = RenderersObjPath.Count + ClothAndAccessoryRootObjPath.Count;
            EUI.ShowProgress(0, "", "Create Animator Layer");

            CreateFaceLayers(controller);
            EditorUtility.SetDirty(controller);
            EUI.ShowProgress(1f / 4f, "", "Create Animator Layer");

            CreateIsMMDLayer(controller);
            EditorUtility.SetDirty(controller);
            EUI.ShowProgress(2f / 4f, "", "Create Animator Layer");

            CreateClothesLayers(controller);
            EditorUtility.SetDirty(controller);
            EUI.ShowProgress(3f / 4f, "", "Create Animator Layer");

            CreateQuickOkigaeLayer(controller);
            EditorUtility.SetDirty(controller);
            EUI.ShowProgress(4f / 4f, "", "Create Animator Layer");

            EUI.ShowProgress(4f / 4f, "", "Create Animator Layer");
            EUI.DismissProgress();
        }

        private void CreateFaceLayers(AnimatorController controller)
        {
            controller.AddParameter(new AnimatorControllerParameter() { name = FZToolsConstants.VRChat.GestureLeft, type = AnimatorControllerParameterType.Int, defaultInt = 0 });
            controller.AddParameter(new AnimatorControllerParameter() { name = FZToolsConstants.VRChat.GestureRight, type = AnimatorControllerParameterType.Int, defaultInt = 0 });
            controller.AddParameter(new AnimatorControllerParameter() { name = FZToolsConstants.VRChat.FaceMenu, type = AnimatorControllerParameterType.Int, defaultInt = 0 });

            new List<string>() { "Face_WD", "Face" }.ForEach(ln =>
            {
                var isWriteDefaultFace = ln.Contains("_WD");
                controller.AddLayer(new AnimatorControllerLayer() { stateMachine = new AnimatorStateMachine(), name = ln, defaultWeight = isWriteDefaultFace ? 0 : 1 });

                var layer = controller.layers.FirstOrDefault(l => l.name == ln);
                var stateMachine = layer.stateMachine;

                // ハンドジェスチャー
                new List<string>() { FZToolsConstants.VRChat.GestureLeft, FZToolsConstants.VRChat.GestureRight }.ForEach(pn =>
                {
                    FZToolsConstants.VRChat.HandGestures.ToList().ForEach(gesture =>
                    {
                        var addingAssetList = new List<UnityEngine.Object>();
                        var lr = pn.Replace("Gesture", "")[0];
                        var isNeutral = gesture == FZToolsConstants.VRChat.HandGesture.Neutral;
                        if (isNeutral && $"{lr}".Equals("R")) return;

                        var animFilename = $"{gesture.ToString()}" + (gesture == FZToolsConstants.VRChat.HandGesture.Neutral ? $".anim" : $"_{lr}.anim");
                        var animationClip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{AnimationClipOutputPath}/Face/{animFilename}");

                        var state = stateMachine.AddState(animationClip.name);
                        var entryTransition = stateMachine.AddEntryTransition(state);
                        var exitTransition = state.AddExitTransition();

                        entryTransition.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Equals, (int)gesture, pn);
                        entryTransition.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Equals, 0, FZToolsConstants.VRChat.FaceMenu);
                        exitTransition.AddCondition(UnityEditor.Animations.AnimatorConditionMode.NotEqual, (int)gesture, pn);
                        // 絶対これもっとスマートな方法あるんだよなぁ（半ギレ）
                        if (isNeutral && $"{lr}".Equals("L"))
                        {
                            entryTransition.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Equals, (int)gesture, FZToolsConstants.VRChat.GestureRight);
                        }
                        addingAssetList = new List<UnityEngine.Object>() { state, entryTransition, exitTransition };
                        if (isNeutral && $"{lr}".Equals("L"))
                        {
                            // OR条件にしないと一生Neutralのまま
                            var rightExitTransition = state.AddExitTransition();
                            rightExitTransition.AddCondition(UnityEditor.Animations.AnimatorConditionMode.NotEqual, (int)gesture, FZToolsConstants.VRChat.GestureRight);

                            addingAssetList.Add(rightExitTransition);
                        }
                        
                        var faceMenuExitTransition = state.AddExitTransition();
                        faceMenuExitTransition.AddCondition(UnityEditor.Animations.AnimatorConditionMode.NotEqual, 0, FZToolsConstants.VRChat.FaceMenu);
                        addingAssetList.Add(faceMenuExitTransition);

                        state.motion = animationClip;
                        state.writeDefaultValues = isWriteDefaultFace;

                        AssetUtils.AddAllObjectToAsset(addingAssetList, controller);
                    });
                });

                // 表情メニュー
                Enumerable.Range(1,15).ToList().ForEach(i =>
                {
                    var state = stateMachine.AddState($"FaceMenu_{i}");
                    var entryTransition = stateMachine.AddEntryTransition(state);
                    var exitTransition = state.AddExitTransition();

                    entryTransition.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Equals, i, FZToolsConstants.VRChat.FaceMenu);
                    exitTransition.AddCondition(UnityEditor.Animations.AnimatorConditionMode.NotEqual, i, FZToolsConstants.VRChat.FaceMenu);

                    state.motion = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{AnimationClipOutputPath}/Face/Face_{i}.anim");
                    state.writeDefaultValues = isWriteDefaultFace;

                    AssetUtils.AddAllObjectToAsset(new List<UnityEngine.Object>() { state, entryTransition, exitTransition }, controller);
                });

                AssetDatabase.AddObjectToAsset(stateMachine, controller);
            });
        }

        private void CreateIsMMDLayer(AnimatorController controller)
        {
            var paramAndLayerName = "IsMMD";
            controller.AddParameter(new AnimatorControllerParameter()
            {
                name = paramAndLayerName,
                type = AnimatorControllerParameterType.Bool,
                defaultBool = false
            });
            controller.AddLayer(new AnimatorControllerLayer()
            {
                stateMachine = new AnimatorStateMachine(),
                name = paramAndLayerName,
                defaultWeight = 1
            });
            var layer = controller.layers.FirstOrDefault(l => l.name == paramAndLayerName);
            var stateMachine = layer.stateMachine;

            new List<bool>()
            {
                true, false
            }.ForEach(isOn =>
            {
                var stateName = isOn ? "on" : "off";
                var state = stateMachine.AddState(stateName);

                var entryTransition = stateMachine.AddEntryTransition(state);
                var exitTransition = state.AddExitTransition();

                var entryMode = isOn ? UnityEditor.Animations.AnimatorConditionMode.If : UnityEditor.Animations.AnimatorConditionMode.IfNot;
                var exitMode = isOn ? UnityEditor.Animations.AnimatorConditionMode.IfNot : UnityEditor.Animations.AnimatorConditionMode.If;
                entryTransition.AddCondition(entryMode, 0, paramAndLayerName);
                exitTransition.AddCondition(exitMode, 0, paramAndLayerName);
                if (!isOn)
                {
                    stateMachine.defaultState = state;
                }
                state.writeDefaultValues = true;

                // StateMachineBehaviourをAddするためにこの段階でAddObjectToAsset
                AssetUtils.AddAllObjectToAsset(new List<UnityEngine.Object>() { state, entryTransition, exitTransition }, controller);

                var layerControlFace = state.AddStateMachineBehaviour<VRCAnimatorLayerControl>();
                layerControlFace.playable = VRCAnimatorLayerControl.BlendableLayer.FX;
                layerControlFace.blendDuration = 0;
                layerControlFace.layer = 2;
                layerControlFace.goalWeight = isOn ? 0 : 1;

                var layerControlFaceMMD = state.AddStateMachineBehaviour<VRCAnimatorLayerControl>();
                layerControlFaceMMD.playable = VRCAnimatorLayerControl.BlendableLayer.FX;
                layerControlFaceMMD.blendDuration = 0;
                layerControlFaceMMD.layer = 1;
                layerControlFaceMMD.goalWeight = isOn ? 1 : 0;

                controller.SetStateEffectiveBehaviours(state, 3, new List<VRCAnimatorLayerControl>() { layerControlFace, layerControlFaceMMD }.ToArray());
            });

            AssetDatabase.AddObjectToAsset(layer.stateMachine, controller);
        }

        private void CreateClothesLayers(AnimatorController controller)
        {
            var clothesAnimDirs = System.IO.Directory.GetDirectories($"{AnimationClipOutputPath}/Expression")
                                                        .Select(d => System.IO.Directory.GetFiles(d))
                                                        .SelectMany(s => s)
                                                        .Select(s => s.Replace("\\", "/"));
            var clothesAnimPaths = clothesAnimDirs.Where(n => n.Contains(".anim") && !n.Contains(".meta")).ToList();

            clothesAnimPaths.ForEach(acp =>
            {
                var animationClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(acp);
                var splitPaths = acp.Split('/');
                var name = $"{splitPaths.Take(splitPaths.Length - 1).Last()}_{animationClip.name}".Replace($"{TargetAvatarName}_", "");
                var paramAndLayerName = name.Replace("_on", "").Replace("_off", "");
                if (paramAndLayerName.Contains("AUTOCREATE_ALL"))
                {
                    return;
                }

                var isFirst = false;
                if (controller.parameters.FirstOrDefault(p => p.name == paramAndLayerName) == null)
                {
                    controller.AddParameter(new AnimatorControllerParameter()
                    {
                        name = paramAndLayerName,
                        type = AnimatorControllerParameterType.Bool,
                        defaultBool = true
                    });
                    controller.AddLayer(new AnimatorControllerLayer()
                    {
                        stateMachine = new AnimatorStateMachine(),
                        name = paramAndLayerName,
                        defaultWeight = 1
                    });
                    isFirst = true;
                }

                var layer = controller.layers.FirstOrDefault(l => l.name == paramAndLayerName);
                var stateMachine = layer.stateMachine;

                var state = stateMachine.AddState(name);
                var entryTransition = stateMachine.AddEntryTransition(state);
                var exitTransition = state.AddExitTransition();
                if (name.Contains("_on"))
                {
                    entryTransition.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0, paramAndLayerName);
                    exitTransition.AddCondition(UnityEditor.Animations.AnimatorConditionMode.IfNot, 0, paramAndLayerName);
                    stateMachine.defaultState = state;
                }
                if (name.Contains("_off"))
                {
                    entryTransition.AddCondition(UnityEditor.Animations.AnimatorConditionMode.IfNot, 0, paramAndLayerName);
                    exitTransition.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0, paramAndLayerName);
                }
                entryTransition.name = name;
                state.motion = animationClip;
                state.writeDefaultValues = true;

                AssetUtils.AddAllObjectToAsset(new List<UnityEngine.Object>() { state, entryTransition, exitTransition }, controller);
                if (!isFirst)
                {
                    AssetDatabase.AddObjectToAsset(layer.stateMachine, controller);
                }
            });
        }

        private void CreateQuickOkigaeLayer(AnimatorController controller)
        {
            var paramAndLayerName = "QuickOkigae";
            controller.AddParameter(new AnimatorControllerParameter()
            {
                name = paramAndLayerName,
                type = AnimatorControllerParameterType.Int,
                defaultInt = 0
            });
            controller.AddLayer(new AnimatorControllerLayer()
            {
                stateMachine = new AnimatorStateMachine(),
                name = paramAndLayerName,
                defaultWeight = 1
            });

            var layer = controller.layers.FirstOrDefault(l => l.name == paramAndLayerName);
            var stateMachine = layer.stateMachine;

            var states = new List<AnimatorState>(){
                stateMachine.AddState("Empty"), stateMachine.AddState("PutOn"), stateMachine.AddState("CastOff")
            };

            states.ForEach(state =>
            {
                var isPutOn = state.name == "PutOn";
                var isCastOff = state.name == "CastOff";
                var param = isPutOn ? 1 : isCastOff ? 99 : 0;

                var entryTransition = stateMachine.AddEntryTransition(state);
                var exitTransition = state.AddExitTransition();

                entryTransition.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Equals, param, paramAndLayerName);
                exitTransition.AddCondition(UnityEditor.Animations.AnimatorConditionMode.NotEqual, param, paramAndLayerName);

                if (isPutOn)
                {
                    stateMachine.defaultState = state;
                }
                state.writeDefaultValues = true;

                AssetUtils.AddAllObjectToAsset(new List<UnityEngine.Object>() { state, entryTransition, exitTransition }, controller);

                if (param == 0) return;

                var parameterDriver = (VRCAvatarParameterDriver)state.AddStateMachineBehaviour(typeof(VRCAvatarParameterDriver));
                var addingParams = new List<VRC.SDKBase.VRC_AvatarParameterDriver.Parameter>();
                controller.parameters.ToList().ForEach(cp =>
                {
                    if (cp.name.Equals("GestureLeft") || cp.name.Equals("GestureRight") || cp.name.Equals("IsMMD")) return;

                    addingParams.Add(new VRC.SDKBase.VRC_AvatarParameterDriver.Parameter()
                    {
                        type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set,
                        name = cp.name,
                        value = isPutOn && !isCastOff ? 1 : 0
                    });
                });

                addingParams.Add(new VRC.SDKBase.VRC_AvatarParameterDriver.Parameter()
                {
                    type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set,
                    name = "QuickOkigae",
                    value = 0
                });

                parameterDriver.parameters = addingParams;
            });
            AssetDatabase.AddObjectToAsset(layer.stateMachine, controller);
        }

        private void CreateExpressions(AnimatorController controller)
        {
            var dirPath = $"{ExpressionsOutputPath}";
            AssetUtils.DeleteAndCreateDirectoryRecursive(dirPath);

            EUI.ShowProgress(0f / 4f, "", "Create Animator Layer");
            CreateExpressionParameters(controller, dirPath);
            EUI.ShowProgress(1f / 4f, "", "Create Animator Layer");
            CreateExpressionMenu(dirPath);
            EUI.ShowProgress(2f / 4f, "", "Create Animator Layer");
            EditorUtility.SetDirty(controller);

            EUI.ShowProgress(3f / 4f, "", "Create Animator Layer");
            EUI.ShowProgress(4f / 4f, "", "Create Animator Layer");
            EUI.DismissProgress();
        }

        private void CreateExpressionParameters(AnimatorController controller, string outputPath)
        {
            var expressionParam = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            var paramList = new List<VRCExpressionParameters.Parameter>();
            controller.parameters.ToList().ForEach(p =>
            {
                var param = new VRCExpressionParameters.Parameter()
                {
                    name = p.name,
                    saved = true,
                    networkSynced = true
                };
                switch (p.type)
                {
                    case AnimatorControllerParameterType.Bool:
                        param.valueType = VRCExpressionParameters.ValueType.Bool;
                        param.defaultValue = p.name.Equals("IsMMD") ? 0 : 1;
                        break;
                    case AnimatorControllerParameterType.Int:
                        param.valueType = VRCExpressionParameters.ValueType.Int;
                        param.defaultValue = 0;
                        break;
                    case AnimatorControllerParameterType.Float:
                        param.valueType = VRCExpressionParameters.ValueType.Float;
                        param.defaultValue = 0;
                        break;
                    case AnimatorControllerParameterType.Trigger:
                    default:
                        break;
                }
                paramList.Add(param);
            });
            expressionParam.parameters = paramList.ToArray();
            AssetDatabase.CreateAsset(expressionParam, $"{outputPath}/ExpressionParams.asset");
            if (isAutoSet)
            {
                var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
                descriptor.expressionParameters = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>($"{outputPath}/ExpressionParams.asset");
            }
        }

        private void CreateExpressionMenu(string outputPath)
        {
            var expressionMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            var mainMenuControls = new List<VRCExpressionsMenu.Control>();

            // isMMD
            mainMenuControls.Add(new VRCExpressionsMenu.Control()
            {
                name = "IsMMD",
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter() { name = "IsMMD" }
            });

            // subMenues
            var clothesAnimDirPaths = System.IO.Directory.GetDirectories($"{AnimationClipOutputPath}/Expression").Select(s => s.convertWinPath2Path()).ToList();
            var subMenuAndMenuItems = clothesAnimDirPaths.ToDictionary(
                dp => dp.Split('/').Last(),
                dp => System.IO.Directory.GetFiles(dp).Select(s => s.convertWinPath2Path())
                                            .Where(n => n.Contains(".anim") && !n.Contains(".meta"))
                                            .Select(s =>
                                            {
                                                var sp = s.Split('/');
                                                var dn = sp.Take(sp.Length - 1).Last();
                                                var fn = sp.Last().Replace(".anim", "").Replace("_on", "").Replace("_off", "");
                                                return $"{dn}_{fn}".Replace($"{TargetAvatarName}_", "");
                                            }).Distinct().ToList()
            );
            subMenuAndMenuItems.ToList().ForEach(kvp =>
            {
                var subMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                subMenu.name = kvp.Key;

                var subMenuControls = new List<VRCExpressionsMenu.Control>();
                kvp.Value.ForEach(v =>
                {
                    subMenuControls.Add(new VRCExpressionsMenu.Control()
                    {
                        name = v,
                        type = VRCExpressionsMenu.Control.ControlType.Toggle,
                        parameter = new VRCExpressionsMenu.Control.Parameter() { name = v }
                    });
                });
                subMenu.controls = subMenuControls;
                AssetUtils.CreateAsset(subMenu, $"{outputPath}/subMenu_{subMenu.name}.asset");

                mainMenuControls.Add(new VRCExpressionsMenu.Control()
                {
                    name = subMenu.name.Replace("subMenu_", ""),
                    type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = subMenu
                });
            });

            // QuickOkigae-Castoff
            var subMenuQO = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            subMenuQO.name = "QuickOkigae";
            var subMenuQOControls = new List<VRCExpressionsMenu.Control>();
            new List<string>() { "Empty", "PutOn", "CastOff" }.ForEach(v =>
            {
                if (v.Equals("Empty"))
                {
                    return;
                }
                subMenuQOControls.Add(new VRCExpressionsMenu.Control()
                {
                    name = v,
                    type = VRCExpressionsMenu.Control.ControlType.Toggle,
                    parameter = new VRCExpressionsMenu.Control.Parameter() { name = "QuickOkigae", },
                    value = v.Equals("PutOn") ? 1 : v.Equals("CastOff") ? 99 : 0
                });
            });
            subMenuQO.controls = subMenuQOControls;
            AssetUtils.CreateAsset(subMenuQO, $"{outputPath}/subMenu_{subMenuQO.name}.asset");
            mainMenuControls.Add(new VRCExpressionsMenu.Control()
            {
                name = subMenuQO.name.Replace("subMenu_", ""),
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                subMenu = subMenuQO
            });

            expressionMenu.controls = mainMenuControls;
            AssetUtils.CreateAsset(expressionMenu, $"{outputPath}/ExpressionMenu.asset");
            if (isAutoSet)
            {
                var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
                descriptor.expressionsMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>($"{outputPath}/ExpressionMenu.asset");
            }
        }
    }
}
