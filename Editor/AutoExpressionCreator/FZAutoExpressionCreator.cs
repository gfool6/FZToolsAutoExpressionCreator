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
using nadena.dev.modular_avatar.core;
namespace FZTools
{
    [Serializable]
    public class MenuToggleTargetInfo
    {
        public bool isActive;
        public GameObject targetObject;
        public string menuGroupName;
    }

    /// <summary>
    /// TODO: 全体的に書き直す
    /// Expression作成周りはMAで構築し、FXは表情機能のみに特化してMerge Animatorで構築する
    /// 一括選択・解除の追加
    /// </summary>
    public class FZAutoExpressionCreator : EditorWindow
    {
        [SerializeField]
        public GameObject avatar;

        [SerializeField] List<MenuToggleTargetInfo> menuToggleTargetInfo = new();

        string TargetAvatarName => avatar?.gameObject?.name;
        string OutputRootPath => $"{AssetUtils.OutputRootPath(TargetAvatarName)}";
        string AnimatorControllerOutputPath => $"{OutputRootPath}/AnimatorController";
        string AnimationClipOutputPath => $"{OutputRootPath}/AnimationClip";

        VRCAvatarDescriptor AvatarDescriptor => avatar.GetComponent<VRCAvatarDescriptor>();
        SkinnedMeshRenderer FaceMesh => AvatarDescriptor.GetVRCAvatarFaceMeshRenderer();
        List<Renderer> Renderers => AvatarDescriptor.GetComponentsInChildren<Renderer>(true).ToList();

        int PreviewSize => (int)Math.Round(position.size.x / 4);
        bool isAutoSet = false;

        bool[] ignoreShapeFlags = new bool[] { };

        Vector2 animClipScrollPos;
        Vector2 faceAnimScrollPos;

        FZPreviewRenderer previewRenderer;

        private const string SUBMENU_UNDEFINED = "未設定";


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
                    });
                    EUI.Space(2);
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
                        SetMenuToggleTargetInfo();
                    });
            EUI.Space();
            var tex = "・現在の表情シェイプをテンプレートにして表情アニメーションを一括作成\n"
                    + "・選択したメッシュやオブジェクトのON/OFFメニューを一括作成\n";
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
                EUI.Label("Meshes And Objects");
                EUI.Space();
                var serializedObject = new SerializedObject(this);
                serializedObject.Update();
                var props = serializedObject.FindProperty("menuToggleTargetInfo");
                for (int i = 0; i < props.arraySize; i++)
                {
                    var prop = props.GetArrayElementAtIndex(i);
                    prop.isExpanded = true;
                }
                EditorGUILayout.PropertyField(props, true);
                serializedObject.ApplyModifiedProperties();
            }
        }

        private void InitTogglesParamList()
        {
            ignoreShapeFlags = new bool[FaceMesh.sharedMesh.blendShapeCount];
            ignoreShapeFlags = Enumerable.Range(0, ignoreShapeFlags.Length).Select(i =>
            {
                var shapeName = FaceMesh.sharedMesh.GetBlendShapeName(i);
                var usesViseme = AvatarDescriptor.lipSync == VRCAvatarDescriptor.LipSyncStyle.VisemeBlendShape;
                var isBlendshapeEyelids = AvatarDescriptor.customEyeLookSettings.eyelidType == VRCAvatarDescriptor.EyelidType.Blendshapes;

                var visemeBlendShapes = usesViseme ? AvatarDescriptor?.VisemeBlendShapes?.ToList() : null;
                var eyelidsBlendShapes = isBlendshapeEyelids ? AvatarDescriptor.customEyeLookSettings.eyelidsBlendshapes.Select(index => FaceMesh.sharedMesh.GetBlendShapeName(index)).ToList() : null;
                return visemeBlendShapes.Contains(shapeName) || eyelidsBlendShapes.Contains(shapeName);
            }).ToArray();
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

        private void SetMenuToggleTargetInfo()
        {
            UnityEngine.Debug.Log("SetMenuToggleTargetInfo");
            UnityEngine.Debug.Log(avatar != null ? "avatar is not null" : "avatar is null");
            menuToggleTargetInfo = new();
            if (avatar != null)
            {
                Renderers.Select(r => r.gameObject).ToList().ForEach(o =>
                {
                    var info = new MenuToggleTargetInfo();
                    info.targetObject = o;
                    info.isActive = o.activeSelf;
                    info.menuGroupName = "";
                    menuToggleTargetInfo.Add(info);
                });
            }
        }

        private void CreateAll()
        {
            AssetUtils.CreateDirectoryRecursive(AnimatorControllerOutputPath);
            CreateTemplateFaceAnimations();

            var controllerFilePath = $"{AnimatorControllerOutputPath}/{TargetAvatarName}_FX.controller";
            CreateAnimatorController(controllerFilePath);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerFilePath);
            CreateAnimatorLayers(controller);
            CreateMergeAnimator(controller);
            CreateOnOffMenu();

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
            float denominator = FZToolsConstants.VRChat.HandGestures.Length * 2f + 15;
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
            Enumerable.Range(1, 15).ToList().ForEach(i =>
            {
                var ac = new AnimationClip();
                ac.AddBlendShape(FaceMesh, ignoreShapeNames);
                AssetUtils.CreateAsset(ac, $"{dirPath}/Face_{i}.anim");

                EUI.ShowProgress(progressCounter / denominator, "", "Create Face Animation");
                progressCounter++;
            });
            EUI.DismissProgress();
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
                var fx = AvatarDescriptor.baseAnimationLayers.First(item => item.type == VRCAvatarDescriptor.AnimLayerType.FX);
                AvatarDescriptor.baseAnimationLayers[AvatarDescriptor.baseAnimationLayers.ToList().IndexOf(fx)].animatorController = controller;
            }
        }

        private void CreateAnimatorLayers(AnimatorController controller)
        {
            EUI.ShowProgress(0, "", "Create Animator Layer");

            CreateFaceLayers(controller);
            EditorUtility.SetDirty(controller);
            EUI.ShowProgress(2f / 4f, "", "Create Animator Layer");

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
                controller.AddLayer(new AnimatorControllerLayer() { stateMachine = new AnimatorStateMachine(), name = ln, defaultWeight = isWriteDefaultFace ? 1 : 0 });

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

        private void CreateMergeAnimator(AnimatorController controller)
        {
            var mergeAnimator = new GameObject("FaceFX").AddComponent<ModularAvatarMergeAnimator>();
            mergeAnimator.transform.SetParent(avatar.transform);
            mergeAnimator.animator = controller;
            mergeAnimator.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            mergeAnimator.pathMode = MergeAnimatorPathMode.Absolute;
            mergeAnimator.mergeAnimatorMode = MergeAnimatorMode.Replace;
        }

        private void CreateOnOffMenu()
        {
            AddMAMenu();

            List<GameObject> subMenuObjects = new List<GameObject>();
            subMenuObjects.Add(AddSubMenu("表情"));
            menuToggleTargetInfo.Select(ro => ro.menuGroupName).Distinct().ToList().ForEach((menuName) =>
            {
                string name = menuName;
                if (name == "") name = SUBMENU_UNDEFINED;
                subMenuObjects.Add(AddSubMenu(name));
            });

            subMenuObjects.ForEach((subMenuObject) =>
            {
                // 表情メニュー
                if (subMenuObject.name == "表情")
                {
                    Enumerable.Range(1, 15).ToList().ForEach(i =>
                    {
                        string faceMenuObjName = $"{FZToolsConstants.VRChat.FaceMenu}_{i}";
                        // var faceMenuItem = new GameObject(faceMenuObjName);
                        AddMAMenuItem(subMenuObject, null, faceMenuObjName, FZToolsConstants.VRChat.FaceMenu, i);
                    });
                }
                else
                {
                    menuToggleTargetInfo.ToList().ForEach((ro) =>
                    {
                        if (ro.isActive)
                        {
                            if (ro.menuGroupName == subMenuObject.name)
                            {
                                AddMAMenuItem(subMenuObject, ro.targetObject, ro.targetObject.name, defaultValue: true);
                            }
                            else if (ro.menuGroupName == "" && subMenuObject.name == SUBMENU_UNDEFINED)
                            {
                                AddMAMenuItem(subMenuObject, ro.targetObject, ro.targetObject.name, defaultValue: true);
                            }
                        }
                    });
                }
            });
        }

        // TODO Core側にMAUtilみたいなのを作っとく方がいいかも
        GameObject ExMenuObject;
        private void AddMAMenu()
        {
            if (avatar == null)
            {
                UnityEngine.Debug.LogWarning("Target Avatarが指定されていません。");
                return;
            }

            ExMenuObject = new GameObject("Expression Menu");
            ExMenuObject.transform.SetParent(avatar.transform);
            ExMenuObject.AddComponent<ModularAvatarMenuInstaller>();
            ExMenuObject.AddComponent<ModularAvatarMenuGroup>();
        }

        private GameObject AddSubMenu(string name)
        {
            var subMenuObject = new GameObject(name);
            subMenuObject.transform.SetParent(ExMenuObject.transform);
            var item = subMenuObject.AddComponent<ModularAvatarMenuItem>();
            item.Control.type = VRCExpressionsMenu.Control.ControlType.SubMenu;
            item.automaticValue = true;
            item.MenuSource = SubmenuSource.Children;

            return subMenuObject;
        }

        private void AddMAMenuItem(GameObject parent, GameObject toggleTarget, string menuItemName, string paramName = null, int paramValue = 0, bool defaultValue = false)
        {
            var menuItemObject = new GameObject(menuItemName);
            menuItemObject.transform.SetParent(parent.transform);

            var menuItem = menuItemObject.AddComponent<ModularAvatarMenuItem>();
            menuItem.Control.type = VRCExpressionsMenu.Control.ControlType.Toggle;
            menuItem.isDefault = defaultValue;
            menuItem.automaticValue = true;
            if (paramName != null && paramName != "")
            {
                menuItem.Control.parameter = new VRCExpressionsMenu.Control.Parameter
                {
                    name = paramName,
                };
                menuItem.automaticValue = false;
                menuItem.Control.value = paramValue;
            }

            if (toggleTarget != null)
            {
                var objectToggle = menuItemObject.AddComponent<ModularAvatarObjectToggle>();
                var objRef = new AvatarObjectReference();
                objRef.Set(toggleTarget);
                objectToggle.Objects = new List<ToggledObject>
                {
                    new ToggledObject
                    {
                        Object = objRef,
                        Active = false,
                    }
                };
                objectToggle.Inverted = true;
            }
        }
    }
}
