using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class MiniIronManLegAnalyzer
{
    [MenuItem("Tools/Analyze Mini Iron Man Legs")]
    public static void RunAnalysis()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=================================================");
        sb.AppendLine("=== MINI IRON MAN LEG UPGRADE SPATIAL ANALYSIS ===");
        sb.AppendLine("=================================================");

        GameObject imageTarget = GameObject.Find("ImageTarget");
        if (imageTarget == null)
        {
            Debug.LogError("[LegAnalyzer] ImageTarget GameObject not found in scene!");
            return;
        }

        Transform targetT = imageTarget.transform;
        Transform boxBotRoot = targetT.Find("BoxBot_Root");
        if (boxBotRoot == null)
        {
            Debug.LogError("[LegAnalyzer] BoxBot_Root not found under ImageTarget!");
            return;
        }

        sb.AppendLine($"ImageTarget Position: {targetT.position}, LocalScale: {targetT.localScale}");
        sb.AppendLine($"BoxBot_Root LocalPosition: {boxBotRoot.localPosition}, LocalRotation: {boxBotRoot.localEulerAngles}, LocalScale: {boxBotRoot.localScale}");

        // -------------------------------------------------------------
        // 1. GROUND CONTACT VERIFICATION
        // -------------------------------------------------------------
        sb.AppendLine("\n--- 1. GROUND CONTACT VERIFICATION ---");
        Transform leftLeg = boxBotRoot.Find("LeftLeg");
        Transform rightLeg = boxBotRoot.Find("RightLeg");

        AnalyzeLegGroundContact(leftLeg, targetT, boxBotRoot, "LeftLeg", sb);
        AnalyzeLegGroundContact(rightLeg, targetT, boxBotRoot, "RightLeg", sb);

        // -------------------------------------------------------------
        // 2. HIP & TORSO ALIGNMENT
        // -------------------------------------------------------------
        sb.AppendLine("\n--- 2. HIP & TORSO ALIGNMENT ---");
        Transform pelvis = boxBotRoot.Find("Pelvis");
        Transform torso = boxBotRoot.Find("Torso");

        if (pelvis != null)
        {
            sb.AppendLine($"Pelvis LocalPosition (rel to BoxBot_Root): {pelvis.localPosition:F6}");
            Transform beltGold = pelvis.Find("Belt_Gold");
            if (beltGold != null)
            {
                Bounds beltBoundsTarget = GetHierarchyBoundsInSpace(beltGold, targetT);
                Bounds beltBoundsRoot = GetHierarchyBoundsInSpace(beltGold, boxBotRoot);
                sb.AppendLine($"Belt_Gold LocalPos: {beltGold.localPosition:F6}, LocalScale: {beltGold.localScale:F6}");
                sb.AppendLine($"Belt_Gold Bounds in BoxBot_Root: Min Y = {beltBoundsRoot.min.y:F6}, Max Y = {beltBoundsRoot.max.y:F6}, Center = {beltBoundsRoot.center:F6}");
            }

            Transform hipBallL = pelvis.Find("Hip_Ball_L");
            Transform hipBallR = pelvis.Find("Hip_Ball_R");
            if (hipBallL != null)
            {
                Vector3 hipLInRoot = boxBotRoot.InverseTransformPoint(hipBallL.position);
                sb.AppendLine($"Hip_Ball_L LocalPos (in Pelvis): {hipBallL.localPosition:F6}, In BoxBot_Root: {hipLInRoot:F6}");
            }
            if (hipBallR != null)
            {
                Vector3 hipRInRoot = boxBotRoot.InverseTransformPoint(hipBallR.position);
                sb.AppendLine($"Hip_Ball_R LocalPos (in Pelvis): {hipBallR.localPosition:F6}, In BoxBot_Root: {hipRInRoot:F6}");
            }
        }
        else
        {
            sb.AppendLine("Pelvis NOT found!");
        }

        if (torso != null)
        {
            sb.AppendLine($"Torso LocalPosition (rel to BoxBot_Root): {torso.localPosition:F6}");
            Bounds torsoBoundsRoot = GetHierarchyBoundsInSpace(torso, boxBotRoot);
            sb.AppendLine($"Torso Entire Bounds in BoxBot_Root: Min Y = {torsoBoundsRoot.min.y:F6}, Max Y = {torsoBoundsRoot.max.y:F6}");

            // Check for hypogastric / lumbar or children
            foreach (Transform child in torso)
            {
                Bounds cb = GetHierarchyBoundsInSpace(child, boxBotRoot);
                sb.AppendLine($"  Torso Child '{child.name}': Min Y = {cb.min.y:F6}, Max Y = {cb.max.y:F6}, Center Y = {cb.center.y:F6}");
            }
        }
        else
        {
            sb.AppendLine("Torso NOT found!");
        }

        if (leftLeg != null)
            sb.AppendLine($"LeftLeg Pivot LocalPos (rel to BoxBot_Root): {leftLeg.localPosition:F6}");
        if (rightLeg != null)
            sb.AppendLine($"RightLeg Pivot LocalPos (rel to BoxBot_Root): {rightLeg.localPosition:F6}");

        // -------------------------------------------------------------
        // 3. VIRTUAL BUTTON OCCLUSION / RAYCAST CLEARANCE
        // -------------------------------------------------------------
        sb.AppendLine("\n--- 3. VIRTUAL BUTTON OCCLUSION / RAYCAST CLEARANCE ---");
        Transform vbtnMood = targetT.Find("VBtn_Mood");
        Transform vbtnAction = targetT.Find("VBtn_Action");
        Transform vbtnSound = targetT.Find("VBtn_Sound");

        AnalyzeButton("VBtn_Mood", vbtnMood, targetT, sb);
        AnalyzeButton("VBtn_Action", vbtnAction, targetT, sb);
        AnalyzeButton("VBtn_Sound", vbtnSound, targetT, sb);

        // Compute XZ Footprints of Legs and minimum clearance to buttons
        Bounds leftLegBoundsTarget = leftLeg != null ? GetHierarchyBoundsInSpace(leftLeg, targetT) : new Bounds();
        Bounds rightLegBoundsTarget = rightLeg != null ? GetHierarchyBoundsInSpace(rightLeg, targetT) : new Bounds();

        sb.AppendLine($"\nLeftLeg Bounds in ImageTarget Space: Min={leftLegBoundsTarget.min:F4}, Max={leftLegBoundsTarget.max:F4}, Size={leftLegBoundsTarget.size:F4}");
        sb.AppendLine($"RightLeg Bounds in ImageTarget Space: Min={rightLegBoundsTarget.min:F4}, Max={rightLegBoundsTarget.max:F4}, Size={rightLegBoundsTarget.size:F4}");

        Bounds combinedLegsTarget = leftLegBoundsTarget;
        combinedLegsTarget.Encapsulate(rightLegBoundsTarget);
        sb.AppendLine($"Combined Legs Bounds (ImageTarget space): X=[{combinedLegsTarget.min.x:F4}, {combinedLegsTarget.max.x:F4}], Z=[{combinedLegsTarget.min.z:F4}, {combinedLegsTarget.max.z:F4}]");

        CheckClearanceToButton("VBtn_Mood", vbtnMood, combinedLegsTarget, targetT, sb);
        CheckClearanceToButton("VBtn_Action", vbtnAction, combinedLegsTarget, targetT, sb);
        CheckClearanceToButton("VBtn_Sound", vbtnSound, combinedLegsTarget, targetT, sb);

        // -------------------------------------------------------------
        // 4. SYMMETRY & TRANSFORM HIERARCHY & COLLIDERS
        // -------------------------------------------------------------
        sb.AppendLine("\n--- 4. SYMMETRY, TRANSFORM HIERARCHY & COLLIDERS ---");
        CompareLegSymmetry(leftLeg, rightLeg, sb);

        sb.AppendLine("\nCollider Audit across Mini Iron Man (Pelvis, LeftLeg, RightLeg):");
        CheckColliders(pelvis, sb);
        CheckColliders(leftLeg, sb);
        CheckColliders(rightLeg, sb);

        Debug.Log(sb.ToString());
        
        string logPath = "Assets/Editor/LegUpgradeAnalysisReport.txt";
        System.IO.File.WriteAllText(logPath, sb.ToString());
        Debug.Log($"[LegAnalyzer] Analysis saved to {logPath}");
    }

    private static void AnalyzeLegGroundContact(Transform leg, Transform targetT, Transform rootT, string legName, StringBuilder sb)
    {
        if (leg == null)
        {
            sb.AppendLine($"{legName} is NULL!");
            return;
        }

        sb.AppendLine($"\n[{legName}] Details:");
        sb.AppendLine($"  Pivot LocalPos in BoxBot_Root: {leg.localPosition:F6}");
        sb.AppendLine($"  Pivot LocalEulerAngles: {leg.localEulerAngles:F3}, LocalScale: {leg.localScale:F3}");

        float lowestYInRoot = float.MaxValue;
        string lowestPartInRoot = "";

        float lowestYInTarget = float.MaxValue;
        string lowestPartInTarget = "";

        foreach (Transform child in leg)
        {
            Bounds bInRoot = GetHierarchyBoundsInSpace(child, rootT);
            Bounds bInTarget = GetHierarchyBoundsInSpace(child, targetT);

            if (bInRoot.min.y < lowestYInRoot)
            {
                lowestYInRoot = bInRoot.min.y;
                lowestPartInRoot = child.name;
            }

            if (bInTarget.min.y < lowestYInTarget)
            {
                lowestYInTarget = bInTarget.min.y;
                lowestPartInTarget = child.name;
            }

            // If it's a sole or boot part, log explicitly
            if (child.name.Contains("Sole") || child.name.Contains("Boot"))
            {
                sb.AppendLine($"  {child.name}:");
                sb.AppendLine($"    LocalPos: {child.localPosition:F6}, LocalScale: {child.localScale:F6}, LocalRot: {child.localEulerAngles:F3}");
                sb.AppendLine($"    In BoxBot_Root -> Min Y: {bInRoot.min.y:F6}m, Max Y: {bInRoot.max.y:F6}m, Center Y: {bInRoot.center.y:F6}m");
                sb.AppendLine($"    In ImageTarget -> Min Y: {bInTarget.min.y:F6}m, Max Y: {bInTarget.max.y:F6}m, Center Y: {bInTarget.center.y:F6}m");
            }
        }

        sb.AppendLine($"  >>> LOWEST PRIMITIVE IN ROOT: '{lowestPartInRoot}' at Y = {lowestYInRoot:F6}m ({lowestYInRoot * 1000f:F3} mm)");
        sb.AppendLine($"  >>> LOWEST PRIMITIVE IN TARGET: '{lowestPartInTarget}' at Y = {lowestYInTarget:F6}m ({lowestYInTarget * 1000f:F3} mm)");
        
        bool inRange = lowestYInTarget >= -0.00005f && lowestYInTarget <= 0.00025f;
        sb.AppendLine($"  >>> Ground Contact Criteria (0.0000m to 0.0002m): {(inRange ? "PASS" : "FAIL (Out of tolerance)")}");
    }

    private static void AnalyzeButton(string name, Transform btn, Transform targetT, StringBuilder sb)
    {
        if (btn == null)
        {
            sb.AppendLine($"Button '{name}' NOT found!");
            return;
        }

        Vector3 localPos = targetT.InverseTransformPoint(btn.position);
        Bounds b = GetHierarchyBoundsInSpace(btn, targetT);
        Collider col = btn.GetComponentInChildren<Collider>();

        sb.AppendLine($"{name}:");
        sb.AppendLine($"  LocalPos rel to ImageTarget: {localPos:F5}");
        sb.AppendLine($"  Bounds in ImageTarget: Min={b.min:F4}, Max={b.max:F4}, Center={b.center:F4}, Size={b.size:F4}");
        if (col != null)
        {
            sb.AppendLine($"  Collider: {col.GetType().Name}, isTrigger={col.isTrigger}, enabled={col.enabled}, bounds in world={col.bounds}");
        }
        else
        {
            sb.AppendLine("  Collider: NONE found!");
        }
    }

    private static void CheckClearanceToButton(string btnName, Transform btn, Bounds legsBoundsTarget, Transform targetT, StringBuilder sb)
    {
        if (btn == null) return;
        Bounds btnBounds = GetHierarchyBoundsInSpace(btn, targetT);

        // 2D distance in XZ plane
        float dx = 0f;
        if (btnBounds.min.x > legsBoundsTarget.max.x)
            dx = btnBounds.min.x - legsBoundsTarget.max.x;
        else if (legsBoundsTarget.min.x > btnBounds.max.x)
            dx = legsBoundsTarget.min.x - btnBounds.max.x;

        float dz = 0f;
        if (btnBounds.min.z > legsBoundsTarget.max.z)
            dz = btnBounds.min.z - legsBoundsTarget.max.z;
        else if (legsBoundsTarget.min.z > btnBounds.max.z)
            dz = legsBoundsTarget.min.z - btnBounds.max.z;

        float clearance2D = Mathf.Sqrt(dx * dx + dz * dz);
        sb.AppendLine($"Clearance from Leg Envelope to {btnName}:");
        sb.AppendLine($"  dx = {dx:F4}m ({dx * 100f:F2} cm), dz = {dz:F4}m ({dz * 100f:F2} cm) -> 2D Radial Gap = {clearance2D:F4}m ({clearance2D * 100f:F2} cm)");
        bool safe = clearance2D > 0.015f;
        sb.AppendLine($"  Occlusion / Raycast Safety: {(safe ? "SAFE (Generous Clearance)" : "WARNING: Potential overlap or tight clearance")}");
    }

    private static void CompareLegSymmetry(Transform leftLeg, Transform rightLeg, StringBuilder sb)
    {
        if (leftLeg == null || rightLeg == null) return;

        sb.AppendLine("Comparing LeftLeg vs RightLeg children transforms:");
        foreach (Transform lChild in leftLeg)
        {
            Transform rChild = rightLeg.Find(lChild.name);
            if (rChild == null)
            {
                sb.AppendLine($"  Missing in RightLeg: {lChild.name}");
                continue;
            }

            Vector3 lp = lChild.localPosition;
            Vector3 rp = rChild.localPosition;
            Vector3 lr = lChild.localEulerAngles;
            Vector3 rr = rChild.localEulerAngles;
            Vector3 ls = lChild.localScale;
            Vector3 rs = rChild.localScale;

            sb.AppendLine($"  [{lChild.name}]");
            sb.AppendLine($"    Left:  pos=({lp.x:F4}, {lp.y:F4}, {lp.z:F4}) rot=({lr.x:F1}, {lr.y:F1}, {lr.z:F1}) scale=({ls.x:F4}, {ls.y:F4}, {ls.z:F4})");
            sb.AppendLine($"    Right: pos=({rp.x:F4}, {rp.y:F4}, {rp.z:F4}) rot=({rr.x:F1}, {rr.y:F1}, {rr.z:F1}) scale=({rs.x:F4}, {rs.y:F4}, {rs.z:F4})");
        }
    }

    private static void CheckColliders(Transform root, StringBuilder sb)
    {
        if (root == null) return;
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        sb.AppendLine($"  {root.name}: Found {colliders.Length} colliders.");
        foreach (var col in colliders)
        {
            sb.AppendLine($"    Object: '{col.gameObject.name}', Type: {col.GetType().Name}, Enabled: {col.enabled}, IsTrigger: {col.isTrigger}, Layer: {LayerMask.LayerToName(col.gameObject.layer)}");
        }
    }

    private static Bounds GetHierarchyBoundsInSpace(Transform root, Transform space)
    {
        MeshFilter[] mfs = root.GetComponentsInChildren<MeshFilter>(true);
        if (mfs.Length == 0)
        {
            Vector3 p = space.InverseTransformPoint(root.position);
            return new Bounds(p, Vector3.zero);
        }

        Bounds totalBounds = new Bounds();
        bool first = true;

        foreach (var mf in mfs)
        {
            if (mf.sharedMesh == null) continue;
            Mesh m = mf.sharedMesh;
            Vector3[] vertices = m.vertices;
            Transform t = mf.transform;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 worldPt = t.TransformPoint(vertices[i]);
                Vector3 spacePt = space.InverseTransformPoint(worldPt);
                if (first)
                {
                    totalBounds = new Bounds(spacePt, Vector3.zero);
                    first = false;
                }
                else
                {
                    totalBounds.Encapsulate(spacePt);
                }
            }
        }

        return totalBounds;
    }
}
