using UnityEngine;

/// <summary>
/// Production-Ready VFX & Laser Controller for BoxBot Action Mode in URP.
/// Manages:
/// 1. Volumetric dual-layer Unibeam LineRenderer (Core + Mantle) and Concentric Collimator Rings.
/// 2. Dual Palm Repulsor Pulse LineRenderers and Muzzle Orbs.
/// 3. Planar Ground Heat Glow projection.
/// 4. Managed Dynamic Shared Material: 100% SRP Batcher compatible, zero material duplication leaks.
/// 5. Zero-Collider Policy: Guarantees zero interference with optical touch raycasts.
/// </summary>
public class BoxBotVFXController : MonoBehaviour
{
    [Header("=== Muzzle & Beam Anchors ===")]
    [SerializeField] private Transform torsoTransform;
    [SerializeField] private Transform reactorCoreTransform;
    [SerializeField] private Transform leftRepulsorTransform;
    [SerializeField] private Transform rightRepulsorTransform;

    public Vector3 ArcReactorOrigin
    {
        get
        {
            if (reactorCoreTransform != null) return reactorCoreTransform.position;
            if (torsoTransform != null) return torsoTransform.TransformPoint(new Vector3(0f, 0.006f, 0.015f));
            return transform.TransformPoint(new Vector3(0f, 0.054f, 0.015f));
        }
    }

    public Vector3 ChestAimForward
    {
        get
        {
            // Crucial: Torso.forward or transform.forward always points horizontally along chest aim.
            // Do NOT use reactorCoreTransform.forward because the cylinder is rotated 90 deg down.
            if (torsoTransform != null) return torsoTransform.forward;
            return transform.forward;
        }
    }

    public Vector3 LeftRepulsorOrigin
    {
        get
        {
            if (leftRepulsorTransform == null || IsArmPivotOnly(leftRepulsorTransform))
            {
                leftRepulsorTransform = ResolveRepulsorTransform(leftRepulsorTransform, true);
            }

            if (leftRepulsorTransform != null)
            {
                if (IsArmPivotOnly(leftRepulsorTransform))
                {
                    // Fall back to accurate local offset relative to arm pivot (wrist / iris palm location)
                    return leftRepulsorTransform.TransformPoint(new Vector3(0f, -0.0410f, -0.0035f));
                }
                return leftRepulsorTransform.position;
            }
            return transform.TransformPoint(new Vector3(-0.029f, 0.056f, 0.041f));
        }
    }

    public Vector3 RightRepulsorOrigin
    {
        get
        {
            if (rightRepulsorTransform == null || IsArmPivotOnly(rightRepulsorTransform))
            {
                rightRepulsorTransform = ResolveRepulsorTransform(rightRepulsorTransform, false);
            }

            if (rightRepulsorTransform != null)
            {
                if (IsArmPivotOnly(rightRepulsorTransform))
                {
                    // Fall back to accurate local offset relative to arm pivot (wrist / iris palm location)
                    return rightRepulsorTransform.TransformPoint(new Vector3(0f, -0.0410f, -0.0035f));
                }
                return rightRepulsorTransform.position;
            }
            return transform.TransformPoint(new Vector3(0.029f, 0.056f, 0.041f));
        }
    }

    private static bool IsArmPivotOnly(Transform t)
    {
        if (t == null) return false;
        string n = t.name;
        return n.Contains("ArmPivot") && !n.Contains("Repulsor") && !n.Contains("Iris") && !n.Contains("Palm");
    }

    private Transform ResolveRepulsorTransform(Transform current, bool isLeft)
    {
        if (current != null && !IsArmPivotOnly(current))
        {
            return current;
        }

        string armName = isLeft ? "LeftArmPivot" : "RightArmPivot";
        Transform arm = (current != null && current.name.Contains(armName))
            ? current
            : (transform.Find(armName) ?? transform.Find($"BoxBot_Root/{armName}"));

        if (arm != null)
        {
            // 1. Direct RepulsorPalm child
            Transform palm = arm.Find("RepulsorPalm");
            if (palm != null) return palm;

            // 2. Recursive search for RepulsorPalm under wrist/iris hub
            foreach (Transform c in arm.GetComponentsInChildren<Transform>(true))
            {
                if (c.name.Equals("RepulsorPalm", System.StringComparison.OrdinalIgnoreCase))
                    return c;
            }

            // 3. Wrist / Iris hub focus collar or outer bezel
            foreach (Transform c in arm.GetComponentsInChildren<Transform>(true))
            {
                if (c.name.Contains("Repulsor_Iris_FocusCollar") || c.name.Contains("Repulsor_Iris_OuterBezel"))
                    return c;
            }

            // 4. Any Repulsor component
            foreach (Transform c in arm.GetComponentsInChildren<Transform>(true))
            {
                if (c.name.Contains("Repulsor"))
                    return c;
            }

            return arm;
        }

        return current;
    }

    [Header("=== Shaders & Materials Templates ===")]
    [SerializeField] private Shader laserBeamShader;
    [SerializeField] private Shader collimatorShader;
    [SerializeField] private Shader groundGlowShader;
    [SerializeField] private Material baseEmissiveTemplate;

    [Header("=== Managed Emissive Suit Renderers ===")]
    [SerializeField] private Renderer[] emissiveSuitRenderers;

    // Procedural LineRenderers
    private LineRenderer repulsorBeamCore;
    private LineRenderer repulsorBeamMantle;
    private LineRenderer leftRepulsorBeamCore;
    private LineRenderer leftRepulsorBeamMantle;
    private LineRenderer rightRepulsorBeamCore;
    private LineRenderer rightRepulsorBeamMantle;
    private LineRenderer unibeamCore;
    private LineRenderer unibeamMantle;

    // Procedural Rings & Ground Glow
    private Transform collimatorRoot;
    private GameObject[] collimatorRings = new GameObject[3];
    private MeshRenderer groundHeatGlowRenderer;
    private Transform leftMuzzleOrb;
    private Transform rightMuzzleOrb;

    // Dynamic Shared Runtime Materials (100% SRP Batcher Compatible)
    private Material dynamicSuitEmissiveMaterial;
    private Material dynamicLaserMantleMaterial;
    private Material dynamicCollimatorMaterial;
    private Material dynamicGroundGlowMaterial;

    // Pre-allocated non-allocating buffers (100% zero-GC)
    private readonly Vector3[] twoPointBuffer = new Vector3[2];
    private readonly Gradient cachedBeamGradient = new Gradient();
    private readonly GradientColorKey[] colorKeyBuffer = new GradientColorKey[2];
    private readonly GradientAlphaKey[] alphaKeyBuffer = new GradientAlphaKey[2];
    private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");
    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int ProgressID = Shader.PropertyToID("_Progress");

    private Color currentMoodColor = new Color(0f, 4.5f, 5f, 1f); // Default Cyan HDR
    private bool isInitialized = false;

    private void Awake()
    {
        InitializeVFX();
    }

    public void InitializeVFX()
    {
        if (isInitialized) return;

        // Auto-locate shaders if not assigned in inspector
        if (laserBeamShader == null) laserBeamShader = Shader.Find("BoxBot/VFX/LaserEnergyBeam") ?? Shader.Find("Universal Render Pipeline/Unlit");
        if (collimatorShader == null) collimatorShader = Shader.Find("BoxBot/VFX/EnergyRingCollimator") ?? Shader.Find("Universal Render Pipeline/Unlit");
        if (groundGlowShader == null) groundGlowShader = Shader.Find("BoxBot/VFX/GroundHeatGlow") ?? Shader.Find("Universal Render Pipeline/Unlit");

        // 1. Managed Dynamic Shared Material for Suit Optics
        if (baseEmissiveTemplate == null)
        {
            baseEmissiveTemplate = Resources.Load<Material>("Mat_EmissiveCyan");
            if (baseEmissiveTemplate == null)
            {
                var defaultShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                baseEmissiveTemplate = new Material(defaultShader);
                baseEmissiveTemplate.EnableKeyword("_EMISSION");
            }
        }

        dynamicSuitEmissiveMaterial = new Material(baseEmissiveTemplate) { name = "BoxBot_DynamicSuitEmissive_Runtime" };
        dynamicLaserMantleMaterial = new Material(laserBeamShader) { name = "BoxBot_DynamicLaserMantle_Runtime" };
        dynamicCollimatorMaterial = new Material(collimatorShader) { name = "BoxBot_DynamicCollimator_Runtime" };
        dynamicGroundGlowMaterial = new Material(groundGlowShader) { name = "BoxBot_DynamicGroundGlow_Runtime" };

        // Auto-discover suit optics if not assigned
        if (emissiveSuitRenderers == null || emissiveSuitRenderers.Length == 0)
        {
            var list = new System.Collections.Generic.List<Renderer>();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                string n = r.gameObject.name;
                if (n.Contains("Eye") || n.Contains("Visor") || n.Contains("Reactor") || n.Contains("Repulsor") || n.Contains("Lens"))
                {
                    list.Add(r);
                }
            }
            emissiveSuitRenderers = list.ToArray();
        }

        // Apply shared material to suit renderers
        if (emissiveSuitRenderers != null)
        {
            for (int i = 0; i < emissiveSuitRenderers.Length; i++)
            {
                if (emissiveSuitRenderers[i] != null)
                {
                    emissiveSuitRenderers[i].sharedMaterial = dynamicSuitEmissiveMaterial;
                }
            }
        }

        // 2. Discover Anchors
        if (torsoTransform == null)
        {
            torsoTransform = transform.Find("Torso") ?? transform.Find("BoxBot_Root/Torso") ?? transform;
        }
        if (reactorCoreTransform == null)
        {
            reactorCoreTransform = (torsoTransform != null ? torsoTransform.Find("ReactorCore") : null)
                ?? transform.Find("Torso/ReactorCore")
                ?? transform.Find("BoxBot_Root/Torso/ReactorCore")
                ?? torsoTransform;
        }
        leftRepulsorTransform = ResolveRepulsorTransform(leftRepulsorTransform, true);
        rightRepulsorTransform = ResolveRepulsorTransform(rightRepulsorTransform, false);

        // 3. Build Procedural LineRenderers
        leftRepulsorBeamCore = CreateLineRenderer("LeftRepulsorBeam_Core", 0.0025f, 0.0020f, dynamicSuitEmissiveMaterial);
        leftRepulsorBeamMantle = CreateLineRenderer("LeftRepulsorBeam_Mantle", 0.010f, 0.006f, dynamicLaserMantleMaterial);
        rightRepulsorBeamCore = CreateLineRenderer("RightRepulsorBeam_Core", 0.0025f, 0.0020f, dynamicSuitEmissiveMaterial);
        rightRepulsorBeamMantle = CreateLineRenderer("RightRepulsorBeam_Mantle", 0.010f, 0.006f, dynamicLaserMantleMaterial);

        // Keep repulsorBeamCore/Mantle references valid for backwards compatibility
        repulsorBeamCore = leftRepulsorBeamCore;
        repulsorBeamMantle = leftRepulsorBeamMantle;

        unibeamCore = CreateLineRenderer("Unibeam_Core", 0.0060f, 0.0075f, dynamicSuitEmissiveMaterial);
        unibeamMantle = CreateLineRenderer("Unibeam_Mantle", 0.022f, 0.028f, dynamicLaserMantleMaterial);

        // 4. Build Concentric Collimator Rings
        collimatorRoot = new GameObject("VFX_CollimatorRoot").transform;
        collimatorRoot.SetParent(transform, false);
        for (int i = 0; i < 3; i++)
        {
            collimatorRings[i] = GameObject.CreatePrimitive(PrimitiveType.Quad);
            collimatorRings[i].name = $"CollimatorRing_{i + 1}";
            collimatorRings[i].transform.SetParent(collimatorRoot, false);
            StripColliders(collimatorRings[i]);
            var mr = collimatorRings[i].GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = dynamicCollimatorMaterial;
            collimatorRings[i].SetActive(false);
        }

        // 5. Build Muzzle Orbs
        leftMuzzleOrb = CreateMuzzleOrb("LeftMuzzleOrb", leftRepulsorTransform);
        rightMuzzleOrb = CreateMuzzleOrb("RightMuzzleOrb", rightRepulsorTransform);

        // 6. Build Ground Heat Glow Quad (At Y = 0.0005m, flush on card)
        GameObject groundGlowGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
        groundGlowGO.name = "VFX_GroundHeatGlow";
        groundGlowGO.transform.SetParent(transform, false);
        groundGlowGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        groundGlowGO.transform.localScale = new Vector3(0.08f, 0.45f, 1f);
        groundGlowGO.transform.localPosition = new Vector3(0f, 0.0005f, 0.22f);
        StripColliders(groundGlowGO);
        groundHeatGlowRenderer = groundGlowGO.GetComponent<MeshRenderer>();
        if (groundHeatGlowRenderer != null)
        {
            groundHeatGlowRenderer.sharedMaterial = dynamicGroundGlowMaterial;
            groundHeatGlowRenderer.enabled = false;
        }

        // Sync initial mood colors
        SetMoodColor(currentMoodColor);

        isInitialized = true;
    }

    private LineRenderer CreateLineRenderer(string name, float startW, float endW, Material mat)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        StripColliders(go);

        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.startWidth = startW;
        lr.endWidth = endW;
        lr.positionCount = 2;
        lr.useWorldSpace = true;
        lr.alignment = LineAlignment.View;
        lr.sharedMaterial = mat;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.enabled = false;
        return lr;
    }

    private Transform CreateMuzzleOrb(string name, Transform parent)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.SetParent(parent != null ? parent : transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localScale = Vector3.zero;
        StripColliders(go);

        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null) mr.sharedMaterial = dynamicCollimatorMaterial;
        go.SetActive(false);
        return go.transform;
    }

    private void StripColliders(GameObject go)
    {
        foreach (var col in go.GetComponentsInChildren<Collider>(true))
        {
            DestroyImmediate(col);
        }
    }

    private void OnDestroy()
    {
        // Prevent native memory leaks: clean up runtime material instances
        if (dynamicSuitEmissiveMaterial != null) Destroy(dynamicSuitEmissiveMaterial);
        if (dynamicLaserMantleMaterial != null) Destroy(dynamicLaserMantleMaterial);
        if (dynamicCollimatorMaterial != null) Destroy(dynamicCollimatorMaterial);
        if (dynamicGroundGlowMaterial != null) Destroy(dynamicGroundGlowMaterial);
    }

    #region Mood & Emission Control (Single Memory Mutation, 100% SRP Batcher)

    public void SetMoodColor(Color hdrColor)
    {
        currentMoodColor = hdrColor;

        if (dynamicSuitEmissiveMaterial != null)
        {
            dynamicSuitEmissiveMaterial.SetColor(EmissionColorID, hdrColor);
            dynamicSuitEmissiveMaterial.SetColor(BaseColorID, hdrColor);
        }
        if (dynamicLaserMantleMaterial != null)
        {
            dynamicLaserMantleMaterial.SetColor(BaseColorID, hdrColor);
        }
        if (dynamicCollimatorMaterial != null)
        {
            dynamicCollimatorMaterial.SetColor(BaseColorID, hdrColor);
            dynamicCollimatorMaterial.SetColor("_RingColor", hdrColor);
        }
        if (dynamicGroundGlowMaterial != null)
        {
            dynamicGroundGlowMaterial.SetColor("_GlowColor", hdrColor);
        }

        // Update LineRenderer Gradient Keys with 100% zero-GC
        colorKeyBuffer[0] = new GradientColorKey(Color.white, 0.0f);
        colorKeyBuffer[1] = new GradientColorKey(hdrColor, 1.0f);
        alphaKeyBuffer[0] = new GradientAlphaKey(1.0f, 0.0f);
        alphaKeyBuffer[1] = new GradientAlphaKey(1.0f, 1.0f);
        cachedBeamGradient.SetKeys(colorKeyBuffer, alphaKeyBuffer);

        if (repulsorBeamMantle != null) repulsorBeamMantle.colorGradient = cachedBeamGradient;
        if (leftRepulsorBeamMantle != null) leftRepulsorBeamMantle.colorGradient = cachedBeamGradient;
        if (rightRepulsorBeamMantle != null) rightRepulsorBeamMantle.colorGradient = cachedBeamGradient;
        if (unibeamMantle != null) unibeamMantle.colorGradient = cachedBeamGradient;
    }

    public void SetEmissiveOverdrive(float multiplier)
    {
        if (dynamicSuitEmissiveMaterial != null)
        {
            Color overdriven = currentMoodColor * multiplier;
            dynamicSuitEmissiveMaterial.SetColor(EmissionColorID, overdriven);
        }
    }

    #endregion

    #region Repulsor Pulse VFX Controls

    public void SetRepulsorMuzzleCharge(float progress, bool isLeft)
    {
        Transform orb = isLeft ? leftMuzzleOrb : rightMuzzleOrb;
        if (orb == null) return;

        if (progress <= 0.01f)
        {
            orb.gameObject.SetActive(false);
            return;
        }

        orb.gameObject.SetActive(true);
        // Expand from 0 to 8mm, then gravitational collapse to 1.5mm
        float s = progress < 0.85f
            ? Mathf.Lerp(0f, 0.008f, progress / 0.85f)
            : Mathf.Lerp(0.008f, 0.0015f, (progress - 0.85f) / 0.15f);
        orb.localScale = Vector3.one * s;
    }

    public void FireRepulsorBeam(Vector3 startPoint, Vector3 direction, float distance = 0.40f)
    {
        twoPointBuffer[0] = startPoint;
        twoPointBuffer[1] = startPoint + direction.normalized * distance;

        // Resolve which arm LineRenderer to use based on origin in robot local space
        Vector3 localStart = transform.InverseTransformPoint(startPoint);
        bool isLeft = localStart.x < 0f;

        LineRenderer core = isLeft ? (leftRepulsorBeamCore ?? repulsorBeamCore) : (rightRepulsorBeamCore ?? repulsorBeamCore);
        LineRenderer mantle = isLeft ? (leftRepulsorBeamMantle ?? repulsorBeamMantle) : (rightRepulsorBeamMantle ?? repulsorBeamMantle);

        if (core != null)
        {
            core.SetPositions(twoPointBuffer);
            core.enabled = true;
        }
        if (mantle != null)
        {
            mantle.SetPositions(twoPointBuffer);
            mantle.enabled = true;
        }
    }

    public void ExtinguishRepulsorBeam()
    {
        if (repulsorBeamCore != null) repulsorBeamCore.enabled = false;
        if (repulsorBeamMantle != null) repulsorBeamMantle.enabled = false;
        if (leftRepulsorBeamCore != null) leftRepulsorBeamCore.enabled = false;
        if (leftRepulsorBeamMantle != null) leftRepulsorBeamMantle.enabled = false;
        if (rightRepulsorBeamCore != null) rightRepulsorBeamCore.enabled = false;
        if (rightRepulsorBeamMantle != null) rightRepulsorBeamMantle.enabled = false;
        if (leftMuzzleOrb != null) leftMuzzleOrb.gameObject.SetActive(false);
        if (rightMuzzleOrb != null) rightMuzzleOrb.gameObject.SetActive(false);
    }

    #endregion

    #region Unibeam Laser VFX Controls

    public void SetCollimatorProgress(float progress)
    {
        if (collimatorRings == null) return;

        if (progress <= 0.01f)
        {
            for (int i = 0; i < collimatorRings.Length; i++)
            {
                if (collimatorRings[i] != null) collimatorRings[i].SetActive(false);
            }
            return;
        }

        Vector3 origin = ArcReactorOrigin;
        Vector3 forward = ChestAimForward;
        Vector3 up = torsoTransform != null ? torsoTransform.up : Vector3.up;

        for (int i = 0; i < collimatorRings.Length; i++)
        {
            if (collimatorRings[i] == null) continue;
            collimatorRings[i].SetActive(true);

            // Progressive distance in front of the Arc Reactor along the forward chest aim axis
            float distanceAlongBeam = 0.010f + (i + 1) * 0.014f * Mathf.Lerp(0.6f, 1.0f, progress);
            collimatorRings[i].transform.position = origin + forward * distanceAlongBeam;

            // ORIENTATION FIX:
            // Ensure the rings stand VERTICALLY upright in front of the Arc Reactor, perpendicular to the laser beam.
            // LookRotation aligns local +Z with 'forward' and local +Y with 'up'.
            // Because a Unity Quad lies in the local XY plane, setting local +Z to 'forward'
            // guarantees that the quad is strictly perpendicular to the laser beam.
            collimatorRings[i].transform.rotation = Quaternion.LookRotation(forward, up);

            float scale = Mathf.Lerp(0.008f, 0.032f + i * 0.008f, progress);
            collimatorRings[i].transform.localScale = Vector3.one * scale;
        }
    }

    public void FireUnibeamLaser(Vector3 startPoint, Vector3 direction, float distance = 0.55f)
    {
        twoPointBuffer[0] = startPoint;
        twoPointBuffer[1] = startPoint + direction.normalized * distance;

        if (unibeamCore != null)
        {
            unibeamCore.SetPositions(twoPointBuffer);
            unibeamCore.enabled = true;
        }
        if (unibeamMantle != null)
        {
            unibeamMantle.SetPositions(twoPointBuffer);
            unibeamMantle.enabled = true;
        }
        if (groundHeatGlowRenderer != null)
        {
            groundHeatGlowRenderer.enabled = true;
            Vector3 groundDir = new Vector3(direction.x, 0f, direction.z).normalized;
            Vector3 groundCenter = startPoint + groundDir * (distance * 0.45f);
            groundHeatGlowRenderer.transform.position = new Vector3(groundCenter.x, 0.0005f, groundCenter.z);
            groundHeatGlowRenderer.transform.rotation = Quaternion.LookRotation(groundDir, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
        }

        // Keep the collimator rings active and fully expanded around the unibeam core during discharge
        SetCollimatorProgress(1.0f);
    }

    public void ExtinguishUnibeamLaser()
    {
        if (unibeamCore != null) unibeamCore.enabled = false;
        if (unibeamMantle != null) unibeamMantle.enabled = false;
        if (groundHeatGlowRenderer != null) groundHeatGlowRenderer.enabled = false;

        SetCollimatorProgress(0f);
    }

    public void ResetAllVFX()
    {
        ExtpoolAll();
    }

    private void ExtpoolAll()
    {
        ExtinguishRepulsorBeam();
        ExtinguishUnibeamLaser();
        SetEmissiveOverdrive(1.0f);
    }

    #endregion
}
