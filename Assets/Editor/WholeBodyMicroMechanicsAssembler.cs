using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace MiniIronMan.Editor
{
    public static class WholeBodyMicroMechanicsAssembler
    {
        public struct PrimDef
        {
            public string name;
            public PrimitiveType type;
            public Vector3 pos;
            public Vector3 rot;
            public Vector3 scale;
            public string matName;

            public PrimDef(string n, PrimitiveType t, Vector3 p, Vector3 r, Vector3 s, string m)
            {
                name = n; type = t; pos = p; rot = r; scale = s; matName = m;
            }
        }

        [MenuItem("MiniIronMan/Assemble Whole Body Micro Mechanics")]
        public static void Assemble()
        {
            var boxBot = GameObject.Find("BoxBot_Root");
            if (boxBot == null)
            {
                Debug.LogError("[MiniIronMan] BoxBot_Root not found in scene!");
                return;
            }

            var itGO = GameObject.Find("ImageTarget");
            if (itGO == null)
            {
                Debug.LogError("[MiniIronMan] ImageTarget not found in scene!");
                return;
            }

            // Load Materials
            Dictionary<string, Material> mats = new Dictionary<string, Material>();
            string[] matNames = {
                "Mat_IronMan_Red", "Mat_IronMan_Gold", "Mat_IronMan_Silver",
                "Mat_IronMan_DarkMetal", "Mat_CopperCoil", "Mat_EmissiveBlue",
                "Mat_EmissiveCyan"
            };
            foreach (var m in matNames)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/BoxBot/Materials/" + m + ".mat");
                if (mat != null) mats[m] = mat;
                else Debug.LogWarning("[MiniIronMan] Missing material: " + m);
            }

            // 1. Get or Create Subsystem Transforms
            Transform head = GetOrCreateChild(boxBot.transform, "Head", new Vector3(0f, 0.0820f, 0f), Quaternion.identity);
            Transform torso = boxBot.transform.Find("Torso");
            Transform leftArm = GetOrCreateChild(boxBot.transform, "LeftArmPivot", new Vector3(-0.0290f, 0.0560f, 0f), Quaternion.identity);
            Transform rightArm = GetOrCreateChild(boxBot.transform, "RightArmPivot", new Vector3(0.0290f, 0.0560f, 0f), Quaternion.identity);
            Transform pelvis = GetOrCreateChild(boxBot.transform, "Pelvis", new Vector3(0f, 0.0260f, 0f), Quaternion.identity);
            Transform leftLeg = GetOrCreateChild(boxBot.transform, "LeftLeg", new Vector3(-0.0110f, 0f, 0f), Quaternion.identity);
            Transform rightLeg = GetOrCreateChild(boxBot.transform, "RightLeg", new Vector3(0.0110f, 0f, 0f), Quaternion.identity);

            // ==========================================
            // 2. HEAD & NECK MICRO-MECHANICAL UPGRADE
            // ==========================================
            List<PrimDef> headMicroDefs = GetHeadMicroDefs();
            GameObject leftEye = null;
            GameObject rightEye = null;
            foreach (var def in headMicroDefs)
            {
                var go = SpawnOrUpdatePrim(def, head, mats);
                if (def.name == "LeftEye") leftEye = go;
                if (def.name == "RightEye") rightEye = go;
            }
            Debug.Log($"[MiniIronMan] Head upgraded. Total children: {head.childCount}");

            // ==========================================
            // 3. TORSO & REACTOR MICRO-MECHANICAL UPGRADE
            // ==========================================
            if (torso != null)
            {
                List<PrimDef> torsoMicroDefs = GetTorsoMicroDefs();
                foreach (var def in torsoMicroDefs)
                {
                    SpawnOrUpdatePrim(def, torso, mats);
                }
                Debug.Log($"[MiniIronMan] Torso upgraded. Total children: {torso.childCount}");
            }

            // ==========================================
            // 4. SHOULDERS & ARMS MULTI-JOINT HIERARCHY & MICRO-MECHANICAL UPGRADE
            // ==========================================
            // 1. Create hierarchical sub-joints
            Transform leftElbow = GetOrCreateChild(leftArm, "LeftElbowPivot", new Vector3(0f, -0.0172f, 0f), Quaternion.identity);
            Transform leftWrist = GetOrCreateChild(leftElbow, "LeftWristPivot", new Vector3(0f, -0.0183f, 0f), Quaternion.identity);
            Transform leftIrisHub = GetOrCreateChild(leftWrist, "LeftRepulsorIrisHub", new Vector3(0f, -0.0055f, -0.0031f), Quaternion.identity);

            Transform rightElbow = GetOrCreateChild(rightArm, "RightElbowPivot", new Vector3(0f, -0.0172f, 0f), Quaternion.identity);
            Transform rightWrist = GetOrCreateChild(rightElbow, "RightWristPivot", new Vector3(0f, -0.0183f, 0f), Quaternion.identity);
            Transform rightIrisHub = GetOrCreateChild(rightWrist, "RightRepulsorIrisHub", new Vector3(0f, -0.0055f, -0.0031f), Quaternion.identity);

            // Strip colliders from new pivots
            StripCollider(leftElbow);
            StripCollider(leftWrist);
            StripCollider(leftIrisHub);
            StripCollider(rightElbow);
            StripCollider(rightWrist);
            StripCollider(rightIrisHub);

            // 2. Reorganize existing scene arm primitives into the sub-joint hierarchy
            ReorganizeArmHierarchy(leftArm, leftElbow, leftWrist, leftIrisHub);
            ReorganizeArmHierarchy(rightArm, rightElbow, rightWrist, rightIrisHub);

            // 3. Spawn or update micro-mechanical primitives under their designated sub-joints
            List<PrimDef> armMicroDefs = GetArmMicroDefs();
            GameObject leftPalm = null;
            GameObject rightPalm = null;

            foreach (var def in armMicroDefs)
            {
                ArmSubJoint jointType = ClassifyArmPrimitive(def.name);
                Transform targetJoint = GetArmSubJointTransform(jointType, leftArm, leftElbow, leftWrist, leftIrisHub);
                Vector3 localPos = def.pos - GetSubJointArmOffset(jointType);
                PrimDef localDef = new PrimDef(def.name, def.type, localPos, def.rot, def.scale, def.matName);
                var go = SpawnOrUpdateArmPrim(localDef, targetJoint, leftArm, mats);
                if (def.name == "RepulsorPalm") leftPalm = go;
            }

            foreach (var def in armMicroDefs)
            {
                ArmSubJoint jointType = ClassifyArmPrimitive(def.name);
                Transform targetJoint = GetArmSubJointTransform(jointType, rightArm, rightElbow, rightWrist, rightIrisHub);
                Vector3 rightArmPos = new Vector3(-def.pos.x, def.pos.y, def.pos.z);
                Vector3 localPos = rightArmPos - GetSubJointArmOffset(jointType);
                Vector3 rightRot = new Vector3(def.rot.x, (360f - def.rot.y) % 360f, (360f - def.rot.z) % 360f);
                PrimDef rightDef = new PrimDef(def.name, def.type, localPos, rightRot, def.scale, def.matName);
                var go = SpawnOrUpdateArmPrim(rightDef, targetJoint, rightArm, mats);
                if (def.name == "RepulsorPalm") rightPalm = go;
            }

            // Find RepulsorPalm under the iris hub or wrist if not already set from microDefs
            if (leftPalm == null)
            {
                Transform lp = leftIrisHub.Find("RepulsorPalm") ?? leftWrist.Find("RepulsorPalm") ?? FindChildRecursive(leftArm, "RepulsorPalm");
                if (lp != null) leftPalm = lp.gameObject;
            }
            if (rightPalm == null)
            {
                Transform rp = rightIrisHub.Find("RepulsorPalm") ?? rightWrist.Find("RepulsorPalm") ?? FindChildRecursive(rightArm, "RepulsorPalm");
                if (rp != null) rightPalm = rp.gameObject;
            }

            Debug.Log($"[MiniIronMan] Arm hierarchy configured: LeftArmPivot ({leftArm.childCount} children, Elbow: {leftElbow.childCount}, Wrist: {leftWrist.childCount}, Iris: {leftIrisHub.childCount})");

            // ==========================================
            // 5. PELVIS & INGUINAL FLUIDICS UPGRADE
            // ==========================================
            List<PrimDef> pelvisDefs = GetPelvisMicroDefs();
            foreach (var def in pelvisDefs)
            {
                SpawnOrUpdatePrim(def, pelvis, mats);
            }
            Debug.Log($"[MiniIronMan] Pelvis upgraded. Total children: {pelvis.childCount}");

            // ==========================================
            // 6. LEGS & BOOTS MICRO-MECHANICAL UPGRADE
            // ==========================================
            List<PrimDef> legDefs = GetLegMicroDefs();
            GameObject leftSole = null;
            GameObject rightSole = null;
            foreach (var def in legDefs)
            {
                var go = SpawnOrUpdatePrim(def, leftLeg, mats);
                if (def.name == "Boot_Repulsor_Lens" || def.name == "Sole_Repulsor_Emitter_Front") leftSole = go;
            }
            foreach (var def in legDefs)
            {
                PrimDef rightDef = new PrimDef(
                    def.name, def.type,
                    new Vector3(-def.pos.x, def.pos.y, def.pos.z),
                    new Vector3(def.rot.x, (360f - def.rot.y) % 360f, (360f - def.rot.z) % 360f),
                    def.scale, def.matName
                );
                var go = SpawnOrUpdatePrim(rightDef, rightLeg, mats);
                if (def.name == "Boot_Repulsor_Lens" || def.name == "Sole_Repulsor_Emitter_Front") rightSole = go;
            }
            Debug.Log($"[MiniIronMan] LeftLeg: {leftLeg.childCount} children, RightLeg: {rightLeg.childCount} children");

            // ==========================================
            // 7. STRIP ALL COLLIDERS FROM VISUAL MESHES
            // ==========================================
            int strippedColliders = 0;
            foreach (var col in boxBot.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(col);
                strippedColliders++;
            }
            Debug.Log($"[MiniIronMan] Stripped {strippedColliders} colliders for 100% Physics.Raycast transparency.");

            // ==========================================
            // 8. WIRE BOXBOT CONTROLLER SERIALIZED FIELDS
            // ==========================================
            var controller = itGO.GetComponent<BoxBotController>();
            if (controller != null)
            {
                var so = new SerializedObject(controller);
                so.Update();

                // Wire eyes
                if (leftEye != null)
                {
                    var prop = so.FindProperty("leftEyeRenderer");
                    if (prop != null) prop.objectReferenceValue = leftEye.GetComponent<Renderer>();
                }
                if (rightEye != null)
                {
                    var prop = so.FindProperty("rightEyeRenderer");
                    if (prop != null) prop.objectReferenceValue = rightEye.GetComponent<Renderer>();
                }

                // Wire reactor core
                Transform reactorCoreT = torso != null ? torso.Find("ReactorCore") : null;
                if (reactorCoreT != null)
                {
                    var prop = so.FindProperty("reactorCoreRenderer");
                    if (prop != null) prop.objectReferenceValue = reactorCoreT.GetComponent<Renderer>();
                }

                // Wire palm repulsors
                if (leftPalm != null)
                {
                    var prop = so.FindProperty("leftRepulsorRenderer");
                    if (prop != null) prop.objectReferenceValue = leftPalm.GetComponent<Renderer>();
                }
                if (rightPalm != null)
                {
                    var prop = so.FindProperty("rightRepulsorRenderer");
                    if (prop != null) prop.objectReferenceValue = rightPalm.GetComponent<Renderer>();
                }

                // Wire sole repulsors
                if (leftSole != null)
                {
                    var prop = so.FindProperty("leftSoleRepulsorRenderer");
                    if (prop != null) prop.objectReferenceValue = leftSole.GetComponent<Renderer>();
                }
                if (rightSole != null)
                {
                    var prop = so.FindProperty("rightSoleRepulsorRenderer");
                    if (prop != null) prop.objectReferenceValue = rightSole.GetComponent<Renderer>();
                }

                // Ensure 5 mood materials
                string[] moodNames = { "Mat_EmissiveCyan", "Mat_EmissiveRed", "Mat_EmissiveGold", "Mat_EmissiveGreen", "Mat_EmissiveBlue" };
                var moodProp = so.FindProperty("moodMaterials");
                if (moodProp != null)
                {
                    moodProp.arraySize = moodNames.Length;
                    for (int i = 0; i < moodNames.Length; i++)
                    {
                        var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/BoxBot/Materials/" + moodNames[i] + ".mat");
                        moodProp.GetArrayElementAtIndex(i).objectReferenceValue = m;
                    }
                }

                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(controller);
                Debug.Log("[MiniIronMan] BoxBotController successfully wired with all 7 suit mood renderers!");
            }

            // ==========================================
            // 9. WIRE ACTION MODE SUBSYSTEMS
            // ==========================================
            WireActionModeSubsystems(boxBot, itGO);

            // Save Scene
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
            Debug.Log("[MiniIronMan] Whole-body micro-mechanical assembly complete! SampleScene.unity saved.");
        }

        [MenuItem("MiniIronMan/Reorganize Arm Sub-Joints Now")]
        public static void ReorganizeArmSubJointsMenu()
        {
            var boxBot = GameObject.Find("BoxBot_Root");
            if (boxBot == null)
            {
                Debug.LogError("[MiniIronMan] BoxBot_Root not found in scene!");
                return;
            }

            Transform leftArm = boxBot.transform.Find("LeftArmPivot");
            Transform rightArm = boxBot.transform.Find("RightArmPivot");

            if (leftArm != null)
            {
                Transform leftElbow = GetOrCreateChild(leftArm, "LeftElbowPivot", new Vector3(0f, -0.0172f, 0f), Quaternion.identity);
                Transform leftWrist = GetOrCreateChild(leftElbow, "LeftWristPivot", new Vector3(0f, -0.0183f, 0f), Quaternion.identity);
                Transform leftIrisHub = GetOrCreateChild(leftWrist, "LeftRepulsorIrisHub", new Vector3(0f, -0.0055f, -0.0031f), Quaternion.identity);
                StripCollider(leftElbow); StripCollider(leftWrist); StripCollider(leftIrisHub);
                ReorganizeArmHierarchy(leftArm, leftElbow, leftWrist, leftIrisHub);
            }

            if (rightArm != null)
            {
                Transform rightElbow = GetOrCreateChild(rightArm, "RightElbowPivot", new Vector3(0f, -0.0172f, 0f), Quaternion.identity);
                Transform rightWrist = GetOrCreateChild(rightElbow, "RightWristPivot", new Vector3(0f, -0.0183f, 0f), Quaternion.identity);
                Transform rightIrisHub = GetOrCreateChild(rightWrist, "RightRepulsorIrisHub", new Vector3(0f, -0.0055f, -0.0031f), Quaternion.identity);
                StripCollider(rightElbow); StripCollider(rightWrist); StripCollider(rightIrisHub);
                ReorganizeArmHierarchy(rightArm, rightElbow, rightWrist, rightIrisHub);
            }

            var itGO = GameObject.Find("ImageTarget");
            if (itGO != null) WireActionModeSubsystems(boxBot, itGO);

            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
            Debug.Log("[MiniIronMan] Arm sub-joints successfully reorganized and SampleScene saved!");
        }

        [MenuItem("MiniIronMan/Wire Action Mode Subsystems")]
        public static void WireActionModeSubsystemsMenu()
        {
            var boxBot = GameObject.Find("BoxBot_Root");
            var itGO = GameObject.Find("ImageTarget");
            if (boxBot != null && itGO != null)
            {
                WireActionModeSubsystems(boxBot, itGO);
                UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
                Debug.Log("[MiniIronMan] Action Mode subsystems successfully wired and saved!");
            }
            else
            {
                Debug.LogError("[MiniIronMan] BoxBot_Root or ImageTarget not found in scene!");
            }
        }

        public static void WireActionModeSubsystems(GameObject boxBot, GameObject itGO)
        {
            // Attach subsystems to boxBot
            var motion = boxBot.GetComponent<BoxBotProceduralMotion>() ?? boxBot.AddComponent<BoxBotProceduralMotion>();
            var vfx = boxBot.GetComponent<BoxBotVFXController>() ?? boxBot.AddComponent<BoxBotVFXController>();
            var audio = boxBot.GetComponent<BoxBotAudioSynthesizer>() ?? boxBot.AddComponent<BoxBotAudioSynthesizer>();
            var sequencer = boxBot.GetComponent<BoxBotActionSequencer>() ?? boxBot.AddComponent<BoxBotActionSequencer>();

            // Wire BoxBotController
            var controller = itGO.GetComponent<BoxBotController>();
            if (controller != null)
            {
                var so = new SerializedObject(controller);
                so.Update();
                var seqProp = so.FindProperty("actionSequencer");
                if (seqProp != null) seqProp.objectReferenceValue = sequencer;
                var vfxProp = so.FindProperty("vfxController");
                if (vfxProp != null) vfxProp.objectReferenceValue = vfx;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(controller);
            }

            // Wire BoxBotActionSequencer
            {
                var so = new SerializedObject(sequencer);
                so.Update();
                var mProp = so.FindProperty("proceduralMotion");
                if (mProp != null) mProp.objectReferenceValue = motion;
                var vProp = so.FindProperty("vfxController");
                if (vProp != null) vProp.objectReferenceValue = vfx;
                var aProp = so.FindProperty("audioSynthesizer");
                if (aProp != null) aProp.objectReferenceValue = audio;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(sequencer);
            }

            // Wire BoxBotProceduralMotion
            {
                var so = new SerializedObject(motion);
                so.Update();
                so.FindProperty("headTransform").objectReferenceValue = boxBot.transform.Find("Head");
                so.FindProperty("torsoTransform").objectReferenceValue = boxBot.transform.Find("Torso");
                so.FindProperty("leftArmPivot").objectReferenceValue = boxBot.transform.Find("LeftArmPivot");
                so.FindProperty("rightArmPivot").objectReferenceValue = boxBot.transform.Find("RightArmPivot");
                so.FindProperty("pelvisTransform").objectReferenceValue = boxBot.transform.Find("Pelvis");
                so.FindProperty("leftLegTransform").objectReferenceValue = boxBot.transform.Find("LeftLeg");
                so.FindProperty("rightLegTransform").objectReferenceValue = boxBot.transform.Find("RightLeg");

                var torsoT = boxBot.transform.Find("Torso");
                if (torsoT != null)
                {
                    var lFlap = torsoT.Find("AirBrake_ScissorLink_Upper_L");
                    var rFlap = torsoT.Find("AirBrake_ScissorLink_Upper_R");
                    if (lFlap != null) so.FindProperty("leftAirBrakeShell").objectReferenceValue = lFlap;
                    if (rFlap != null) so.FindProperty("rightAirBrakeShell").objectReferenceValue = rFlap;
                }

                // Wire sub-joint serialized references
                var lArmT = boxBot.transform.Find("LeftArmPivot");
                var rArmT = boxBot.transform.Find("RightArmPivot");

                Transform lElbow = lArmT != null ? (lArmT.Find("LeftElbowPivot") ?? FindChildRecursive(lArmT, "LeftElbowPivot")) : null;
                Transform rElbow = rArmT != null ? (rArmT.Find("RightElbowPivot") ?? FindChildRecursive(rArmT, "RightElbowPivot")) : null;
                Transform lWrist = lElbow != null ? (lElbow.Find("LeftWristPivot") ?? FindChildRecursive(lArmT, "LeftWristPivot")) : null;
                Transform rWrist = rElbow != null ? (rElbow.Find("RightWristPivot") ?? FindChildRecursive(rArmT, "RightWristPivot")) : null;
                Transform lIrisHub = lWrist != null ? (lWrist.Find("LeftRepulsorIrisHub") ?? FindChildRecursive(lArmT, "LeftRepulsorIrisHub")) : null;
                Transform rIrisHub = rWrist != null ? (rWrist.Find("RightRepulsorIrisHub") ?? FindChildRecursive(rArmT, "RightRepulsorIrisHub")) : null;

                SetSerializedRef(so, "leftElbowPivot", lElbow);
                SetSerializedRef(so, "rightElbowPivot", rElbow);
                SetSerializedRef(so, "leftWristPivot", lWrist);
                SetSerializedRef(so, "rightWristPivot", rWrist);
                SetSerializedRef(so, "leftRepulsorIrisHub", lIrisHub);
                SetSerializedRef(so, "rightRepulsorIrisHub", rIrisHub);

                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(motion);
            }

            // Wire BoxBotVFXController
            {
                var so = new SerializedObject(vfx);
                so.Update();
                var s1 = Shader.Find("BoxBot/VFX/LaserEnergyBeam");
                var s2 = Shader.Find("BoxBot/VFX/EnergyRingCollimator");
                var s3 = Shader.Find("BoxBot/VFX/GroundHeatGlow");

                if (s1 != null) so.FindProperty("laserBeamShader").objectReferenceValue = s1;
                if (s2 != null) so.FindProperty("collimatorShader").objectReferenceValue = s2;
                if (s3 != null) so.FindProperty("groundGlowShader").objectReferenceValue = s3;

                var emissiveMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/BoxBot/Materials/Mat_EmissiveCyan.mat");
                if (emissiveMat != null) so.FindProperty("baseEmissiveTemplate").objectReferenceValue = emissiveMat;

                var torsoT = boxBot.transform.Find("Torso");
                if (torsoT != null)
                {
                    so.FindProperty("torsoTransform").objectReferenceValue = torsoT;
                    var core = torsoT.Find("ReactorCore");
                    if (core != null) so.FindProperty("reactorCoreTransform").objectReferenceValue = core;
                }

                var lArm = boxBot.transform.Find("LeftArmPivot");
                if (lArm != null)
                {
                    Transform lPalm = FindChildRecursive(lArm, "RepulsorPalm");
                    if (lPalm != null) SetSerializedRef(so, "leftRepulsorTransform", lPalm);
                }

                var rArm = boxBot.transform.Find("RightArmPivot");
                if (rArm != null)
                {
                    Transform rPalm = FindChildRecursive(rArm, "RepulsorPalm");
                    if (rPalm != null) SetSerializedRef(so, "rightRepulsorTransform", rPalm);
                }

                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(vfx);
            }

            if (boxBot.scene.IsValid())
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(boxBot.scene);
            }
        }

        private static Transform GetOrCreateChild(Transform parent, string name, Vector3 localPos, Quaternion localRot)
        {
            Transform t = parent.Find(name);
            if (t == null)
            {
                t = new GameObject(name).transform;
                t.SetParent(parent, false);
            }
            t.localPosition = localPos;
            t.localRotation = localRot;
            t.localScale = Vector3.one;
            return t;
        }

        private static GameObject SpawnOrUpdatePrim(PrimDef def, Transform parent, Dictionary<string, Material> mats)
        {
            Transform existing = parent.Find(def.name);
            GameObject go = existing != null ? existing.gameObject : GameObject.CreatePrimitive(def.type);
            go.name = def.name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = def.pos;
            go.transform.localEulerAngles = def.rot;
            go.transform.localScale = def.scale;

            Collider col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);

            if (mats.TryGetValue(def.matName, out Material mat))
            {
                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = mat;
            }
            return go;
        }

        public enum ArmSubJoint
        {
            ArmPivot,
            ElbowPivot,
            WristPivot,
            RepulsorIrisHub
        }

        private static ArmSubJoint ClassifyArmPrimitive(string n)
        {
            if (n.StartsWith("Repulsor_Iris_Blade_") ||
                n == "Repulsor_Iris_CopperRing" ||
                n == "Repulsor_Iris_FocusCollar" ||
                n == "RepulsorPalm")
            {
                return ArmSubJoint.RepulsorIrisHub;
            }

            if (n.StartsWith("Wrist_") ||
                n.StartsWith("Hand_") ||
                n.StartsWith("Knuckle_") ||
                n.StartsWith("Finger_") ||
                n.StartsWith("Thumb_") ||
                n.StartsWith("Repulsor_"))
            {
                return ArmSubJoint.WristPivot;
            }

            if (n.StartsWith("Elbow_") ||
                n.StartsWith("Gauntlet_") ||
                n.StartsWith("Missile_") ||
                n.StartsWith("Aero_Flight_") ||
                n.StartsWith("Targeting_Optic_"))
            {
                return ArmSubJoint.ElbowPivot;
            }

            return ArmSubJoint.ArmPivot;
        }

        private static Vector3 GetSubJointArmOffset(ArmSubJoint joint)
        {
            switch (joint)
            {
                case ArmSubJoint.ElbowPivot: return new Vector3(0f, -0.0172f, 0f);
                case ArmSubJoint.WristPivot: return new Vector3(0f, -0.0355f, 0f);
                case ArmSubJoint.RepulsorIrisHub: return new Vector3(0f, -0.0410f, -0.0031f);
                default: return Vector3.zero;
            }
        }

        private static Transform GetArmSubJointTransform(ArmSubJoint joint, Transform armPivot, Transform elbowPivot, Transform wristPivot, Transform irisHub)
        {
            switch (joint)
            {
                case ArmSubJoint.ElbowPivot: return elbowPivot;
                case ArmSubJoint.WristPivot: return wristPivot;
                case ArmSubJoint.RepulsorIrisHub: return irisHub;
                default: return armPivot;
            }
        }

        private static void ReorganizeArmHierarchy(Transform armPivot, Transform elbowPivot, Transform wristPivot, Transform irisHub)
        {
            var allChildren = new List<Transform>();
            foreach (Transform t in armPivot.GetComponentsInChildren<Transform>(true))
            {
                if (t == armPivot || t == elbowPivot || t == wristPivot || t == irisHub) continue;
                allChildren.Add(t);
            }

            foreach (var child in allChildren)
            {
                ArmSubJoint jointType = ClassifyArmPrimitive(child.name);
                Transform target = GetArmSubJointTransform(jointType, armPivot, elbowPivot, wristPivot, irisHub);

                if (child.parent != target)
                {
                    child.SetParent(target, true);
                }
            }
        }

        private static Transform FindChildRecursive(Transform parent, string name)
        {
            if (parent == null) return null;
            foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name) return t;
            }
            return null;
        }

        private static void StripCollider(Transform t)
        {
            if (t == null) return;
            Collider col = t.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
        }

        private static void SetSerializedRef(SerializedObject so, string propName, Object obj)
        {
            var prop = so.FindProperty(propName);
            if (prop != null)
            {
                prop.objectReferenceValue = obj;
            }
        }

        private static GameObject SpawnOrUpdateArmPrim(PrimDef def, Transform targetJoint, Transform armPivot, Dictionary<string, Material> mats)
        {
            Transform existing = targetJoint.Find(def.name);
            if (existing == null && armPivot != null)
            {
                existing = FindChildRecursive(armPivot, def.name);
            }
            GameObject go = existing != null ? existing.gameObject : GameObject.CreatePrimitive(def.type);
            go.name = def.name;
            go.transform.SetParent(targetJoint, false);
            go.transform.localPosition = def.pos;
            go.transform.localEulerAngles = def.rot;
            go.transform.localScale = def.scale;

            Collider col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);

            if (mats.TryGetValue(def.matName, out Material mat))
            {
                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = mat;
            }
            return go;
        }

        // =========================================================================
        // HEAD & NECK MICRO-MECHANICAL DEFINITIONS
        // =========================================================================
        private static List<PrimDef> GetHeadMicroDefs()
        {
            return new List<PrimDef>
            {
                // 1. 5-Layer Optical Wells
                new PrimDef("Eye_Socket_Bezel_Outer_L", PrimitiveType.Cube, new Vector3(-0.0075f, 0.0060f, 0.0212f), new Vector3(0f, 0f, 348f), new Vector3(0.0085f, 0.0034f, 0.0015f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Eye_Socket_Bezel_Outer_R", PrimitiveType.Cube, new Vector3(0.0075f, 0.0060f, 0.0212f), new Vector3(0f, 0f, 12f), new Vector3(0.0085f, 0.0034f, 0.0015f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Eye_LensRing_Silver_Bezel_L", PrimitiveType.Cube, new Vector3(-0.0075f, 0.0060f, 0.0217f), new Vector3(0f, 0f, 348f), new Vector3(0.0080f, 0.0028f, 0.0012f), "Mat_IronMan_Silver"),
                new PrimDef("Eye_LensRing_Silver_Bezel_R", PrimitiveType.Cube, new Vector3(0.0075f, 0.0060f, 0.0217f), new Vector3(0f, 0f, 12f), new Vector3(0.0080f, 0.0028f, 0.0012f), "Mat_IronMan_Silver"),
                new PrimDef("Eye_Iris_Antireflect_Trench_L", PrimitiveType.Cube, new Vector3(-0.0075f, 0.0060f, 0.0219f), new Vector3(0f, 0f, 348f), new Vector3(0.0076f, 0.0025f, 0.0008f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Eye_Iris_Antireflect_Trench_R", PrimitiveType.Cube, new Vector3(0.0075f, 0.0060f, 0.0219f), new Vector3(0f, 0f, 12f), new Vector3(0.0076f, 0.0025f, 0.0008f), "Mat_IronMan_DarkMetal"),
                new PrimDef("LeftEye", PrimitiveType.Cube, new Vector3(-0.0075f, 0.0060f, 0.0222f), new Vector3(0f, 0f, 348f), new Vector3(0.0072f, 0.0022f, 0.0008f), "Mat_EmissiveBlue"),
                new PrimDef("RightEye", PrimitiveType.Cube, new Vector3(0.0075f, 0.0060f, 0.0222f), new Vector3(0f, 0f, 12f), new Vector3(0.0072f, 0.0022f, 0.0008f), "Mat_EmissiveBlue"),
                new PrimDef("Eye_Pupil_Collimator_Core_L", PrimitiveType.Sphere, new Vector3(-0.0075f, 0.0060f, 0.0224f), new Vector3(0f, 0f, 348f), new Vector3(0.0020f, 0.0014f, 0.0008f), "Mat_EmissiveCyan"),
                new PrimDef("Eye_Pupil_Collimator_Core_R", PrimitiveType.Sphere, new Vector3(0.0075f, 0.0060f, 0.0224f), new Vector3(0f, 0f, 12f), new Vector3(0.0020f, 0.0014f, 0.0008f), "Mat_EmissiveCyan"),

                // 2. Temple Heat Gills
                new PrimDef("Temple_Vent_Trench_Bed_L", PrimitiveType.Cube, new Vector3(-0.0170f, 0.0088f, 0.0075f), new Vector3(12f, 345f, 10f), new Vector3(0.0018f, 0.0035f, 0.0140f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Temple_Vent_Trench_Bed_R", PrimitiveType.Cube, new Vector3(0.0170f, 0.0088f, 0.0075f), new Vector3(12f, 15f, 350f), new Vector3(0.0018f, 0.0035f, 0.0140f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Temple_GillSlat_01_Anterior_L", PrimitiveType.Cube, new Vector3(-0.0172f, 0.0094f, 0.0115f), new Vector3(28f, 345f, 10f), new Vector3(0.0014f, 0.0028f, 0.0008f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Temple_GillSlat_01_Anterior_R", PrimitiveType.Cube, new Vector3(0.0172f, 0.0094f, 0.0115f), new Vector3(28f, 15f, 350f), new Vector3(0.0014f, 0.0028f, 0.0008f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Temple_GillSlat_02_Mid_L", PrimitiveType.Cube, new Vector3(-0.0171f, 0.0088f, 0.0085f), new Vector3(28f, 345f, 10f), new Vector3(0.0014f, 0.0028f, 0.0008f), "Mat_IronMan_Silver"),
                new PrimDef("Temple_GillSlat_02_Mid_R", PrimitiveType.Cube, new Vector3(0.0171f, 0.0088f, 0.0085f), new Vector3(28f, 15f, 350f), new Vector3(0.0014f, 0.0028f, 0.0008f), "Mat_IronMan_Silver"),
                new PrimDef("Temple_GillSlat_03_Posterior_L", PrimitiveType.Cube, new Vector3(-0.0170f, 0.0082f, 0.0055f), new Vector3(28f, 345f, 10f), new Vector3(0.0014f, 0.0028f, 0.0008f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Temple_GillSlat_03_Posterior_R", PrimitiveType.Cube, new Vector3(0.0170f, 0.0082f, 0.0055f), new Vector3(28f, 15f, 350f), new Vector3(0.0014f, 0.0028f, 0.0008f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Temple_Air_Ram_Cowl_L", PrimitiveType.Cube, new Vector3(-0.0166f, 0.0108f, 0.0080f), new Vector3(12f, 345f, 10f), new Vector3(0.0010f, 0.0012f, 0.0150f), "Mat_IronMan_Gold"),
                new PrimDef("Temple_Air_Ram_Cowl_R", PrimitiveType.Cube, new Vector3(0.0166f, 0.0108f, 0.0080f), new Vector3(12f, 15f, 350f), new Vector3(0.0010f, 0.0012f, 0.0150f), "Mat_IronMan_Gold"),

                // 3. Sagittal Crest Seam Latches & Pins
                new PrimDef("Sagittal_Latch_Frontal", PrimitiveType.Cube, new Vector3(0f, 0.0216f, 0.0095f), new Vector3(8f, 0f, 0f), new Vector3(0.0036f, 0.0016f, 0.0028f), "Mat_IronMan_Silver"),
                new PrimDef("Sagittal_Pin_Frontal_L", PrimitiveType.Cylinder, new Vector3(-0.0012f, 0.0222f, 0.0095f), new Vector3(8f, 0f, 0f), new Vector3(0.0006f, 0.0008f, 0.0006f), "Mat_IronMan_Silver"),
                new PrimDef("Sagittal_Pin_Frontal_R", PrimitiveType.Cylinder, new Vector3(0.0012f, 0.0222f, 0.0095f), new Vector3(8f, 0f, 0f), new Vector3(0.0006f, 0.0008f, 0.0006f), "Mat_IronMan_Silver"),
                new PrimDef("Sagittal_Latch_Bregma_Mid", PrimitiveType.Cube, new Vector3(0f, 0.0228f, -0.0015f), new Vector3(6f, 0f, 0f), new Vector3(0.0038f, 0.0016f, 0.0030f), "Mat_IronMan_Silver"),
                new PrimDef("Sagittal_Pin_Bregma_L", PrimitiveType.Cylinder, new Vector3(-0.0013f, 0.0233f, -0.0015f), new Vector3(6f, 0f, 0f), new Vector3(0.0006f, 0.0008f, 0.0006f), "Mat_IronMan_Silver"),
                new PrimDef("Sagittal_Pin_Bregma_R", PrimitiveType.Cylinder, new Vector3(0.0013f, 0.0233f, -0.0015f), new Vector3(6f, 0f, 0f), new Vector3(0.0006f, 0.0008f, 0.0006f), "Mat_IronMan_Silver"),
                new PrimDef("Sagittal_Latch_Lambda_Rear", PrimitiveType.Cube, new Vector3(0f, 0.0218f, -0.0125f), new Vector3(355f, 0f, 0f), new Vector3(0.0036f, 0.0016f, 0.0028f), "Mat_IronMan_Silver"),
                new PrimDef("Sagittal_Pin_Lambda_L", PrimitiveType.Cylinder, new Vector3(-0.0012f, 0.0223f, -0.0125f), new Vector3(355f, 0f, 0f), new Vector3(0.0006f, 0.0008f, 0.0006f), "Mat_IronMan_Silver"),
                new PrimDef("Sagittal_Pin_Lambda_R", PrimitiveType.Cylinder, new Vector3(0.0012f, 0.0223f, -0.0125f), new Vector3(355f, 0f, 0f), new Vector3(0.0006f, 0.0008f, 0.0006f), "Mat_IronMan_Silver"),

                // 4. Glabella Targeting Hood & Optic Prisms
                new PrimDef("Brow_Targeting_Canopy_Hood", PrimitiveType.Cube, new Vector3(0f, 0.0152f, 0.0195f), new Vector3(22f, 0f, 0f), new Vector3(0.0075f, 0.0032f, 0.0040f), "Mat_IronMan_Red"),
                new PrimDef("Brow_Sensor_Recess_Bay", PrimitiveType.Cube, new Vector3(0f, 0.0142f, 0.0192f), new Vector3(22f, 0f, 0f), new Vector3(0.0058f, 0.0018f, 0.0025f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Brow_Targeting_Aperture_Core", PrimitiveType.Cylinder, new Vector3(0f, 0.0142f, 0.0200f), new Vector3(112f, 0f, 0f), new Vector3(0.0016f, 0.0008f, 0.0016f), "Mat_EmissiveCyan"),
                new PrimDef("Brow_Telemetry_Prism_L", PrimitiveType.Cube, new Vector3(-0.0022f, 0.0142f, 0.0194f), new Vector3(22f, 352f, 0f), new Vector3(0.0010f, 0.0012f, 0.0010f), "Mat_IronMan_Silver"),
                new PrimDef("Brow_Telemetry_Prism_R", PrimitiveType.Cube, new Vector3(0.0022f, 0.0142f, 0.0194f), new Vector3(22f, 8f, 0f), new Vector3(0.0010f, 0.0012f, 0.0010f), "Mat_IronMan_Silver"),

                // 5. Temporomandibular Micro-Hydraulic Rams
                new PrimDef("TMJ_Trunnion_Mount_Anchor_L", PrimitiveType.Cube, new Vector3(-0.0182f, -0.0040f, -0.0045f), new Vector3(25f, 345f, 8f), new Vector3(0.0022f, 0.0028f, 0.0026f), "Mat_IronMan_DarkMetal"),
                new PrimDef("TMJ_Trunnion_Mount_Anchor_R", PrimitiveType.Cube, new Vector3(0.0182f, -0.0040f, -0.0045f), new Vector3(25f, 15f, 352f), new Vector3(0.0022f, 0.0028f, 0.0026f), "Mat_IronMan_DarkMetal"),
                new PrimDef("TMJ_Hydraulic_Cylinder_Barrel_L", PrimitiveType.Cylinder, new Vector3(-0.0175f, -0.0068f, -0.0025f), new Vector3(35f, 335f, 12f), new Vector3(0.0016f, 0.0040f, 0.0016f), "Mat_IronMan_DarkMetal"),
                new PrimDef("TMJ_Hydraulic_Cylinder_Barrel_R", PrimitiveType.Cylinder, new Vector3(0.0175f, -0.0068f, -0.0025f), new Vector3(35f, 25f, 348f), new Vector3(0.0016f, 0.0040f, 0.0016f), "Mat_IronMan_DarkMetal"),
                new PrimDef("TMJ_Hydraulic_Piston_Ram_L", PrimitiveType.Cylinder, new Vector3(-0.0162f, -0.0098f, 0.0002f), new Vector3(35f, 335f, 12f), new Vector3(0.0010f, 0.0036f, 0.0010f), "Mat_IronMan_Silver"),
                new PrimDef("TMJ_Hydraulic_Piston_Ram_R", PrimitiveType.Cylinder, new Vector3(0.0162f, -0.0098f, 0.0002f), new Vector3(35f, 25f, 348f), new Vector3(0.0010f, 0.0036f, 0.0010f), "Mat_IronMan_Silver"),
                new PrimDef("TMJ_Mandibular_Gimbal_Ball_L", PrimitiveType.Sphere, new Vector3(-0.0152f, -0.0118f, 0.0022f), Vector3.zero, new Vector3(0.0018f, 0.0018f, 0.0018f), "Mat_IronMan_DarkMetal"),
                new PrimDef("TMJ_Mandibular_Gimbal_Ball_R", PrimitiveType.Sphere, new Vector3(0.0152f, -0.0118f, 0.0022f), Vector3.zero, new Vector3(0.0018f, 0.0018f, 0.0018f), "Mat_IronMan_DarkMetal"),

                // 6. Ear Acoustic Voice Coils & Telemetry Ports
                new PrimDef("Ear_Diaphragm_Chamber_Well_L", PrimitiveType.Cylinder, new Vector3(-0.0228f, 0f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.0084f, 0.0008f, 0.0084f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Ear_Diaphragm_Chamber_Well_R", PrimitiveType.Cylinder, new Vector3(0.0228f, 0f, 0f), new Vector3(0f, 0f, 270f), new Vector3(0.0084f, 0.0008f, 0.0084f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Ear_Acoustic_Coil_Diaphragm_L", PrimitiveType.Cylinder, new Vector3(-0.0233f, 0f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.0068f, 0.0006f, 0.0068f), "Mat_CopperCoil"),
                new PrimDef("Ear_Acoustic_Coil_Diaphragm_R", PrimitiveType.Cylinder, new Vector3(0.0233f, 0f, 0f), new Vector3(0f, 0f, 270f), new Vector3(0.0068f, 0.0006f, 0.0068f), "Mat_CopperCoil"),
                new PrimDef("Ear_Diagnostic_Port_Bezel_L", PrimitiveType.Cylinder, new Vector3(-0.0239f, 0f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.0042f, 0.0008f, 0.0042f), "Mat_IronMan_Silver"),
                new PrimDef("Ear_Diagnostic_Port_Bezel_R", PrimitiveType.Cylinder, new Vector3(0.0239f, 0f, 0f), new Vector3(0f, 0f, 270f), new Vector3(0.0042f, 0.0008f, 0.0042f), "Mat_IronMan_Silver"),
                new PrimDef("Ear_Diagnostic_Aperture_Core_L", PrimitiveType.Cylinder, new Vector3(-0.0244f, 0f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.0016f, 0.0004f, 0.0016f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Ear_Diagnostic_Aperture_Core_R", PrimitiveType.Cylinder, new Vector3(0.0244f, 0f, 0f), new Vector3(0f, 0f, 270f), new Vector3(0.0016f, 0.0004f, 0.0016f), "Mat_IronMan_DarkMetal"),

                // 7. Chin Moisture Purge & Exhaust Louvers
                new PrimDef("Chin_Exhaust_Cowl_Frame", PrimitiveType.Cube, new Vector3(0f, -0.0196f, 0.0142f), new Vector3(345f, 0f, 0f), new Vector3(0.0118f, 0.0022f, 0.0055f), "Mat_IronMan_Red"),
                new PrimDef("Chin_Exhaust_Plenum_Bed", PrimitiveType.Cube, new Vector3(0f, -0.0200f, 0.0146f), new Vector3(345f, 0f, 0f), new Vector3(0.0096f, 0.0014f, 0.0035f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Chin_Exhaust_Slat_Center", PrimitiveType.Cube, new Vector3(0f, -0.0200f, 0.0152f), new Vector3(345f, 0f, 0f), new Vector3(0.0010f, 0.0016f, 0.0022f), "Mat_IronMan_Silver"),
                new PrimDef("Chin_Exhaust_Slat_L", PrimitiveType.Cube, new Vector3(-0.0032f, -0.0200f, 0.0148f), new Vector3(345f, 352f, 6f), new Vector3(0.0010f, 0.0016f, 0.0022f), "Mat_IronMan_Silver"),
                new PrimDef("Chin_Exhaust_Slat_R", PrimitiveType.Cube, new Vector3(0.0032f, -0.0200f, 0.0148f), new Vector3(345f, 8f, 354f), new Vector3(0.0010f, 0.0016f, 0.0022f), "Mat_IronMan_Silver"),
                new PrimDef("Chin_Purge_Nozzle_Port_L", PrimitiveType.Cylinder, new Vector3(-0.0018f, -0.0202f, 0.0138f), new Vector3(75f, 0f, 0f), new Vector3(0.0012f, 0.0008f, 0.0012f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Chin_Purge_Nozzle_Port_R", PrimitiveType.Cylinder, new Vector3(0.0018f, -0.0202f, 0.0138f), new Vector3(75f, 0f, 0f), new Vector3(0.0012f, 0.0008f, 0.0012f), "Mat_IronMan_DarkMetal"),

                // 8. Cervical Spine & Accordion Bellows
                new PrimDef("Cervical_C1_Atlas_Rib", PrimitiveType.Cylinder, new Vector3(0f, -0.0138f, -0.0010f), new Vector3(4f, 0f, 0f), new Vector3(0.0168f, 0.0016f, 0.0168f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Cervical_Spacer_Disc_01", PrimitiveType.Cylinder, new Vector3(0f, -0.0148f, -0.0008f), new Vector3(4f, 0f, 0f), new Vector3(0.0152f, 0.0008f, 0.0152f), "Mat_IronMan_Silver"),
                new PrimDef("Cervical_C2_Axis_Rib", PrimitiveType.Cylinder, new Vector3(0f, -0.0158f, -0.0005f), new Vector3(3f, 0f, 0f), new Vector3(0.0164f, 0.0016f, 0.0164f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Cervical_Spacer_Disc_02", PrimitiveType.Cylinder, new Vector3(0f, -0.0168f, -0.0002f), new Vector3(3f, 0f, 0f), new Vector3(0.0148f, 0.0008f, 0.0148f), "Mat_IronMan_Silver"),
                new PrimDef("Cervical_C3_Base_Collar", PrimitiveType.Cylinder, new Vector3(0f, -0.0178f, 0f), new Vector3(2f, 0f, 0f), new Vector3(0.0160f, 0.0016f, 0.0160f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Cervical_Spine_Vertebra_Prominens", PrimitiveType.Cube, new Vector3(0f, -0.0158f, -0.0090f), new Vector3(350f, 0f, 0f), new Vector3(0.0055f, 0.0050f, 0.0032f), "Mat_IronMan_Gold"),

                // 9. Carotid Cryo-Cooling Lines & Clamps
                new PrimDef("Carotid_Coolant_Line_Upper_L", PrimitiveType.Capsule, new Vector3(-0.0088f, -0.0142f, 0.0035f), new Vector3(14f, 350f, 8f), new Vector3(0.0016f, 0.0045f, 0.0016f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Carotid_Coolant_Line_Upper_R", PrimitiveType.Capsule, new Vector3(0.0088f, -0.0142f, 0.0035f), new Vector3(14f, 10f, 352f), new Vector3(0.0016f, 0.0045f, 0.0016f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Carotid_Titanium_Clamp_Upper_L", PrimitiveType.Cube, new Vector3(-0.0090f, -0.0142f, 0.0038f), new Vector3(14f, 350f, 8f), new Vector3(0.0022f, 0.0012f, 0.0020f), "Mat_IronMan_Silver"),
                new PrimDef("Carotid_Titanium_Clamp_Upper_R", PrimitiveType.Cube, new Vector3(0.0090f, -0.0142f, 0.0038f), new Vector3(14f, 10f, 352f), new Vector3(0.0022f, 0.0012f, 0.0020f), "Mat_IronMan_Silver"),
                new PrimDef("Carotid_Coolant_Line_Lower_L", PrimitiveType.Capsule, new Vector3(-0.0092f, -0.0182f, 0.0042f), new Vector3(10f, 355f, 5f), new Vector3(0.0016f, 0.0048f, 0.0016f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Carotid_Coolant_Line_Lower_R", PrimitiveType.Capsule, new Vector3(0.0092f, -0.0182f, 0.0042f), new Vector3(10f, 5f, 355f), new Vector3(0.0016f, 0.0048f, 0.0016f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Carotid_Titanium_Clamp_Lower_L", PrimitiveType.Cube, new Vector3(-0.0094f, -0.0182f, 0.0045f), new Vector3(10f, 355f, 5f), new Vector3(0.0022f, 0.0012f, 0.0020f), "Mat_IronMan_Silver"),
                new PrimDef("Carotid_Titanium_Clamp_Lower_R", PrimitiveType.Cube, new Vector3(0.0094f, -0.0182f, 0.0045f), new Vector3(10f, 5f, 355f), new Vector3(0.0022f, 0.0012f, 0.0020f), "Mat_IronMan_Silver"),
                new PrimDef("Carotid_Manifold_Banjo_Fitting_L", PrimitiveType.Cylinder, new Vector3(-0.0084f, -0.0125f, 0.0032f), new Vector3(0f, 0f, 90f), new Vector3(0.0022f, 0.0010f, 0.0022f), "Mat_CopperCoil"),
                new PrimDef("Carotid_Manifold_Banjo_Fitting_R", PrimitiveType.Cylinder, new Vector3(0.0084f, -0.0125f, 0.0032f), new Vector3(0f, 0f, 90f), new Vector3(0.0022f, 0.0010f, 0.0022f), "Mat_CopperCoil"),

                // 10. Throat Universal Gimbal Yoke
                new PrimDef("Throat_Gimbal_Yoke_Fork", PrimitiveType.Cube, new Vector3(0f, -0.0165f, 0.0075f), new Vector3(350f, 0f, 0f), new Vector3(0.0110f, 0.0032f, 0.0042f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Throat_Pitch_Axle_Pin", PrimitiveType.Cylinder, new Vector3(0f, -0.0165f, 0.0075f), new Vector3(0f, 0f, 90f), new Vector3(0.0022f, 0.0118f, 0.0022f), "Mat_IronMan_Silver"),
                new PrimDef("Throat_Yaw_Spindle_Core", PrimitiveType.Cylinder, new Vector3(0f, -0.0175f, 0.0055f), new Vector3(350f, 0f, 0f), new Vector3(0.0035f, 0.0035f, 0.0035f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Throat_Laryngeal_Shield_Plate", PrimitiveType.Cube, new Vector3(0f, -0.0162f, 0.0098f), new Vector3(340f, 0f, 0f), new Vector3(0.0068f, 0.0038f, 0.0018f), "Mat_IronMan_Gold"),
                new PrimDef("Throat_Bilateral_Pivot_Cap_L", PrimitiveType.Cylinder, new Vector3(-0.0060f, -0.0165f, 0.0075f), new Vector3(0f, 0f, 90f), new Vector3(0.0028f, 0.0006f, 0.0028f), "Mat_IronMan_Silver"),
                new PrimDef("Throat_Bilateral_Pivot_Cap_R", PrimitiveType.Cylinder, new Vector3(0.0060f, -0.0165f, 0.0075f), new Vector3(0f, 0f, 90f), new Vector3(0.0028f, 0.0006f, 0.0028f), "Mat_IronMan_Silver")
            };
        }

        // =========================================================================
        // TORSO & ARC REACTOR MICRO-MECHANICAL DEFINITIONS
        // =========================================================================
        private static List<PrimDef> GetTorsoMicroDefs()
        {
            return new List<PrimDef>
            {
                // Sternal Canyon Studs & Bolts
                new PrimDef("Sternal_Stud_Upper_L", PrimitiveType.Cylinder, new Vector3(-0.0014f, 0.0122f, 0.0102f), new Vector3(90f, 0f, 0f), new Vector3(0.0007f, 0.0004f, 0.0007f), "Mat_IronMan_Silver"),
                new PrimDef("Sternal_Stud_Upper_R", PrimitiveType.Cylinder, new Vector3(0.0014f, 0.0122f, 0.0102f), new Vector3(90f, 0f, 0f), new Vector3(0.0007f, 0.0004f, 0.0007f), "Mat_IronMan_Silver"),
                new PrimDef("Sternal_Stud_Mid_L", PrimitiveType.Cylinder, new Vector3(-0.0022f, 0.0138f, 0.0108f), new Vector3(90f, 0f, 0f), new Vector3(0.00065f, 0.0004f, 0.00065f), "Mat_IronMan_Silver"),
                new PrimDef("Sternal_Stud_Mid_R", PrimitiveType.Cylinder, new Vector3(0.0022f, 0.0138f, 0.0108f), new Vector3(90f, 0f, 0f), new Vector3(0.00065f, 0.0004f, 0.00065f), "Mat_IronMan_Silver"),
                new PrimDef("Sternal_Stud_Lower_L", PrimitiveType.Cylinder, new Vector3(-0.0016f, 0.0012f, 0.0098f), new Vector3(90f, 0f, 0f), new Vector3(0.0007f, 0.0004f, 0.0007f), "Mat_IronMan_Silver"),
                new PrimDef("Sternal_Stud_Lower_R", PrimitiveType.Cylinder, new Vector3(0.0016f, 0.0012f, 0.0098f), new Vector3(90f, 0f, 0f), new Vector3(0.0007f, 0.0004f, 0.0007f), "Mat_IronMan_Silver"),
                new PrimDef("Sternal_Chassis_Bolt_L", PrimitiveType.Cylinder, new Vector3(-0.0012f, 0.0070f, 0.0088f), new Vector3(90f, 0f, 0f), new Vector3(0.0006f, 0.0004f, 0.0006f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Sternal_Chassis_Bolt_R", PrimitiveType.Cylinder, new Vector3(0.0012f, 0.0070f, 0.0088f), new Vector3(90f, 0f, 0f), new Vector3(0.0006f, 0.0004f, 0.0006f), "Mat_IronMan_DarkMetal"),

                // Supraclavicular RCS Thrusters
                new PrimDef("RCS_Nozzle_L_Bell", PrimitiveType.Cylinder, new Vector3(-0.0072f, 0.0160f, 0.0078f), new Vector3(335f, 0f, 15f), new Vector3(0.0022f, 0.0018f, 0.0022f), "Mat_IronMan_Silver"),
                new PrimDef("RCS_Nozzle_R_Bell", PrimitiveType.Cylinder, new Vector3(0.0072f, 0.0160f, 0.0078f), new Vector3(335f, 0f, 345f), new Vector3(0.0022f, 0.0018f, 0.0022f), "Mat_IronMan_Silver"),
                new PrimDef("RCS_Nozzle_L_Lip", PrimitiveType.Cylinder, new Vector3(-0.0075f, 0.0172f, 0.0083f), new Vector3(335f, 0f, 15f), new Vector3(0.0025f, 0.0005f, 0.0025f), "Mat_CopperCoil"),
                new PrimDef("RCS_Nozzle_R_Lip", PrimitiveType.Cylinder, new Vector3(0.0075f, 0.0172f, 0.0083f), new Vector3(335f, 0f, 345f), new Vector3(0.0025f, 0.0005f, 0.0025f), "Mat_CopperCoil"),
                new PrimDef("RCS_Manifold_Seat_L", PrimitiveType.Cube, new Vector3(-0.0072f, 0.0148f, 0.0074f), new Vector3(335f, 0f, 15f), new Vector3(0.0028f, 0.0012f, 0.0028f), "Mat_IronMan_DarkMetal"),
                new PrimDef("RCS_Manifold_Seat_R", PrimitiveType.Cube, new Vector3(0.0072f, 0.0148f, 0.0074f), new Vector3(335f, 0f, 345f), new Vector3(0.0028f, 0.0012f, 0.0028f), "Mat_IronMan_DarkMetal"),

                // Sub-Pectoral Tie-Bars
                new PrimDef("TieBar_Pectoral_Sup_L", PrimitiveType.Cylinder, new Vector3(-0.0092f, 0.0118f, 0.0090f), new Vector3(45f, 320f, 25f), new Vector3(0.0010f, 0.0036f, 0.0010f), "Mat_IronMan_Silver"),
                new PrimDef("TieBar_Pectoral_Sup_R", PrimitiveType.Cylinder, new Vector3(0.0092f, 0.0118f, 0.0090f), new Vector3(45f, 40f, 335f), new Vector3(0.0010f, 0.0036f, 0.0010f), "Mat_IronMan_Silver"),
                new PrimDef("TieBar_Pectoral_Sup_Clevis_L", PrimitiveType.Cube, new Vector3(-0.0070f, 0.0130f, 0.0068f), Vector3.zero, new Vector3(0.0016f, 0.0016f, 0.0016f), "Mat_IronMan_DarkMetal"),
                new PrimDef("TieBar_Pectoral_Sup_Clevis_R", PrimitiveType.Cube, new Vector3(0.0070f, 0.0130f, 0.0068f), Vector3.zero, new Vector3(0.0016f, 0.0016f, 0.0016f), "Mat_IronMan_DarkMetal"),
                new PrimDef("TieBar_Pectoral_Inf_L", PrimitiveType.Cylinder, new Vector3(-0.0092f, 0.0038f, 0.0100f), new Vector3(340f, 315f, 80f), new Vector3(0.0010f, 0.0032f, 0.0010f), "Mat_IronMan_Silver"),
                new PrimDef("TieBar_Pectoral_Inf_R", PrimitiveType.Cylinder, new Vector3(0.0092f, 0.0038f, 0.0100f), new Vector3(340f, 45f, 280f), new Vector3(0.0010f, 0.0032f, 0.0010f), "Mat_IronMan_Silver"),
                new PrimDef("TieBar_Pectoral_Inf_Clevis_L", PrimitiveType.Cube, new Vector3(-0.0065f, 0.0040f, 0.0075f), Vector3.zero, new Vector3(0.0015f, 0.0015f, 0.0015f), "Mat_IronMan_DarkMetal"),
                new PrimDef("TieBar_Pectoral_Inf_Clevis_R", PrimitiveType.Cube, new Vector3(0.0065f, 0.0040f, 0.0075f), Vector3.zero, new Vector3(0.0015f, 0.0015f, 0.0015f), "Mat_IronMan_DarkMetal"),

                // Intercostal Harnesses & Gold Brackets
                new PrimDef("Harness_Intercostal_Upper_L", PrimitiveType.Cylinder, new Vector3(-0.0182f, 0.0005f, 0.0045f), new Vector3(18f, 335f, 340f), new Vector3(0.0012f, 0.0070f, 0.0012f), "Mat_CopperCoil"),
                new PrimDef("Harness_Intercostal_Upper_R", PrimitiveType.Cylinder, new Vector3(0.0182f, 0.0005f, 0.0045f), new Vector3(18f, 25f, 20f), new Vector3(0.0012f, 0.0070f, 0.0012f), "Mat_CopperCoil"),
                new PrimDef("Harness_Intercostal_Lower_L", PrimitiveType.Cylinder, new Vector3(-0.0182f, -0.0050f, 0.0035f), new Vector3(15f, 335f, 345f), new Vector3(0.0012f, 0.0065f, 0.0012f), "Mat_CopperCoil"),
                new PrimDef("Harness_Intercostal_Lower_R", PrimitiveType.Cylinder, new Vector3(0.0182f, -0.0050f, 0.0035f), new Vector3(15f, 25f, 15f), new Vector3(0.0012f, 0.0065f, 0.0012f), "Mat_CopperCoil"),
                new PrimDef("Conduit_Intercostal_Braid_L", PrimitiveType.Cylinder, new Vector3(-0.0170f, -0.0020f, 0.0020f), new Vector3(12f, 340f, 10f), new Vector3(0.0010f, 0.0110f, 0.0010f), "Mat_IronMan_Silver"),
                new PrimDef("Conduit_Intercostal_Braid_R", PrimitiveType.Cylinder, new Vector3(0.0170f, -0.0020f, 0.0020f), new Vector3(12f, 20f, 350f), new Vector3(0.0010f, 0.0110f, 0.0010f), "Mat_IronMan_Silver"),
                new PrimDef("Conduit_Bracket_Sup_L", PrimitiveType.Cube, new Vector3(-0.0180f, 0.0020f, 0.0030f), new Vector3(14f, 342f, 348f), new Vector3(0.0022f, 0.0010f, 0.0025f), "Mat_IronMan_Gold"),
                new PrimDef("Conduit_Bracket_Sup_R", PrimitiveType.Cube, new Vector3(0.0180f, 0.0020f, 0.0030f), new Vector3(14f, 18f, 12f), new Vector3(0.0022f, 0.0010f, 0.0025f), "Mat_IronMan_Gold"),
                new PrimDef("Conduit_Bracket_Mid_L", PrimitiveType.Cube, new Vector3(-0.0180f, -0.0035f, 0.0020f), new Vector3(12f, 342f, 350f), new Vector3(0.0022f, 0.0010f, 0.0025f), "Mat_IronMan_Gold"),
                new PrimDef("Conduit_Bracket_Mid_R", PrimitiveType.Cube, new Vector3(0.0180f, -0.0035f, 0.0020f), new Vector3(12f, 18f, 10f), new Vector3(0.0022f, 0.0010f, 0.0025f), "Mat_IronMan_Gold"),
                new PrimDef("Conduit_Bracket_Inf_L", PrimitiveType.Cube, new Vector3(-0.0178f, -0.0075f, 0.0010f), new Vector3(10f, 342f, 352f), new Vector3(0.0022f, 0.0010f, 0.0025f), "Mat_IronMan_Gold"),
                new PrimDef("Conduit_Bracket_Inf_R", PrimitiveType.Cube, new Vector3(0.0178f, -0.0075f, 0.0010f), new Vector3(10f, 18f, 8f), new Vector3(0.0022f, 0.0010f, 0.0025f), "Mat_IronMan_Gold"),

                // Rib Slat Pivot Pins & Bosses
                new PrimDef("Rib_PivotPin_1_L", PrimitiveType.Cylinder, new Vector3(-0.0188f, 0.0032f, 0.0005f), new Vector3(0f, 75f, 0f), new Vector3(0.0008f, 0.0018f, 0.0008f), "Mat_IronMan_Silver"),
                new PrimDef("Rib_PivotPin_1_R", PrimitiveType.Cylinder, new Vector3(0.0188f, 0.0032f, 0.0005f), new Vector3(0f, 285f, 0f), new Vector3(0.0008f, 0.0018f, 0.0008f), "Mat_IronMan_Silver"),
                new PrimDef("Rib_PivotPin_2_L", PrimitiveType.Cylinder, new Vector3(-0.0188f, -0.0023f, -0.0005f), new Vector3(0f, 75f, 0f), new Vector3(0.0008f, 0.0018f, 0.0008f), "Mat_IronMan_Silver"),
                new PrimDef("Rib_PivotPin_2_R", PrimitiveType.Cylinder, new Vector3(0.0188f, -0.0023f, -0.0005f), new Vector3(0f, 285f, 0f), new Vector3(0.0008f, 0.0018f, 0.0008f), "Mat_IronMan_Silver"),
                new PrimDef("Rib_PivotPin_3_L", PrimitiveType.Cylinder, new Vector3(-0.0188f, -0.0078f, -0.0015f), new Vector3(0f, 75f, 0f), new Vector3(0.0008f, 0.0018f, 0.0008f), "Mat_IronMan_Silver"),
                new PrimDef("Rib_PivotPin_3_R", PrimitiveType.Cylinder, new Vector3(0.0188f, -0.0078f, -0.0015f), new Vector3(0f, 285f, 0f), new Vector3(0.0008f, 0.0018f, 0.0008f), "Mat_IronMan_Silver"),

                // Abdominal Lateral Hydraulic Actuators
                new PrimDef("Ab_Actuator_Cylinder_L", PrimitiveType.Cylinder, new Vector3(-0.0108f, -0.0060f, 0.0072f), new Vector3(10f, 350f, 8f), new Vector3(0.0016f, 0.0032f, 0.0016f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Ab_Actuator_Cylinder_R", PrimitiveType.Cylinder, new Vector3(0.0108f, -0.0060f, 0.0072f), new Vector3(10f, 10f, 352f), new Vector3(0.0016f, 0.0032f, 0.0016f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Ab_Actuator_PistonRam_L", PrimitiveType.Cylinder, new Vector3(-0.0112f, -0.0098f, 0.0065f), new Vector3(10f, 350f, 8f), new Vector3(0.0010f, 0.0030f, 0.0010f), "Mat_IronMan_Silver"),
                new PrimDef("Ab_Actuator_PistonRam_R", PrimitiveType.Cylinder, new Vector3(0.0112f, -0.0098f, 0.0065f), new Vector3(10f, 10f, 352f), new Vector3(0.0010f, 0.0030f, 0.0010f), "Mat_IronMan_Silver"),
                new PrimDef("Ab_Actuator_Collar_L", PrimitiveType.Cylinder, new Vector3(-0.0106f, -0.0038f, 0.0075f), new Vector3(10f, 350f, 8f), new Vector3(0.0019f, 0.0006f, 0.0019f), "Mat_IronMan_Gold"),
                new PrimDef("Ab_Actuator_Collar_R", PrimitiveType.Cylinder, new Vector3(0.0106f, -0.0038f, 0.0075f), new Vector3(10f, 10f, 352f), new Vector3(0.0019f, 0.0006f, 0.0019f), "Mat_IronMan_Gold"),
                new PrimDef("Ab_Actuator_RodEnd_L", PrimitiveType.Sphere, new Vector3(-0.0116f, -0.0130f, 0.0060f), Vector3.zero, new Vector3(0.0014f, 0.0014f, 0.0014f), "Mat_IronMan_Silver"),
                new PrimDef("Ab_Actuator_RodEnd_R", PrimitiveType.Sphere, new Vector3(0.0116f, -0.0130f, 0.0060f), Vector3.zero, new Vector3(0.0014f, 0.0014f, 0.0014f), "Mat_IronMan_Silver"),

                // Vertebral Spine Cryo-Distribution Block
                new PrimDef("Spine_Cryo_DistributionBlock", PrimitiveType.Cube, new Vector3(0f, 0.0050f, -0.0125f), Vector3.zero, new Vector3(0.0072f, 0.0030f, 0.0032f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Spine_Cryo_SensorCap", PrimitiveType.Cylinder, new Vector3(0f, 0.0070f, -0.0132f), Vector3.zero, new Vector3(0.0014f, 0.0012f, 0.0014f), "Mat_IronMan_Silver"),
                new PrimDef("Spine_Cryo_SensorSeal", PrimitiveType.Cylinder, new Vector3(0f, 0.0062f, -0.0132f), Vector3.zero, new Vector3(0.0017f, 0.0004f, 0.0017f), "Mat_CopperCoil"),
                new PrimDef("Spine_Manifold_Union_L", PrimitiveType.Cylinder, new Vector3(-0.0038f, 0.0050f, -0.0130f), new Vector3(0f, 0f, 90f), new Vector3(0.0018f, 0.0016f, 0.0018f), "Mat_IronMan_Silver"),
                new PrimDef("Spine_Manifold_Union_R", PrimitiveType.Cylinder, new Vector3(0.0038f, 0.0050f, -0.0130f), new Vector3(0f, 0f, 90f), new Vector3(0.0018f, 0.0016f, 0.0018f), "Mat_IronMan_Silver"),
                new PrimDef("Spine_Coolant_FeedBridge_L", PrimitiveType.Cylinder, new Vector3(-0.0025f, 0.0050f, -0.0080f), new Vector3(90f, 0f, 0f), new Vector3(0.0010f, 0.0060f, 0.0010f), "Mat_CopperCoil"),
                new PrimDef("Spine_Coolant_FeedBridge_R", PrimitiveType.Cylinder, new Vector3(0.0025f, 0.0050f, -0.0080f), new Vector3(90f, 0f, 0f), new Vector3(0.0010f, 0.0060f, 0.0010f), "Mat_CopperCoil"),

                // Dorsal Air-Brake Scissor Links
                new PrimDef("AirBrake_ScissorLink_Upper_L", PrimitiveType.Cube, new Vector3(-0.0118f, 0.0125f, -0.0135f), new Vector3(340f, 25f, 20f), new Vector3(0.0008f, 0.0042f, 0.0010f), "Mat_IronMan_DarkMetal"),
                new PrimDef("AirBrake_ScissorLink_Upper_R", PrimitiveType.Cube, new Vector3(0.0118f, 0.0125f, -0.0135f), new Vector3(340f, 335f, 340f), new Vector3(0.0008f, 0.0042f, 0.0010f), "Mat_IronMan_DarkMetal"),
                new PrimDef("AirBrake_ScissorLink_Lower_L", PrimitiveType.Cube, new Vector3(-0.0122f, 0.0085f, -0.0132f), new Vector3(20f, 20f, 340f), new Vector3(0.0008f, 0.0038f, 0.0010f), "Mat_IronMan_DarkMetal"),
                new PrimDef("AirBrake_ScissorLink_Lower_R", PrimitiveType.Cube, new Vector3(0.0122f, 0.0085f, -0.0132f), new Vector3(20f, 340f, 20f), new Vector3(0.0008f, 0.0038f, 0.0010f), "Mat_IronMan_DarkMetal"),
                new PrimDef("AirBrake_PivotPin_Chassis_L", PrimitiveType.Cylinder, new Vector3(-0.0090f, 0.0138f, -0.0125f), new Vector3(0f, 15f, 90f), new Vector3(0.0007f, 0.0016f, 0.0007f), "Mat_IronMan_Silver"),
                new PrimDef("AirBrake_PivotPin_Chassis_R", PrimitiveType.Cylinder, new Vector3(0.0090f, 0.0138f, -0.0125f), new Vector3(0f, 345f, 90f), new Vector3(0.0007f, 0.0016f, 0.0007f), "Mat_IronMan_Silver"),
                new PrimDef("AirBrake_PivotPin_Apex_L", PrimitiveType.Cylinder, new Vector3(-0.0125f, 0.0105f, -0.0140f), new Vector3(0f, 15f, 90f), new Vector3(0.0007f, 0.0016f, 0.0007f), "Mat_IronMan_Silver"),
                new PrimDef("AirBrake_PivotPin_Apex_R", PrimitiveType.Cylinder, new Vector3(0.0125f, 0.0105f, -0.0140f), new Vector3(0f, 345f, 90f), new Vector3(0.0007f, 0.0016f, 0.0007f), "Mat_IronMan_Silver"),

                // Thruster Pre-Burn Manifolds & Gimbals
                new PrimDef("Thruster_PreBurn_Manifold_L", PrimitiveType.Cylinder, new Vector3(-0.0095f, 0.0022f, -0.0138f), new Vector3(335f, 8f, 0f), new Vector3(0.0046f, 0.0008f, 0.0046f), "Mat_CopperCoil"),
                new PrimDef("Thruster_PreBurn_Manifold_R", PrimitiveType.Cylinder, new Vector3(0.0095f, 0.0022f, -0.0138f), new Vector3(335f, 352f, 0f), new Vector3(0.0046f, 0.0008f, 0.0046f), "Mat_CopperCoil"),
                new PrimDef("Thruster_Injector_Line_Upper_L", PrimitiveType.Cylinder, new Vector3(-0.0078f, 0.0028f, -0.0134f), new Vector3(310f, 30f, 45f), new Vector3(0.0007f, 0.0028f, 0.0007f), "Mat_CopperCoil"),
                new PrimDef("Thruster_Injector_Line_Upper_R", PrimitiveType.Cylinder, new Vector3(0.0078f, 0.0028f, -0.0134f), new Vector3(310f, 330f, 315f), new Vector3(0.0007f, 0.0028f, 0.0007f), "Mat_CopperCoil"),
                new PrimDef("Thruster_Gimbal_Bracket_L", PrimitiveType.Cube, new Vector3(-0.0095f, 0.0012f, -0.0136f), new Vector3(335f, 8f, 0f), new Vector3(0.0058f, 0.0016f, 0.0024f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Thruster_Gimbal_Bracket_R", PrimitiveType.Cube, new Vector3(0.0095f, 0.0012f, -0.0136f), new Vector3(335f, 352f, 0f), new Vector3(0.0058f, 0.0016f, 0.0024f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Thruster_GimbalPin_Pitch_L", PrimitiveType.Cylinder, new Vector3(-0.0095f, 0.0012f, -0.0136f), new Vector3(0f, 8f, 90f), new Vector3(0.0008f, 0.0062f, 0.0008f), "Mat_IronMan_Silver"),
                new PrimDef("Thruster_GimbalPin_Pitch_R", PrimitiveType.Cylinder, new Vector3(0.0095f, 0.0012f, -0.0136f), new Vector3(0f, 352f, 90f), new Vector3(0.0008f, 0.0062f, 0.0008f), "Mat_IronMan_Silver")
            };
        }

        // =========================================================================
        // SHOULDERS & ARMS MICRO-MECHANICAL DEFINITIONS
        // =========================================================================
        private static List<PrimDef> GetArmMicroDefs()
        {
            return new List<PrimDef>
            {
                // Pauldron Scissor Links & Slide Rails
                new PrimDef("Pauldron_Scissor_UpperBracket", PrimitiveType.Cube, new Vector3(-0.0045f, 0.0085f, 0f), new Vector3(0f, 0f, 15f), new Vector3(0.0016f, 0.0042f, 0.0028f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Pauldron_Scissor_LowerBracket", PrimitiveType.Cube, new Vector3(-0.0065f, 0.0055f, 0f), new Vector3(0f, 0f, 340f), new Vector3(0.0014f, 0.0048f, 0.0024f), "Mat_IronMan_Silver"),
                new PrimDef("Pauldron_Scissor_PivotPin", PrimitiveType.Cylinder, new Vector3(-0.0055f, 0.0070f, 0f), new Vector3(90f, 0f, 0f), new Vector3(0.0012f, 0.0032f, 0.0012f), "Mat_IronMan_Gold"),
                new PrimDef("Pauldron_Slide_Rail_Anterior", PrimitiveType.Cylinder, new Vector3(-0.0010f, 0.0082f, 0.0035f), new Vector3(15f, 0f, 345f), new Vector3(0.0010f, 0.0085f, 0.0010f), "Mat_IronMan_Silver"),
                new PrimDef("Pauldron_Slide_Rail_Posterior", PrimitiveType.Cylinder, new Vector3(-0.0010f, 0.0082f, -0.0035f), new Vector3(345f, 0f, 345f), new Vector3(0.0010f, 0.0085f, 0.0010f), "Mat_IronMan_Silver"),
                new PrimDef("Pauldron_Slide_Shoe_Anterior", PrimitiveType.Cube, new Vector3(-0.0010f, 0.0082f, 0.0035f), new Vector3(15f, 0f, 345f), new Vector3(0.0018f, 0.0026f, 0.0018f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Pauldron_Slide_Shoe_Posterior", PrimitiveType.Cube, new Vector3(-0.0010f, 0.0082f, -0.0035f), new Vector3(345f, 0f, 345f), new Vector3(0.0018f, 0.0026f, 0.0018f), "Mat_IronMan_DarkMetal"),

                // Pauldron Attitude RCS Thruster
                new PrimDef("Pauldron_RCS_ExhaustLip", PrimitiveType.Cylinder, new Vector3(0.0102f, 0.0078f, 0f), new Vector3(0f, 0f, 285f), new Vector3(0.0033f, 0.0006f, 0.0033f), "Mat_IronMan_Silver"),
                new PrimDef("Pauldron_RCS_CopperRing", PrimitiveType.Cylinder, new Vector3(0.0098f, 0.0078f, 0f), new Vector3(0f, 0f, 285f), new Vector3(0.0030f, 0.0005f, 0.0030f), "Mat_CopperCoil"),
                new PrimDef("Pauldron_RCS_PlasmaCore", PrimitiveType.Cylinder, new Vector3(0.0095f, 0.0078f, 0f), new Vector3(0f, 0f, 285f), new Vector3(0.0014f, 0.0004f, 0.0014f), "Mat_EmissiveBlue"),

                // Deltoid Ball Joint Sensor Rings
                new PrimDef("Deltoid_Sensor_Ring_Equator", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0f, 0f, 90f), new Vector3(0.0094f, 0.0012f, 0.0094f), "Mat_CopperCoil"),
                new PrimDef("Deltoid_Sensor_Ring_Meridian", PrimitiveType.Cylinder, Vector3.zero, new Vector3(90f, 0f, 0f), new Vector3(0.0093f, 0.0010f, 0.0093f), "Mat_IronMan_Silver"),
                new PrimDef("Deltoid_Sensor_PickupHead", PrimitiveType.Cube, new Vector3(0f, 0.0038f, 0.0038f), new Vector3(45f, 0f, 0f), new Vector3(0.0022f, 0.0018f, 0.0018f), "Mat_IronMan_DarkMetal"),

                // Humeral Telemetry Diagnostic Port
                new PrimDef("Humeral_Telemetry_Housing", PrimitiveType.Cube, new Vector3(0.0042f, -0.0080f, 0f), new Vector3(0f, 0f, 10f), new Vector3(0.0016f, 0.0045f, 0.0038f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Humeral_Telemetry_Socket", PrimitiveType.Cube, new Vector3(0.0048f, -0.0080f, 0f), new Vector3(0f, 0f, 10f), new Vector3(0.0008f, 0.0032f, 0.0026f), "Mat_IronMan_Gold"),
                new PrimDef("Humeral_Telemetry_CoverFlap", PrimitiveType.Cube, new Vector3(0.0051f, -0.0060f, 0f), new Vector3(0f, 0f, 25f), new Vector3(0.0006f, 0.0020f, 0.0030f), "Mat_IronMan_Silver"),
                new PrimDef("Humeral_Telemetry_DataNode", PrimitiveType.Cylinder, new Vector3(0.0049f, -0.0092f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.0008f, 0.0005f, 0.0008f), "Mat_EmissiveBlue"),

                // Bicep-to-Elbow Breakout Junction Boxes
                new PrimDef("Bicep_Junction_Box_Lat", PrimitiveType.Cube, new Vector3(0.0032f, -0.0135f, 0.0026f), Vector3.zero, new Vector3(0.0022f, 0.0028f, 0.0022f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Bicep_Junction_Box_Med", PrimitiveType.Cube, new Vector3(-0.0032f, -0.0135f, 0.0026f), Vector3.zero, new Vector3(0.0022f, 0.0028f, 0.0022f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Bicep_Diag_Plate_Lat", PrimitiveType.Cube, new Vector3(0.0042f, -0.0135f, 0.0026f), Vector3.zero, new Vector3(0.0005f, 0.0022f, 0.0016f), "Mat_IronMan_Gold"),
                new PrimDef("Bicep_Diag_Plate_Med", PrimitiveType.Cube, new Vector3(-0.0042f, -0.0135f, 0.0026f), Vector3.zero, new Vector3(0.0005f, 0.0022f, 0.0016f), "Mat_IronMan_Gold"),
                new PrimDef("Bicep_Conduit_Ferrule_Lat", PrimitiveType.Cylinder, new Vector3(0.0028f, -0.0150f, 0.0032f), new Vector3(15f, 0f, 0f), new Vector3(0.0015f, 0.0012f, 0.0015f), "Mat_IronMan_Silver"),
                new PrimDef("Bicep_Conduit_Ferrule_Med", PrimitiveType.Cylinder, new Vector3(-0.0028f, -0.0150f, 0.0032f), new Vector3(15f, 0f, 0f), new Vector3(0.0015f, 0.0012f, 0.0015f), "Mat_IronMan_Silver"),

                // Elbow Circlips & Grease Zerk Nipples
                new PrimDef("Elbow_Circlip_Ring_Lat", PrimitiveType.Cylinder, new Vector3(0.0050f, -0.0172f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.0054f, 0.0003f, 0.0054f), "Mat_IronMan_Silver"),
                new PrimDef("Elbow_Circlip_Ring_Med", PrimitiveType.Cylinder, new Vector3(-0.0046f, -0.0172f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.0050f, 0.0003f, 0.0050f), "Mat_IronMan_Silver"),
                new PrimDef("Elbow_Lube_Port_Lat", PrimitiveType.Cylinder, new Vector3(0.0052f, -0.0152f, 0.0012f), new Vector3(0f, 0f, 90f), new Vector3(0.0010f, 0.0008f, 0.0010f), "Mat_IronMan_Gold"),
                new PrimDef("Elbow_Lube_Port_Med", PrimitiveType.Cylinder, new Vector3(-0.0048f, -0.0152f, 0.0012f), new Vector3(0f, 0f, 90f), new Vector3(0.0010f, 0.0008f, 0.0010f), "Mat_IronMan_Gold"),

                // Forearm Micro-Missile Latches & Targeting LIDAR Prism
                new PrimDef("Missile_Latch_Clip_Ant", PrimitiveType.Cube, new Vector3(0f, -0.0218f, 0.0075f), new Vector3(15f, 0f, 0f), new Vector3(0.0045f, 0.0012f, 0.0014f), "Mat_IronMan_Silver"),
                new PrimDef("Missile_Latch_Clip_Post", PrimitiveType.Cube, new Vector3(0f, -0.0308f, 0.0072f), new Vector3(345f, 0f, 0f), new Vector3(0.0045f, 0.0012f, 0.0014f), "Mat_IronMan_Silver"),
                new PrimDef("Missile_Latch_Solenoid", PrimitiveType.Cylinder, new Vector3(0f, -0.0210f, 0.0062f), new Vector3(90f, 0f, 0f), new Vector3(0.0014f, 0.0018f, 0.0014f), "Mat_IronMan_Gold"),
                new PrimDef("Targeting_Optic_Housing", PrimitiveType.Cube, new Vector3(0f, -0.0202f, 0.0055f), new Vector3(20f, 0f, 0f), new Vector3(0.0042f, 0.0022f, 0.0026f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Targeting_Optic_Prism", PrimitiveType.Cylinder, new Vector3(0f, -0.0198f, 0.0064f), new Vector3(70f, 0f, 0f), new Vector3(0.0022f, 0.0012f, 0.0022f), "Mat_EmissiveBlue"),

                // Gauntlet Thermal Dissipation Micro-Louvers & Standoffs
                new PrimDef("Gauntlet_Standoff_Lat_Upper", PrimitiveType.Cube, new Vector3(0.0060f, -0.0235f, 0.0020f), Vector3.zero, new Vector3(0.0014f, 0.0022f, 0.0018f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Gauntlet_Standoff_Lat_Lower", PrimitiveType.Cube, new Vector3(0.0060f, -0.0315f, 0.0020f), Vector3.zero, new Vector3(0.0014f, 0.0022f, 0.0018f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Gauntlet_HeatVent_Louver_Lat", PrimitiveType.Cube, new Vector3(0.0064f, -0.0275f, -0.0030f), new Vector3(0f, 15f, 0f), new Vector3(0.0012f, 0.0085f, 0.0024f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Gauntlet_HeatVent_Louver_Med", PrimitiveType.Cube, new Vector3(-0.0064f, -0.0275f, -0.0030f), new Vector3(0f, 345f, 0f), new Vector3(0.0012f, 0.0085f, 0.0024f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Gauntlet_HeatVent_Core_Lat", PrimitiveType.Cube, new Vector3(0.0060f, -0.0275f, -0.0030f), new Vector3(0f, 15f, 0f), new Vector3(0.0006f, 0.0075f, 0.0018f), "Mat_CopperCoil"),
                new PrimDef("Gauntlet_HeatVent_Core_Med", PrimitiveType.Cube, new Vector3(-0.0060f, -0.0275f, -0.0030f), new Vector3(0f, 345f, 0f), new Vector3(0.0006f, 0.0075f, 0.0018f), "Mat_CopperCoil"),

                // Wrist Cuff Pneumatic Manifold & Hex Screws
                new PrimDef("Wrist_Pressure_Valve_Fitting", PrimitiveType.Cube, new Vector3(0.0050f, -0.0355f, 0.0015f), Vector3.zero, new Vector3(0.0022f, 0.0020f, 0.0022f), "Mat_IronMan_Silver"),
                new PrimDef("Wrist_Pressure_Relief_Nozzle", PrimitiveType.Cylinder, new Vector3(0.0058f, -0.0355f, 0.0015f), new Vector3(0f, 0f, 90f), new Vector3(0.0010f, 0.0008f, 0.0010f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Wrist_Screw_Ant_Lat", PrimitiveType.Cylinder, new Vector3(0.0036f, -0.0348f, 0.0036f), Vector3.zero, new Vector3(0.0010f, 0.0005f, 0.0010f), "Mat_IronMan_Silver"),
                new PrimDef("Wrist_Screw_Ant_Med", PrimitiveType.Cylinder, new Vector3(-0.0036f, -0.0348f, 0.0036f), Vector3.zero, new Vector3(0.0010f, 0.0005f, 0.0010f), "Mat_IronMan_Silver"),
                new PrimDef("Wrist_Screw_Post_Lat", PrimitiveType.Cylinder, new Vector3(0.0036f, -0.0348f, -0.0036f), Vector3.zero, new Vector3(0.0010f, 0.0005f, 0.0010f), "Mat_IronMan_Silver"),
                new PrimDef("Wrist_Screw_Post_Med", PrimitiveType.Cylinder, new Vector3(-0.0036f, -0.0348f, -0.0036f), Vector3.zero, new Vector3(0.0010f, 0.0005f, 0.0010f), "Mat_IronMan_Silver"),

                // Metacarpal Rivets & Thumb Saddle Joint
                new PrimDef("Hand_Rivet_Prox_Lat", PrimitiveType.Sphere, new Vector3(0.0028f, -0.0380f, 0.0056f), Vector3.zero, new Vector3(0.0012f, 0.0012f, 0.0012f), "Mat_IronMan_Silver"),
                new PrimDef("Hand_Rivet_Prox_Med", PrimitiveType.Sphere, new Vector3(-0.0028f, -0.0380f, 0.0056f), Vector3.zero, new Vector3(0.0012f, 0.0012f, 0.0012f), "Mat_IronMan_Silver"),
                new PrimDef("Hand_Rivet_Dist_Center", PrimitiveType.Sphere, new Vector3(0f, -0.0410f, 0.0058f), Vector3.zero, new Vector3(0.0014f, 0.0014f, 0.0014f), "Mat_IronMan_Gold"),
                new PrimDef("Thumb_Gimbal_Yoke", PrimitiveType.Cube, new Vector3(-0.0036f, -0.0392f, 0.0018f), new Vector3(0f, 0f, 335f), new Vector3(0.0018f, 0.0028f, 0.0024f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Thumb_Gimbal_HingePin", PrimitiveType.Cylinder, new Vector3(-0.0042f, -0.0405f, 0.0018f), new Vector3(0f, 90f, 0f), new Vector3(0.0010f, 0.0030f, 0.0010f), "Mat_IronMan_Silver"),
                new PrimDef("Thumb_Rotation_Stop_Lug", PrimitiveType.Cube, new Vector3(-0.0052f, -0.0396f, 0.0025f), new Vector3(0f, 0f, 335f), new Vector3(0.0010f, 0.0012f, 0.0012f), "Mat_IronMan_Gold"),

                // Palm Repulsor Iris Aperture Blades & Enclosure
                new PrimDef("Repulsor_Iris_OuterBezel", PrimitiveType.Cylinder, new Vector3(0f, -0.0410f, -0.0026f), new Vector3(90f, 0f, 0f), new Vector3(0.0074f, 0.0008f, 0.0074f), "Mat_IronMan_Silver"),
                new PrimDef("Repulsor_Iris_Blade_1", PrimitiveType.Cube, new Vector3(0.0016f, -0.0394f, -0.0031f), new Vector3(0f, 0f, 45f), new Vector3(0.0026f, 0.0008f, 0.0006f), "Mat_IronMan_Silver"),
                new PrimDef("Repulsor_Iris_Blade_2", PrimitiveType.Cube, new Vector3(0.0016f, -0.0426f, -0.0031f), new Vector3(0f, 0f, 135f), new Vector3(0.0026f, 0.0008f, 0.0006f), "Mat_IronMan_Silver"),
                new PrimDef("Repulsor_Iris_Blade_3", PrimitiveType.Cube, new Vector3(-0.0016f, -0.0426f, -0.0031f), new Vector3(0f, 0f, 225f), new Vector3(0.0026f, 0.0008f, 0.0006f), "Mat_IronMan_Silver"),
                new PrimDef("Repulsor_Iris_Blade_4", PrimitiveType.Cube, new Vector3(-0.0016f, -0.0394f, -0.0031f), new Vector3(0f, 0f, 315f), new Vector3(0.0026f, 0.0008f, 0.0006f), "Mat_IronMan_Silver"),
                new PrimDef("Repulsor_Iris_CopperRing", PrimitiveType.Cylinder, new Vector3(0f, -0.0410f, -0.0033f), new Vector3(90f, 0f, 0f), new Vector3(0.0048f, 0.0008f, 0.0048f), "Mat_CopperCoil"),
                new PrimDef("Repulsor_Iris_FocusCollar", PrimitiveType.Cylinder, new Vector3(0f, -0.0410f, -0.0035f), new Vector3(90f, 0f, 0f), new Vector3(0.0038f, 0.0010f, 0.0038f), "Mat_IronMan_DarkMetal")
            };
        }

        // =========================================================================
        // PELVIS & INGUINAL FLUIDICS DEFINITIONS
        // =========================================================================
        private static List<PrimDef> GetPelvisMicroDefs()
        {
            return new List<PrimDef>
            {
                // Primary Belt & Chamfers
                new PrimDef("Pelvis_Belt_Frame", PrimitiveType.Cube, new Vector3(0f, 0.0018f, 0.0005f), Vector3.zero, new Vector3(0.0260f, 0.0032f, 0.0200f), "Mat_IronMan_Gold"),
                new PrimDef("Pelvis_Belt_UpperChamfer_L", PrimitiveType.Cube, new Vector3(-0.0080f, 0.0035f, 0.0005f), new Vector3(0f, 0f, 5f), new Vector3(0.0105f, 0.0008f, 0.0195f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Pelvis_Belt_UpperChamfer_R", PrimitiveType.Cube, new Vector3(0.0080f, 0.0035f, 0.0005f), new Vector3(0f, 0f, 355f), new Vector3(0.0105f, 0.0008f, 0.0195f), "Mat_IronMan_DarkMetal"),

                // 6-Cell Li-S Energy Storage Banks
                new PrimDef("Belt_Busbar_Strip_L", PrimitiveType.Cube, new Vector3(-0.0082f, 0.0018f, 0.0092f), new Vector3(0f, 352f, 0f), new Vector3(0.0072f, 0.0006f, 0.0005f), "Mat_CopperCoil"),
                new PrimDef("Belt_Busbar_Strip_R", PrimitiveType.Cube, new Vector3(0.0082f, 0.0018f, 0.0092f), new Vector3(0f, 8f, 0f), new Vector3(0.0072f, 0.0006f, 0.0005f), "Mat_CopperCoil"),
                new PrimDef("Belt_Capacitor_Cell_L1", PrimitiveType.Cylinder, new Vector3(-0.0055f, 0.0018f, 0.0104f), new Vector3(0f, 0f, 90f), new Vector3(0.0014f, 0.0008f, 0.0014f), "Mat_IronMan_Silver"),
                new PrimDef("Belt_Capacitor_Cell_L2", PrimitiveType.Cylinder, new Vector3(-0.0082f, 0.0018f, 0.0100f), new Vector3(0f, 0f, 90f), new Vector3(0.0014f, 0.0008f, 0.0014f), "Mat_IronMan_Silver"),
                new PrimDef("Belt_Capacitor_Cell_L3", PrimitiveType.Cylinder, new Vector3(-0.0108f, 0.0018f, 0.0094f), new Vector3(0f, 0f, 90f), new Vector3(0.0014f, 0.0008f, 0.0014f), "Mat_IronMan_Silver"),
                new PrimDef("Belt_Capacitor_Cell_R1", PrimitiveType.Cylinder, new Vector3(0.0055f, 0.0018f, 0.0104f), new Vector3(0f, 0f, 90f), new Vector3(0.0014f, 0.0008f, 0.0014f), "Mat_IronMan_Silver"),
                new PrimDef("Belt_Capacitor_Cell_R2", PrimitiveType.Cylinder, new Vector3(0.0082f, 0.0018f, 0.0100f), new Vector3(0f, 0f, 90f), new Vector3(0.0014f, 0.0008f, 0.0014f), "Mat_IronMan_Silver"),
                new PrimDef("Belt_Capacitor_Cell_R3", PrimitiveType.Cylinder, new Vector3(0.0108f, 0.0018f, 0.0094f), new Vector3(0f, 0f, 90f), new Vector3(0.0014f, 0.0008f, 0.0014f), "Mat_IronMan_Silver"),
                new PrimDef("Belt_Capacitor_Clip_L1", PrimitiveType.Cube, new Vector3(-0.0068f, 0.0018f, 0.0102f), new Vector3(0f, 352f, 0f), new Vector3(0.0004f, 0.0018f, 0.0008f), "Mat_IronMan_Gold"),
                new PrimDef("Belt_Capacitor_Clip_R1", PrimitiveType.Cube, new Vector3(0.0068f, 0.0018f, 0.0102f), new Vector3(0f, 8f, 0f), new Vector3(0.0004f, 0.0018f, 0.0008f), "Mat_IronMan_Gold"),

                // Buckle Housing, Arc Status Lens & Diagnostic Port
                new PrimDef("Pelvis_Belt_Buckle", PrimitiveType.Cube, new Vector3(0f, 0.0018f, 0.0107f), Vector3.zero, new Vector3(0.0066f, 0.0028f, 0.0016f), "Mat_IronMan_Silver"),
                new PrimDef("Belt_Buckle_StatusLens", PrimitiveType.Cylinder, new Vector3(0f, 0.0022f, 0.0116f), new Vector3(90f, 0f, 0f), new Vector3(0.0016f, 0.0004f, 0.0016f), "Mat_EmissiveBlue"),
                new PrimDef("Belt_Buckle_LensRetainer", PrimitiveType.Cylinder, new Vector3(0f, 0.0022f, 0.0115f), new Vector3(90f, 0f, 0f), new Vector3(0.0022f, 0.0003f, 0.0022f), "Mat_IronMan_Gold"),
                new PrimDef("Belt_Buckle_RoutingPort_Outer", PrimitiveType.Cylinder, new Vector3(0f, 0.0012f, 0.0115f), new Vector3(90f, 0f, 0f), new Vector3(0.0018f, 0.0004f, 0.0018f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Belt_Buckle_RoutingPort_PinArray", PrimitiveType.Cylinder, new Vector3(0f, 0.0012f, 0.0117f), new Vector3(90f, 0f, 0f), new Vector3(0.0008f, 0.0003f, 0.0008f), "Mat_CopperCoil"),
                new PrimDef("Belt_Buckle_Bolt_L", PrimitiveType.Cylinder, new Vector3(-0.0025f, 0.0018f, 0.0114f), new Vector3(90f, 0f, 0f), new Vector3(0.0007f, 0.0003f, 0.0007f), "Mat_IronMan_Silver"),
                new PrimDef("Belt_Buckle_Bolt_R", PrimitiveType.Cylinder, new Vector3(0.0025f, 0.0018f, 0.0114f), new Vector3(90f, 0f, 0f), new Vector3(0.0007f, 0.0003f, 0.0007f), "Mat_IronMan_Silver"),

                // Inguinal Hip Sockets & Gimbals
                new PrimDef("Pelvis_Hip_Socket_L", PrimitiveType.Cylinder, new Vector3(-0.0110f, 0f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.0090f, 0.0030f, 0.0090f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Pelvis_Hip_Socket_R", PrimitiveType.Cylinder, new Vector3(0.0110f, 0f, 0f), new Vector3(0f, 0f, 270f), new Vector3(0.0090f, 0.0030f, 0.0090f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Pelvis_Hip_Gimbal_L", PrimitiveType.Sphere, new Vector3(-0.0110f, 0f, 0f), Vector3.zero, new Vector3(0.0080f, 0.0080f, 0.0080f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Pelvis_Hip_Gimbal_R", PrimitiveType.Sphere, new Vector3(0.0110f, 0f, 0f), Vector3.zero, new Vector3(0.0080f, 0.0080f, 0.0080f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Pelvis_Hip_RetainingCollar_L", PrimitiveType.Cylinder, new Vector3(-0.0092f, 0f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.0084f, 0.0008f, 0.0084f), "Mat_IronMan_Silver"),
                new PrimDef("Pelvis_Hip_RetainingCollar_R", PrimitiveType.Cylinder, new Vector3(0.0092f, 0f, 0f), new Vector3(0f, 0f, 270f), new Vector3(0.0084f, 0.0008f, 0.0084f), "Mat_IronMan_Silver"),

                // Inguinal Fluidic Banjo Unions & Elbows
                new PrimDef("Inguinal_BanjoRing_L", PrimitiveType.Cylinder, new Vector3(-0.0104f, 0.0028f, 0.0012f), new Vector3(75f, 335f, 0f), new Vector3(0.0018f, 0.0008f, 0.0018f), "Mat_IronMan_Silver"),
                new PrimDef("Inguinal_BanjoRing_R", PrimitiveType.Cylinder, new Vector3(0.0104f, 0.0028f, 0.0012f), new Vector3(75f, 25f, 0f), new Vector3(0.0018f, 0.0008f, 0.0018f), "Mat_IronMan_Silver"),
                new PrimDef("Inguinal_CrushWasher_L", PrimitiveType.Cylinder, new Vector3(-0.0103f, 0.0024f, 0.0011f), new Vector3(75f, 335f, 0f), new Vector3(0.0019f, 0.0003f, 0.0019f), "Mat_CopperCoil"),
                new PrimDef("Inguinal_CrushWasher_R", PrimitiveType.Cylinder, new Vector3(0.0103f, 0.0024f, 0.0011f), new Vector3(75f, 25f, 0f), new Vector3(0.0019f, 0.0003f, 0.0019f), "Mat_CopperCoil"),
                new PrimDef("Inguinal_FlexLine_L", PrimitiveType.Cylinder, new Vector3(-0.0082f, 0.0010f, 0.0023f), new Vector3(20f, 10f, 355f), new Vector3(0.0009f, 0.0016f, 0.0009f), "Mat_CopperCoil"),
                new PrimDef("Inguinal_FlexLine_R", PrimitiveType.Cylinder, new Vector3(0.0082f, 0.0010f, 0.0023f), new Vector3(20f, 350f, 5f), new Vector3(0.0009f, 0.0016f, 0.0009f), "Mat_CopperCoil"),

                // Codpiece & Dovetail Release Latches
                new PrimDef("Pelvis_Codpiece_Main", PrimitiveType.Cube, new Vector3(0f, -0.0018f, 0.0068f), new Vector3(18f, 0f, 0f), new Vector3(0.0120f, 0.0080f, 0.0080f), "Mat_IronMan_Red"),
                new PrimDef("Pelvis_Codpiece_Flange", PrimitiveType.Cube, new Vector3(0f, -0.0042f, 0.0050f), new Vector3(35f, 0f, 0f), new Vector3(0.0075f, 0.0035f, 0.0050f), "Mat_IronMan_Gold"),
                new PrimDef("Codpiece_GuideRail_Central", PrimitiveType.Cube, new Vector3(0f, -0.0052f, 0.0032f), new Vector3(45f, 0f, 0f), new Vector3(0.0024f, 0.0010f, 0.0060f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Codpiece_Latch_CamLever", PrimitiveType.Cube, new Vector3(0f, -0.0061f, 0.0050f), new Vector3(40f, 0f, 0f), new Vector3(0.0018f, 0.0006f, 0.0010f), "Mat_IronMan_Gold"),

                // Flare Dispensary Pods (Bilateral Staggered Ejection Tubes)
                new PrimDef("Pelvis_Hip_Pod_Chassis_L", PrimitiveType.Cube, new Vector3(-0.0152f, 0.0005f, 0f), new Vector3(0f, 0f, 10f), new Vector3(0.0040f, 0.0065f, 0.0120f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Pelvis_Hip_Pod_Chassis_R", PrimitiveType.Cube, new Vector3(0.0152f, 0.0005f, 0f), new Vector3(0f, 0f, 350f), new Vector3(0.0040f, 0.0065f, 0.0120f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Pelvis_Hip_Pod_Cap_L", PrimitiveType.Cube, new Vector3(-0.0162f, 0.0028f, 0f), new Vector3(0f, 0f, 10f), new Vector3(0.0022f, 0.0022f, 0.0110f), "Mat_IronMan_Gold"),
                new PrimDef("Pelvis_Hip_Pod_Cap_R", PrimitiveType.Cube, new Vector3(0.0162f, 0.0028f, 0f), new Vector3(0f, 0f, 350f), new Vector3(0.0022f, 0.0022f, 0.0110f), "Mat_IronMan_Gold"),
                new PrimDef("FlarePod_Tube_L1", PrimitiveType.Cylinder, new Vector3(-0.0163f, 0.0038f, 0.0036f), new Vector3(0f, 0f, 10f), new Vector3(0.0013f, 0.0006f, 0.0013f), "Mat_IronMan_DarkMetal"),
                new PrimDef("FlarePod_Tube_R1", PrimitiveType.Cylinder, new Vector3(0.0163f, 0.0038f, 0.0036f), new Vector3(0f, 0f, 350f), new Vector3(0.0013f, 0.0006f, 0.0013f), "Mat_IronMan_DarkMetal"),
                new PrimDef("FlarePod_Tube_L2", PrimitiveType.Cylinder, new Vector3(-0.0163f, 0.0038f, 0.0012f), new Vector3(0f, 0f, 10f), new Vector3(0.0013f, 0.0006f, 0.0013f), "Mat_IronMan_DarkMetal"),
                new PrimDef("FlarePod_Tube_R2", PrimitiveType.Cylinder, new Vector3(0.0163f, 0.0038f, 0.0012f), new Vector3(0f, 0f, 350f), new Vector3(0.0013f, 0.0006f, 0.0013f), "Mat_IronMan_DarkMetal"),
                new PrimDef("FlarePod_Tube_L3", PrimitiveType.Cylinder, new Vector3(-0.0163f, 0.0038f, -0.0012f), new Vector3(0f, 0f, 10f), new Vector3(0.0013f, 0.0006f, 0.0013f), "Mat_IronMan_DarkMetal"),
                new PrimDef("FlarePod_Tube_R3", PrimitiveType.Cylinder, new Vector3(0.0163f, 0.0038f, -0.0012f), new Vector3(0f, 0f, 350f), new Vector3(0.0013f, 0.0006f, 0.0013f), "Mat_IronMan_DarkMetal"),
                new PrimDef("FlarePod_Tube_L4", PrimitiveType.Cylinder, new Vector3(-0.0163f, 0.0038f, -0.0036f), new Vector3(0f, 0f, 10f), new Vector3(0.0013f, 0.0006f, 0.0013f), "Mat_IronMan_DarkMetal"),
                new PrimDef("FlarePod_Tube_R4", PrimitiveType.Cylinder, new Vector3(0.0163f, 0.0038f, -0.0036f), new Vector3(0f, 0f, 350f), new Vector3(0.0013f, 0.0006f, 0.0013f), "Mat_IronMan_DarkMetal"),
                new PrimDef("FlarePod_TorsionSpring_L", PrimitiveType.Cylinder, new Vector3(-0.0172f, -0.0022f, 0f), new Vector3(90f, 0f, 0f), new Vector3(0.0011f, 0.0025f, 0.0011f), "Mat_CopperCoil"),
                new PrimDef("FlarePod_TorsionSpring_R", PrimitiveType.Cylinder, new Vector3(0.0172f, -0.0022f, 0f), new Vector3(90f, 0f, 0f), new Vector3(0.0011f, 0.0025f, 0.0011f), "Mat_CopperCoil"),

                // Gluteal Reverse-Thrust Micro-Slits & Sacral Mount
                new PrimDef("Pelvis_Gluteal_Plate_L", PrimitiveType.Cube, new Vector3(-0.0070f, -0.0010f, -0.0080f), new Vector3(345f, 5f, 0f), new Vector3(0.0100f, 0.0070f, 0.0055f), "Mat_IronMan_Red"),
                new PrimDef("Pelvis_Gluteal_Plate_R", PrimitiveType.Cube, new Vector3(0.0070f, -0.0010f, -0.0080f), new Vector3(345f, 355f, 0f), new Vector3(0.0100f, 0.0070f, 0.0055f), "Mat_IronMan_Red"),
                new PrimDef("Gluteal_SlitPlasma_L1", PrimitiveType.Cube, new Vector3(-0.0070f, 0.0012f, -0.0104f), new Vector3(345f, 5f, 0f), new Vector3(0.0055f, 0.0002f, 0.0004f), "Mat_EmissiveBlue"),
                new PrimDef("Gluteal_SlitPlasma_R1", PrimitiveType.Cube, new Vector3(0.0070f, 0.0012f, -0.0104f), new Vector3(345f, 355f, 0f), new Vector3(0.0055f, 0.0002f, 0.0004f), "Mat_EmissiveBlue"),
                new PrimDef("Gluteal_SlitPlasma_L2", PrimitiveType.Cube, new Vector3(-0.0070f, -0.0005f, -0.0102f), new Vector3(345f, 5f, 0f), new Vector3(0.0055f, 0.0002f, 0.0004f), "Mat_EmissiveBlue"),
                new PrimDef("Gluteal_SlitPlasma_R2", PrimitiveType.Cube, new Vector3(0.0070f, -0.0005f, -0.0102f), new Vector3(345f, 355f, 0f), new Vector3(0.0055f, 0.0002f, 0.0004f), "Mat_EmissiveBlue"),
                new PrimDef("Pelvis_Lumbar_Spine_Mount", PrimitiveType.Cube, new Vector3(0f, 0.0012f, -0.0090f), new Vector3(352f, 0f, 0f), new Vector3(0.0055f, 0.0050f, 0.0035f), "Mat_IronMan_Silver"),
                new PrimDef("Sacral_S1_ArticulationCup", PrimitiveType.Cylinder, new Vector3(0f, 0.0042f, -0.0092f), new Vector3(8f, 0f, 0f), new Vector3(0.0042f, 0.0018f, 0.0042f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Sacral_FluidCoupler_L", PrimitiveType.Cylinder, new Vector3(-0.0045f, 0.0045f, -0.0112f), Vector3.zero, new Vector3(0.0015f, 0.0018f, 0.0015f), "Mat_CopperCoil"),
                new PrimDef("Sacral_FluidCoupler_R", PrimitiveType.Cylinder, new Vector3(0.0045f, 0.0045f, -0.0112f), Vector3.zero, new Vector3(0.0015f, 0.0018f, 0.0015f), "Mat_CopperCoil")
            };
        }

        // =========================================================================
        // LEGS & BOOTS MICRO-MECHANICAL DEFINITIONS (FLUSH AT Y = 0.0000m)
        // =========================================================================
        private static List<PrimDef> GetLegMicroDefs()
        {
            return new List<PrimDef>
            {
                // Femoral Load-Bearing Core & Floating Vastus Lateralis
                new PrimDef("Thigh_Proximal_Collar", PrimitiveType.Cylinder, new Vector3(0f, 0.0242f, 0f), Vector3.zero, new Vector3(0.0105f, 0.0018f, 0.0105f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Thigh_Actuator_Hydraulic", PrimitiveType.Cylinder, new Vector3(-0.0025f, 0.0230f, 0.0028f), new Vector3(12f, 0f, 352f), new Vector3(0.0022f, 0.0035f, 0.0022f), "Mat_IronMan_Silver"),
                new PrimDef("Thigh_Actuator_Rod", PrimitiveType.Cylinder, new Vector3(-0.0022f, 0.0205f, 0.0032f), new Vector3(12f, 0f, 352f), new Vector3(0.0014f, 0.0030f, 0.0014f), "Mat_IronMan_Silver"),
                new PrimDef("Thigh_Rectus_Femoris_Main", PrimitiveType.Cube, new Vector3(0f, 0.0205f, 0.0042f), new Vector3(8f, 0f, 0f), new Vector3(0.0075f, 0.0065f, 0.0035f), "Mat_IronMan_Gold"),
                new PrimDef("Thigh_Rectus_Femoris_Ridge", PrimitiveType.Cube, new Vector3(0f, 0.0205f, 0.0058f), new Vector3(8f, 0f, 0f), new Vector3(0.0018f, 0.0062f, 0.0012f), "Mat_IronMan_Gold"),
                new PrimDef("Thigh_Vastus_Lateralis", PrimitiveType.Cube, new Vector3(-0.0040f, 0.0208f, 0.0005f), new Vector3(4f, 0f, 350f), new Vector3(0.0035f, 0.0075f, 0.0090f), "Mat_IronMan_Red"),
                new PrimDef("Thigh_Vastus_Medialis", PrimitiveType.Cube, new Vector3(0.0035f, 0.0198f, 0.0015f), new Vector3(4f, 0f, 6f), new Vector3(0.0030f, 0.0060f, 0.0075f), "Mat_IronMan_Red"),
                new PrimDef("Thigh_Hamstring_Shell", PrimitiveType.Cube, new Vector3(0f, 0.0210f, -0.0042f), new Vector3(352f, 0f, 0f), new Vector3(0.0085f, 0.0070f, 0.0040f), "Mat_IronMan_Red"),
                new PrimDef("Thigh_Hamstring_TieBar", PrimitiveType.Cylinder, new Vector3(-0.0015f, 0.0208f, -0.0055f), new Vector3(350f, 0f, 355f), new Vector3(0.0015f, 0.0055f, 0.0015f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Thigh_Hamstring_Conduit", PrimitiveType.Capsule, new Vector3(0.0015f, 0.0208f, -0.0055f), new Vector3(350f, 0f, 5f), new Vector3(0.0013f, 0.0055f, 0.0013f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Femoral_Cryo_Hose", PrimitiveType.Cylinder, new Vector3(-0.0014f, 0.0182f, 0.0054f), new Vector3(8f, 2f, 358f), new Vector3(0.0008f, 0.0042f, 0.0008f), "Mat_CopperCoil"),
                new PrimDef("Femoral_Banjo_Hip_Bolt", PrimitiveType.Cylinder, new Vector3(-0.0022f, 0.0222f, 0.0046f), new Vector3(0f, 0f, 90f), new Vector3(0.0014f, 0.0006f, 0.0014f), "Mat_IronMan_Gold"),
                new PrimDef("Hamstring_Spring_Coil_Lat", PrimitiveType.Cylinder, new Vector3(-0.0024f, 0.0175f, -0.0038f), Vector3.zero, new Vector3(0.0013f, 0.0020f, 0.0013f), "Mat_CopperCoil"),

                // Polycentric Knee Joint & Synchronizing Gear Cogs
                new PrimDef("Knee_Hinge_Block", PrimitiveType.Cube, new Vector3(0f, 0.0168f, 0f), Vector3.zero, new Vector3(0.0085f, 0.0042f, 0.0075f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Knee_Pivot_Axle_Lateral", PrimitiveType.Cylinder, new Vector3(-0.0045f, 0.0168f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.0040f, 0.0012f, 0.0040f), "Mat_IronMan_Silver"),
                new PrimDef("Knee_Pivot_Axle_Medial", PrimitiveType.Cylinder, new Vector3(0.0045f, 0.0168f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.0040f, 0.0012f, 0.0040f), "Mat_IronMan_Silver"),
                new PrimDef("Knee_Cog_Lat_Femoral", PrimitiveType.Cylinder, new Vector3(-0.0050f, 0.0132f, 0.0015f), new Vector3(0f, 0f, 90f), new Vector3(0.0028f, 0.0004f, 0.0028f), "Mat_IronMan_Silver"),
                new PrimDef("Knee_GearTooth_Mesh_Lat", PrimitiveType.Cube, new Vector3(-0.00505f, 0.0122f, 0.00135f), Vector3.zero, new Vector3(0.00035f, 0.0008f, 0.0012f), "Mat_IronMan_Silver"),
                new PrimDef("Knee_Patella_Cop", PrimitiveType.Cube, new Vector3(0f, 0.0172f, 0.0045f), new Vector3(15f, 0f, 0f), new Vector3(0.0070f, 0.0042f, 0.0028f), "Mat_IronMan_Red"),
                new PrimDef("Knee_Patella_Diamond", PrimitiveType.Cube, new Vector3(0f, 0.0173f, 0.0058f), new Vector3(15f, 0f, 45f), new Vector3(0.0026f, 0.0026f, 0.0010f), "Mat_IronMan_Gold"),
                new PrimDef("Knee_Patella_LipGuard", PrimitiveType.Cube, new Vector3(0f, 0.0153f, 0.0042f), new Vector3(25f, 0f, 0f), new Vector3(0.0062f, 0.0018f, 0.0020f), "Mat_IronMan_Gold"),
                new PrimDef("Patella_BleedPort_Core", PrimitiveType.Cylinder, new Vector3(-0.0032f, 0.0118f, 0.0066f), new Vector3(16f, 330f, 0f), new Vector3(0.00045f, 0.00015f, 0.00045f), "Mat_EmissiveBlue"),
                new PrimDef("Knee_Popliteal_Damper", PrimitiveType.Cylinder, new Vector3(-0.0015f, 0.0168f, -0.0038f), Vector3.zero, new Vector3(0.0018f, 0.0035f, 0.0018f), "Mat_IronMan_Silver"),
                new PrimDef("Knee_Popliteal_Piston", PrimitiveType.Cylinder, new Vector3(-0.0015f, 0.0152f, -0.0038f), Vector3.zero, new Vector3(0.0011f, 0.0025f, 0.0011f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Knee_Popliteal_Conduit", PrimitiveType.Capsule, new Vector3(0.0015f, 0.0165f, -0.0038f), new Vector3(0f, 0f, 10f), new Vector3(0.0015f, 0.0040f, 0.0015f), "Mat_IronMan_DarkMetal"),

                // Lower Leg / Tibia & Calf Thrusters
                new PrimDef("Shin_Tibial_Greave", PrimitiveType.Cube, new Vector3(0f, 0.0112f, 0.0030f), new Vector3(6f, 0f, 0f), new Vector3(0.0085f, 0.0085f, 0.0065f), "Mat_IronMan_Red"),
                new PrimDef("Shin_Ridge_Plate", PrimitiveType.Cube, new Vector3(0f, 0.0114f, 0.0062f), new Vector3(6f, 0f, 0f), new Vector3(0.0022f, 0.0078f, 0.0015f), "Mat_IronMan_Gold"),
                new PrimDef("Shin_Peroneal_Flap", PrimitiveType.Cube, new Vector3(-0.0042f, 0.0110f, 0.0005f), new Vector3(0f, 355f, 352f), new Vector3(0.0025f, 0.0080f, 0.0085f), "Mat_IronMan_Red"),
                new PrimDef("Shin_Medial_Splint", PrimitiveType.Cube, new Vector3(0.0038f, 0.0108f, 0.0010f), new Vector3(0f, 5f, 5f), new Vector3(0.0022f, 0.0075f, 0.0075f), "Mat_IronMan_Red"),
                new PrimDef("Calf_AirBrake_Cowl", PrimitiveType.Cube, new Vector3(0f, 0.0118f, -0.0048f), new Vector3(348f, 0f, 0f), new Vector3(0.0082f, 0.0070f, 0.0042f), "Mat_IronMan_Red"),
                new PrimDef("Calf_Radiator_Matrix", PrimitiveType.Cube, new Vector3(0f, 0.0118f, -0.0064f), new Vector3(348f, 0f, 0f), new Vector3(0.0068f, 0.0055f, 0.0012f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Calf_Nozzle_Upper", PrimitiveType.Cylinder, new Vector3(0f, 0.0132f, -0.0072f), new Vector3(285f, 0f, 0f), new Vector3(0.0026f, 0.0018f, 0.0026f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Calf_Copper_Ring_Upper", PrimitiveType.Cylinder, new Vector3(0f, 0.0132f, -0.0076f), new Vector3(285f, 0f, 0f), new Vector3(0.0029f, 0.0006f, 0.0029f), "Mat_CopperCoil"),
                new PrimDef("Calf_Mach_Disk_Upper", PrimitiveType.Cylinder, new Vector3(0f, 0.0132f, -0.0080f), new Vector3(285f, 0f, 0f), new Vector3(0.0018f, 0.0004f, 0.0018f), "Mat_EmissiveBlue"),
                new PrimDef("Calf_Nozzle_Lower", PrimitiveType.Cylinder, new Vector3(0f, 0.0098f, -0.0068f), new Vector3(285f, 0f, 0f), new Vector3(0.0024f, 0.0018f, 0.0024f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Calf_Copper_Ring_Lower", PrimitiveType.Cylinder, new Vector3(0f, 0.0098f, -0.0072f), new Vector3(285f, 0f, 0f), new Vector3(0.0027f, 0.0006f, 0.0027f), "Mat_CopperCoil"),
                new PrimDef("Calf_Mach_Disk_Lower", PrimitiveType.Cylinder, new Vector3(0f, 0.0098f, -0.0076f), new Vector3(285f, 0f, 0f), new Vector3(0.0016f, 0.0004f, 0.0016f), "Mat_EmissiveBlue"),

                // Ankle Articulation & Malleolus Grease Seals
                new PrimDef("Ankle_Gimbal_Ring", PrimitiveType.Cylinder, new Vector3(0f, 0.0062f, 0f), Vector3.zero, new Vector3(0.0090f, 0.0020f, 0.0090f), "Mat_IronMan_Silver"),
                new PrimDef("Ankle_Malleolus_Lateral", PrimitiveType.Cylinder, new Vector3(-0.0042f, 0.0062f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.0032f, 0.0010f, 0.0032f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Ankle_Malleolus_Medial", PrimitiveType.Cylinder, new Vector3(0.0042f, 0.0062f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.0032f, 0.0010f, 0.0032f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Ankle_GreaseSeal_Lat_Ring", PrimitiveType.Cylinder, new Vector3(-0.00555f, 0.0034f, 0.0008f), new Vector3(0f, 0f, 90f), new Vector3(0.0021f, 0.0002f, 0.0021f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Ankle_AttitudeSensor_Lens", PrimitiveType.Cylinder, new Vector3(-0.00475f, 0.0011f, 0.00315f), new Vector3(330f, 345f, 0f), new Vector3(0.00045f, 0.00015f, 0.00045f), "Mat_EmissiveBlue"),
                new PrimDef("Ankle_Achilles_Cylinder", PrimitiveType.Cylinder, new Vector3(0f, 0.0068f, -0.0042f), new Vector3(345f, 0f, 0f), new Vector3(0.0018f, 0.0032f, 0.0018f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Ankle_Achilles_Rod", PrimitiveType.Cylinder, new Vector3(0f, 0.0046f, -0.0048f), new Vector3(345f, 0f, 0f), new Vector3(0.0011f, 0.0028f, 0.0011f), "Mat_IronMan_Silver"),
                new PrimDef("Ankle_Flexion_Gasket", PrimitiveType.Cube, new Vector3(0f, 0.0050f, 0f), Vector3.zero, new Vector3(0.0080f, 0.0018f, 0.0080f), "Mat_IronMan_DarkMetal"),

                // Boot & Tactical Soles (Exact Ground Contact at Y = 0.0000m)
                new PrimDef("Boot_Chassis_Red", PrimitiveType.Cube, new Vector3(0f, 0.0028f, 0.0015f), Vector3.zero, new Vector3(0.0098f, 0.0036f, 0.0135f), "Mat_IronMan_Red"),
                new PrimDef("Boot_Toe_Cap_Gold", PrimitiveType.Cube, new Vector3(0f, 0.0024f, 0.0085f), new Vector3(350f, 0f, 0f), new Vector3(0.0092f, 0.0026f, 0.0045f), "Mat_IronMan_Gold"),
                new PrimDef("Boot_Flex_Seam", PrimitiveType.Cube, new Vector3(0f, 0.0036f, 0.0062f), Vector3.zero, new Vector3(0.0094f, 0.0008f, 0.0008f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Boot_Heel_Spur", PrimitiveType.Cube, new Vector3(0f, 0.0026f, -0.0065f), new Vector3(18f, 0f, 0f), new Vector3(0.0085f, 0.0030f, 0.0032f), "Mat_IronMan_Silver"),
                // Ground flush: Cube, pos.y = 0.0005, scale.y = 0.0010 -> bottom is exactly 0.000000m
                new PrimDef("Boot_Tactical_Sole", PrimitiveType.Cube, new Vector3(0f, 0.0005f, 0.0015f), Vector3.zero, new Vector3(0.0105f, 0.0010f, 0.0165f), "Mat_IronMan_DarkMetal"),
                // Sub-flush recessed repulsors:
                new PrimDef("Boot_Repulsor_Reflector", PrimitiveType.Cylinder, new Vector3(0f, 0.0005f, 0.0030f), new Vector3(180f, 0f, 0f), new Vector3(0.0055f, 0.0003f, 0.0055f), "Mat_IronMan_Silver"),
                new PrimDef("Boot_Repulsor_Coil", PrimitiveType.Cylinder, new Vector3(0f, 0.00045f, 0.0030f), new Vector3(180f, 0f, 0f), new Vector3(0.0046f, 0.00025f, 0.0046f), "Mat_CopperCoil"),
                new PrimDef("Boot_Repulsor_Lens", PrimitiveType.Cylinder, new Vector3(0f, 0.00035f, 0.0030f), new Vector3(180f, 0f, 0f), new Vector3(0.0034f, 0.0002f, 0.0034f), "Mat_EmissiveBlue"),
                new PrimDef("Boot_Heel_MicroThruster", PrimitiveType.Cylinder, new Vector3(0f, 0.0005f, -0.0045f), new Vector3(180f, 0f, 0f), new Vector3(0.0026f, 0.0003f, 0.0026f), "Mat_IronMan_DarkMetal"),
                new PrimDef("Boot_Heel_Thruster_Core", PrimitiveType.Cylinder, new Vector3(0f, 0.00035f, -0.0045f), new Vector3(180f, 0f, 0f), new Vector3(0.0015f, 0.0002f, 0.0015f), "Mat_EmissiveBlue")
            };
        }
    }
}
