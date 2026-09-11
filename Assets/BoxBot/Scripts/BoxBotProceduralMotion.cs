using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mathematical 2nd-Order Spring Dynamics & Whole-Body Procedural Kinematics Engine for BoxBot.
/// Manages procedural poses, harmonic armor jitter, recoil shockwaves, and whole-body secondary motion
/// across all 378 suit micro-primitives while preserving exact ground adherence (Y = 0.0000m invariance).
/// </summary>
public class BoxBotProceduralMotion : MonoBehaviour
{
    #region Mathematical Physics Helpers

    /// <summary>
    /// Unconditionally stable 2nd-Order Dynamic System with dynamic pole clamping (Keijiro / Freya Holmér formulation).
    /// Continuous ODE: y'' + 2*zeta*w*y' + w^2*y = w^2*x + 2*zeta*w*r*x'
    /// </summary>
    [System.Serializable]
    public struct SecondOrderDynamics
    {
        private float xp;     // Previous target position
        private float y, yd;  // Current position and velocity
        private float k1, k2, k3;

        public float Value => y;
        public float Velocity => yd;

        public SecondOrderDynamics(float f, float zeta, float r, float x0)
        {
            float omega = 2f * Mathf.PI * Mathf.Max(0.01f, f);
            k1 = zeta / (Mathf.PI * Mathf.Max(0.01f, f));
            k2 = 1f / (omega * omega);
            k3 = (r * zeta) / omega;
            xp = x0;
            y = x0;
            yd = 0f;
        }

        public void RecomputeCoefficients(float f, float zeta, float r)
        {
            float omega = 2f * Mathf.PI * Mathf.Max(0.01f, f);
            k1 = zeta / (Mathf.PI * Mathf.Max(0.01f, f));
            k2 = 1f / (omega * omega);
            k3 = (r * zeta) / omega;
        }

        public void Reset(float x0, float v0 = 0f)
        {
            xp = x0;
            y = x0;
            yd = v0;
        }

        public float Update(float dt, float x)
        {
            if (dt <= 0.00001f) return y;

            float xd = (x - xp) / dt;
            xp = x;

            // Dynamic pole clamping to guarantee unconditional stability even on low mobile AR frame rates
            float k2Stable = Mathf.Max(k2, 0.5f * dt * dt + 0.5f * dt * k1, dt * k1);

            // Symplectic (Semi-Implicit) Euler Integration
            yd += dt * (x + k3 * xd - y - k1 * yd) / k2Stable;
            y += dt * yd;

            return y;
        }

        public void AddImpulse(float impulseVelocity)
        {
            yd += impulseVelocity;
        }
    }

    /// <summary>
    /// Multi-Axis 3D Second-Order Dynamical System.
    /// </summary>
    public struct SecondOrderDynamics3D
    {
        public SecondOrderDynamics X;
        public SecondOrderDynamics Y;
        public SecondOrderDynamics Z;

        public Vector3 Value => new Vector3(X.Value, Y.Value, Z.Value);

        public SecondOrderDynamics3D(float f, float zeta, float r, Vector3 initialVal)
        {
            X = new SecondOrderDynamics(f, zeta, r, initialVal.x);
            Y = new SecondOrderDynamics(f, zeta, r, initialVal.y);
            Z = new SecondOrderDynamics(f, zeta, r, initialVal.z);
        }

        public Vector3 Update(float dt, Vector3 target)
        {
            return new Vector3(X.Update(dt, target.x), Y.Update(dt, target.y), Z.Update(dt, target.z));
        }

        public void Reset(Vector3 initialVal)
        {
            X.Reset(initialVal.x);
            Y.Reset(initialVal.y);
            Z.Reset(initialVal.z);
        }

        public void AddImpulse(Vector3 impulse)
        {
            X.AddImpulse(impulse.x);
            Y.AddImpulse(impulse.y);
            Z.AddImpulse(impulse.z);
        }
    }

    /// <summary>
    /// Multi-harmonic inharmonic vibration synthesizer simulating structural armor resonance.
    /// Uses golden-ratio frequency harmonics: 34Hz, 55Hz, 89Hz with pre-fire vacuum tension dip at progress ~ 0.92.
    /// </summary>
    public class MechanicalArmorJitter
    {
        private float phase1, phase2, phase3;

        public Vector3 TranslationJitter { get; private set; }
        public Vector3 RotationJitter { get; private set; }

        public void Update(float dt, float chargeProgress, bool isSustaining, float intensity = 1f)
        {
            // Golden-ratio Fibonacci harmonic frequencies (34Hz, 55Hz, 89Hz)
            float freqScale = isSustaining ? 1.0f : Mathf.Lerp(0.75f, 1.0f, chargeProgress);
            float w1 = 2f * Mathf.PI * 34f * freqScale;
            float w2 = 2f * Mathf.PI * 55f * freqScale;
            float w3 = 2f * Mathf.PI * 89f * freqScale;

            phase1 = (phase1 + w1 * dt) % (2f * Mathf.PI);
            phase2 = (phase2 + w2 * dt) % (2f * Mathf.PI);
            phase3 = (phase3 + w3 * dt) % (2f * Mathf.PI);

            // Pre-fire vacuum tension dip at progress ~ 0.92:
            // The suit's micro-actuators and plates lock into extreme pre-blast tension (~12% remaining jitter).
            float vacuumDip = 1f;
            if (!isSustaining && chargeProgress > 0.70f && chargeProgress < 0.99f)
            {
                float dipDist = (chargeProgress - 0.92f) / 0.045f;
                float dipGaussian = Mathf.Exp(-dipDist * dipDist);
                vacuumDip = 1f - 0.88f * dipGaussian;
            }

            float baseEnvelope = isSustaining ? 1.0f : Mathf.Pow(chargeProgress, 2.0f);
            float envelope = baseEnvelope * vacuumDip;

            float amp1 = 0.50f * envelope * intensity;
            float amp2 = 0.35f * envelope * intensity;
            float amp3 = 0.20f * envelope * intensity;
            float noiseAmp = 0.15f * envelope * intensity;

            float noiseSampleX = Mathf.PerlinNoise(Time.time * 85f, 0.1f) * 2f - 1f;
            float noiseSampleY = Mathf.PerlinNoise(Time.time * 92f, 3.7f) * 2f - 1f;
            float noiseSampleZ = Mathf.PerlinNoise(Time.time * 78f, 7.3f) * 2f - 1f;

            float jX = amp1 * Mathf.Sin(phase1) + amp2 * Mathf.Sin(phase2 + 1.047f) + amp3 * Mathf.Cos(phase3 + 2.356f) + noiseAmp * noiseSampleX;
            float jY = amp1 * Mathf.Sin(phase1 + 1.57f) + amp2 * Mathf.Cos(phase2) + amp3 * Mathf.Sin(phase3 + 0.785f) + noiseAmp * noiseSampleY;
            float jZ = amp1 * Mathf.Cos(phase1) + amp2 * Mathf.Sin(phase2 + 2.094f) + amp3 * Mathf.Cos(phase3) + noiseAmp * noiseSampleZ;

            // Physical scaling: 0.15mm translational micro-vibrations, 0.35 degrees rotational flutter
            TranslationJitter = new Vector3(jX * 0.00012f, jY * 0.00022f, jZ * 0.00015f);
            RotationJitter = new Vector3(jX * 0.35f, jY * 0.20f, jZ * 0.28f);
        }

        public void Reset()
        {
            phase1 = 0f;
            phase2 = 0f;
            phase3 = 0f;
            TranslationJitter = Vector3.zero;
            RotationJitter = Vector3.zero;
        }
    }

    /// <summary>
    /// Piecewise C1-continuous asymmetrical recoil impulse generator.
    /// Fast sin^2 ballistic attack (16ms) followed by dual-stage critically damped exponential decay
    /// (gamma1=28, gamma2=12, w1=0.70, w2=0.30).
    /// </summary>
    public class ProceduralRecoilImpulse
    {
        private float tAttack;
        private float gamma1;
        private float gamma2;
        private float w1;
        private float w2;
        private float elapsedTime;
        private bool isActive;

        public float CurrentValue { get; private set; }
        public bool IsActive => isActive;

        public ProceduralRecoilImpulse(float attackTime = 0.016f, float g1 = 28f, float g2 = 12f, float weight1 = 0.70f, float weight2 = 0.30f)
        {
            tAttack = attackTime;
            gamma1 = g1;
            gamma2 = g2;
            w1 = weight1;
            w2 = weight2;
            elapsedTime = 999f;
            isActive = false;
            CurrentValue = 0f;
        }

        public ProceduralRecoilImpulse(float attackTime, float decayGamma)
            : this(attackTime, decayGamma, decayGamma, 1.0f, 0.0f)
        {
        }

        public void Trigger()
        {
            elapsedTime = 0f;
            isActive = true;
        }

        public float Update(float dt)
        {
            if (!isActive)
            {
                CurrentValue = 0f;
                return 0f;
            }

            elapsedTime += dt;

            if (elapsedTime <= tAttack)
            {
                // Ballistic sin^2 attack: zero derivative at t=0 and t=tAttack
                float p = (Mathf.PI * elapsedTime) / (2f * tAttack);
                float s = Mathf.Sin(p);
                CurrentValue = s * s;
            }
            else
            {
                // Dual-stage critically damped exponential decay
                float tau = elapsedTime - tAttack;
                float stage1 = (1f + gamma1 * tau) * Mathf.Exp(-gamma1 * tau);
                float stage2 = (1f + gamma2 * tau) * Mathf.Exp(-gamma2 * tau);
                CurrentValue = w1 * stage1 + w2 * stage2;

                if (CurrentValue < 0.001f && tau > 0.35f)
                {
                    CurrentValue = 0f;
                    isActive = false;
                }
            }

            return CurrentValue;
        }

        public void Reset()
        {
            elapsedTime = 999f;
            isActive = false;
            CurrentValue = 0f;
        }
    }

    #endregion

    [Header("=== Rig Primary Transform References ===")]
    [SerializeField] private Transform headTransform;
    [SerializeField] private Transform torsoTransform;
    [SerializeField] private Transform leftArmPivot;
    [SerializeField] private Transform rightArmPivot;
    [SerializeField] private Transform pelvisTransform;
    [SerializeField] private Transform leftLegTransform;
    [SerializeField] private Transform rightLegTransform;

    [Header("=== Articulated Arm Kinematic Sub-Joints ===")]
    [SerializeField] private Transform leftElbowPivot;
    [SerializeField] private Transform rightElbowPivot;
    [SerializeField] private Transform leftWristPivot;
    [SerializeField] private Transform rightWristPivot;
    [SerializeField] private Transform leftRepulsorIrisHub;
    [SerializeField] private Transform rightRepulsorIrisHub;

    [Header("=== Whole-Body Micro-Mechanical Sub-Parts (Auto-Discovered) ===")]
    [SerializeField] private Transform leftAirBrakeShell;
    [SerializeField] private Transform rightAirBrakeShell;
    [SerializeField] private Transform chinExhaustCowl;
    [SerializeField] private Transform sternalPlate;
    [SerializeField] private Transform cervicalSpine;
    [SerializeField] private Transform leftIntercostalRib;
    [SerializeField] private Transform rightIntercostalRib;
    [SerializeField] private Transform leftAbPiston;
    [SerializeField] private Transform rightAbPiston;
    [SerializeField] private Transform leftAchillesRod;
    [SerializeField] private Transform rightAchillesRod;

    // Resting transforms (Cached in local parent space)
    private Vector3 headRestPos;
    private Quaternion headRestRot;
    private Vector3 torsoRestPos;
    private Quaternion torsoRestRot;
    private Vector3 leftArmRestPos;
    private Quaternion leftArmRestRot;
    private Vector3 rightArmRestPos;
    private Quaternion rightArmRestRot;
    private Vector3 pelvisRestPos;
    private Quaternion pelvisRestRot;
    private Vector3 leftLegRestPos;
    private Quaternion leftLegRestRot;
    private Vector3 rightLegRestPos;
    private Quaternion rightLegRestRot;

    // Sub-Joint resting transforms
    private Vector3 leftElbowRestPos, rightElbowRestPos;
    private Quaternion leftElbowRestRot, rightElbowRestRot;
    private Vector3 leftWristRestPos, rightWristRestPos;
    private Quaternion leftWristRestRot, rightWristRestRot;
    private Vector3 leftRepulsorIrisHubRestPos, rightRepulsorIrisHubRestPos;
    private Quaternion leftRepulsorIrisHubRestRot, rightRepulsorIrisHubRestRot;

    // Secondary micro-mechanics resting transforms
    private Quaternion leftAirBrakeRestRot = Quaternion.identity;
    private Quaternion rightAirBrakeRestRot = Quaternion.identity;
    private Vector3 chinExhaustRestPos;
    private Quaternion chinExhaustRestRot = Quaternion.identity;
    private Vector3 sternalRestPos;
    private Quaternion sternalRestRot = Quaternion.identity;
    private Vector3 cervicalSpineRestPos;
    private Quaternion cervicalSpineRestRot = Quaternion.identity;
    private Quaternion leftIntercostalRestRot = Quaternion.identity;
    private Quaternion rightIntercostalRestRot = Quaternion.identity;
    private Vector3 leftAbPistonRestPos, rightAbPistonRestPos;
    private Vector3 leftAchillesRestPos, rightAchillesRestPos;

    // Iris blade cache for radial dilation
    private struct IrisBladeData
    {
        public Transform transform;
        public Vector3 restLocalPos;
        public Vector3 radialDirection;
    }

    private List<IrisBladeData> leftIrisBlades = new List<IrisBladeData>();
    private List<IrisBladeData> rightIrisBlades = new List<IrisBladeData>();

    // Numerical physics instances
    private SecondOrderDynamics3D leftShoulderSpring;
    private SecondOrderDynamics3D rightShoulderSpring;
    private SecondOrderDynamics3D leftElbowSpring;
    private SecondOrderDynamics3D rightElbowSpring;
    private SecondOrderDynamics3D leftWristSpring;
    private SecondOrderDynamics3D rightWristSpring;
    private SecondOrderDynamics irisApertureSpring;
    private SecondOrderDynamics3D torsoSpring;
    private SecondOrderDynamics pelvisDropSpring;
    private SecondOrderDynamics3D headSpring;

    private MechanicalArmorJitter armorJitter = new MechanicalArmorJitter();
    private ProceduralRecoilImpulse recoilImpulse = new ProceduralRecoilImpulse(0.016f, 28f, 12f, 0.70f, 0.30f);
    private float sustainedFiringTimer = 0f;

    private bool isInitialized = false;

    private void Awake()
    {
        InitializeRig();
    }

    public void InitializeRig()
    {
        if (isInitialized) return;

        // Auto-discover root parts if not assigned
        Transform root = transform;
        if (headTransform == null) headTransform = root.Find("Head");
        if (torsoTransform == null) torsoTransform = root.Find("Torso");
        if (leftArmPivot == null) leftArmPivot = root.Find("LeftArmPivot");
        if (rightArmPivot == null) rightArmPivot = root.Find("RightArmPivot");
        if (pelvisTransform == null) pelvisTransform = root.Find("Pelvis");
        if (leftLegTransform == null) leftLegTransform = root.Find("LeftLeg");
        if (rightLegTransform == null) rightLegTransform = root.Find("RightLeg");

        // Sub-Joint Discovery & Fallback Auto-Wiring
        if (leftArmPivot != null)
        {
            SetupArmSubJointHierarchy(leftArmPivot, ref leftElbowPivot, ref leftWristPivot, ref leftRepulsorIrisHub, isLeft: true);
        }
        if (rightArmPivot != null)
        {
            SetupArmSubJointHierarchy(rightArmPivot, ref rightElbowPivot, ref rightWristPivot, ref rightRepulsorIrisHub, isLeft: false);
        }

        // Secondary Micro-Mechanics Auto-Discovery
        if (headTransform != null)
        {
            if (cervicalSpine == null) cervicalSpine = headTransform.Find("Cervical_C1_Atlas_Rib") ?? headTransform.Find("Cervical_Spine_Vertebra_Prominens");
            if (chinExhaustCowl == null) chinExhaustCowl = headTransform.Find("Chin_Exhaust_Cowl_Frame");
        }

        if (torsoTransform != null)
        {
            if (leftAirBrakeShell == null) leftAirBrakeShell = torsoTransform.Find("AirBrake_ScissorLink_Upper_L");
            if (rightAirBrakeShell == null) rightAirBrakeShell = torsoTransform.Find("AirBrake_ScissorLink_Upper_R");
            if (sternalPlate == null) sternalPlate = torsoTransform.Find("ReactorCore") ?? torsoTransform.Find("Sternal_Stud_Mid_L");
            if (leftIntercostalRib == null) leftIntercostalRib = torsoTransform.Find("Harness_Intercostal_Upper_L");
            if (rightIntercostalRib == null) rightIntercostalRib = torsoTransform.Find("Harness_Intercostal_Upper_R");
            if (leftAbPiston == null) leftAbPiston = torsoTransform.Find("Ab_Actuator_PistonRam_L");
            if (rightAbPiston == null) rightAbPiston = torsoTransform.Find("Ab_Actuator_PistonRam_R");
        }

        if (leftLegTransform != null && leftAchillesRod == null)
            leftAchillesRod = leftLegTransform.Find("Ankle_Achilles_Rod");
        if (rightLegTransform != null && rightAchillesRod == null)
            rightAchillesRod = rightLegTransform.Find("Ankle_Achilles_Rod");

        // Cache Resting Positions & Rotations
        if (headTransform != null) { headRestPos = headTransform.localPosition; headRestRot = headTransform.localRotation; }
        if (torsoTransform != null) { torsoRestPos = torsoTransform.localPosition; torsoRestRot = torsoTransform.localRotation; }
        if (leftArmPivot != null) { leftArmRestPos = leftArmPivot.localPosition; leftArmRestRot = leftArmPivot.localRotation; }
        if (rightArmPivot != null) { rightArmRestPos = rightArmPivot.localPosition; rightArmRestRot = rightArmPivot.localRotation; }
        if (pelvisTransform != null) { pelvisRestPos = pelvisTransform.localPosition; pelvisRestRot = pelvisTransform.localRotation; }
        if (leftLegTransform != null) { leftLegRestPos = leftLegTransform.localPosition; leftLegRestRot = leftLegTransform.localRotation; }
        if (rightLegTransform != null) { rightLegRestPos = rightLegTransform.localPosition; rightLegRestRot = rightLegTransform.localRotation; }

        if (leftElbowPivot != null) { leftElbowRestPos = leftElbowPivot.localPosition; leftElbowRestRot = leftElbowPivot.localRotation; }
        if (rightElbowPivot != null) { rightElbowRestPos = rightElbowPivot.localPosition; rightElbowRestRot = rightElbowPivot.localRotation; }
        if (leftWristPivot != null) { leftWristRestPos = leftWristPivot.localPosition; leftWristRestRot = leftWristPivot.localRotation; }
        if (rightWristPivot != null) { rightWristRestPos = rightWristPivot.localPosition; rightWristRestRot = rightWristPivot.localRotation; }
        if (leftRepulsorIrisHub != null) { leftRepulsorIrisHubRestPos = leftRepulsorIrisHub.localPosition; leftRepulsorIrisHubRestRot = leftRepulsorIrisHub.localRotation; }
        if (rightRepulsorIrisHub != null) { rightRepulsorIrisHubRestPos = rightRepulsorIrisHub.localPosition; rightRepulsorIrisHubRestRot = rightRepulsorIrisHub.localRotation; }

        if (leftAirBrakeShell != null) leftAirBrakeRestRot = leftAirBrakeShell.localRotation;
        if (rightAirBrakeShell != null) rightAirBrakeRestRot = rightAirBrakeShell.localRotation;
        if (chinExhaustCowl != null) { chinExhaustRestPos = chinExhaustCowl.localPosition; chinExhaustRestRot = chinExhaustCowl.localRotation; }
        if (sternalPlate != null) { sternalRestPos = sternalPlate.localPosition; sternalRestRot = sternalPlate.localRotation; }
        if (cervicalSpine != null) { cervicalSpineRestPos = cervicalSpine.localPosition; cervicalSpineRestRot = cervicalSpine.localRotation; }
        if (leftIntercostalRib != null) leftIntercostalRestRot = leftIntercostalRib.localRotation;
        if (rightIntercostalRib != null) rightIntercostalRestRot = rightIntercostalRib.localRotation;
        if (leftAbPiston != null) leftAbPistonRestPos = leftAbPiston.localPosition;
        if (rightAbPiston != null) rightAbPistonRestPos = rightAbPiston.localPosition;
        if (leftAchillesRod != null) leftAchillesRestPos = leftAchillesRod.localPosition;
        if (rightAchillesRod != null) rightAchillesRestPos = rightAchillesRod.localPosition;

        // Cache Iris Blade Primitives for radial expansion
        CacheIrisBlades(leftRepulsorIrisHub, leftIrisBlades);
        CacheIrisBlades(rightRepulsorIrisHub, rightIrisBlades);

        // Setup 2nd-Order Dynamic Systems with Dynamic Pole Clamping
        leftShoulderSpring = new SecondOrderDynamics3D(5.6f, 0.78f, 0.85f, Vector3.zero);
        rightShoulderSpring = new SecondOrderDynamics3D(5.6f, 0.78f, 0.85f, Vector3.zero);
        leftElbowSpring = new SecondOrderDynamics3D(8.5f, 0.82f, 0.75f, Vector3.zero);
        rightElbowSpring = new SecondOrderDynamics3D(8.5f, 0.82f, 0.75f, Vector3.zero);
        leftWristSpring = new SecondOrderDynamics3D(11.2f, 0.75f, 0.90f, Vector3.zero);
        rightWristSpring = new SecondOrderDynamics3D(11.2f, 0.75f, 0.90f, Vector3.zero);
        irisApertureSpring = new SecondOrderDynamics(14.0f, 0.88f, 1.20f, 0f);
        torsoSpring = new SecondOrderDynamics3D(3.6f, 0.70f, 0.60f, Vector3.zero);
        pelvisDropSpring = new SecondOrderDynamics(2.8f, 0.94f, 0.15f, 0f);
        headSpring = new SecondOrderDynamics3D(5.2f, 0.76f, 1.00f, Vector3.zero);

        isInitialized = true;
    }

    private void SetupArmSubJointHierarchy(Transform armPivot, ref Transform elbowPivot, ref Transform wristPivot, ref Transform irisHub, bool isLeft)
    {
        string prefix = isLeft ? "Left" : "Right";

        // 1. Discover or Instantiate Elbow Pivot at (0, -0.0172f, 0)
        if (elbowPivot == null) elbowPivot = armPivot.Find($"{prefix}ElbowPivot") ?? armPivot.Find("ElbowPivot");
        if (elbowPivot == null)
        {
            GameObject elbowGO = new GameObject($"{prefix}ElbowPivot");
            elbowPivot = elbowGO.transform;
            elbowPivot.SetParent(armPivot, false);
            elbowPivot.localPosition = new Vector3(0f, -0.0172f, 0f);
            elbowPivot.localRotation = Quaternion.identity;
            elbowPivot.localScale = Vector3.one;
        }

        // 2. Discover or Instantiate Wrist Pivot at (0, -0.0183f, 0)
        if (wristPivot == null) wristPivot = elbowPivot.Find($"{prefix}WristPivot") ?? elbowPivot.Find("WristPivot") ?? armPivot.Find($"{prefix}WristPivot");
        if (wristPivot == null)
        {
            GameObject wristGO = new GameObject($"{prefix}WristPivot");
            wristPivot = wristGO.transform;
            wristPivot.SetParent(elbowPivot, false);
            wristPivot.localPosition = new Vector3(0f, -0.0183f, 0f);
            wristPivot.localRotation = Quaternion.identity;
            wristPivot.localScale = Vector3.one;
        }
        else if (wristPivot.parent != elbowPivot)
        {
            wristPivot.SetParent(elbowPivot, true);
        }

        // 3. Discover or Instantiate Repulsor Iris Hub at (0, -0.0055f, -0.0031f)
        if (irisHub == null) irisHub = wristPivot.Find($"{prefix}RepulsorIrisHub") ?? wristPivot.Find("RepulsorIrisHub") ?? armPivot.Find($"{prefix}RepulsorIrisHub");
        if (irisHub == null)
        {
            GameObject irisGO = new GameObject($"{prefix}RepulsorIrisHub");
            irisHub = irisGO.transform;
            irisHub.SetParent(wristPivot, false);
            irisHub.localPosition = new Vector3(0f, -0.0055f, -0.0031f);
            irisHub.localRotation = Quaternion.identity;
            irisHub.localScale = Vector3.one;
        }
        else if (irisHub.parent != wristPivot)
        {
            irisHub.SetParent(wristPivot, true);
        }

        // Strip any residual colliders from sub-joint pivots
        StripCollider(elbowPivot);
        StripCollider(wristPivot);
        StripCollider(irisHub);

        // 4. Re-parent existing child primitives based on anatomical names / Y-bounds with worldPositionStays: true
        List<Transform> existingChildren = new List<Transform>();
        foreach (Transform child in armPivot)
        {
            if (child != elbowPivot && child != wristPivot && child != irisHub)
            {
                existingChildren.Add(child);
            }
        }

        foreach (Transform child in existingChildren)
        {
            string cName = child.name;
            float localY = child.localPosition.y;

            // Repulsor Iris Blades & Hub Primitives
            if (cName.Contains("Iris_Blade") || cName.Contains("Repulsor_Iris"))
            {
                child.SetParent(irisHub, true);
            }
            // Wrist & Hand primitives (Wrist screws, Metacarpal rivets, Thumb, RepulsorPalm) or Y < -0.033m
            else if (cName.Contains("Wrist") || cName.Contains("Hand") || cName.Contains("Thumb") || cName.Contains("Palm") || localY < -0.033f)
            {
                child.SetParent(wristPivot, true);
            }
            // Elbow & Forearm primitives (Elbow rings, Forearm missile latches, Gauntlet louvers, Optics) or Y between -0.016m and -0.033m
            else if (cName.Contains("Elbow") || cName.Contains("Missile") || cName.Contains("Targeting") || cName.Contains("Gauntlet") || (localY <= -0.016f && localY >= -0.033f))
            {
                child.SetParent(elbowPivot, true);
            }
            // Otherwise remains under armPivot (Pauldron, Deltoid, Humeral, Bicep)
        }
    }

    private void StripCollider(Transform t)
    {
        if (t == null) return;
        Collider col = t.GetComponent<Collider>();
        if (col != null)
        {
            if (Application.isPlaying) Destroy(col);
            else DestroyImmediate(col);
        }
    }

    private void CacheIrisBlades(Transform hub, List<IrisBladeData> bladeList)
    {
        bladeList.Clear();
        if (hub == null) return;

        foreach (Transform child in hub.GetComponentsInChildren<Transform>(true))
        {
            if (child != hub && child.name.Contains("Blade"))
            {
                Vector3 rPos = child.localPosition;
                Vector3 dir = new Vector3(rPos.x, rPos.y, 0f);
                if (dir.sqrMagnitude > 0.000001f)
                    dir.Normalize();
                else
                    dir = Vector3.up;

                bladeList.Add(new IrisBladeData
                {
                    transform = child,
                    restLocalPos = rPos,
                    radialDirection = dir
                });
            }
        }
    }

    private void ApplyIrisDilation(List<IrisBladeData> blades, float dilation)
    {
        for (int i = 0; i < blades.Count; i++)
        {
            var b = blades[i];
            if (b.transform != null)
            {
                b.transform.localPosition = b.restLocalPos + b.radialDirection * dilation;
            }
        }
    }

    public void TriggerRecoilKick()
    {
        recoilImpulse.Trigger();

        // Add velocity impulses to dynamic springs
        torsoSpring.AddImpulse(new Vector3(5.5f, 0f, -0.04f));
        headSpring.AddImpulse(new Vector3(2.2f, 0f, 0f));
        leftShoulderSpring.AddImpulse(new Vector3(12f, 0f, 0f));
        rightShoulderSpring.AddImpulse(new Vector3(12f, 0f, 0f));
        leftElbowSpring.AddImpulse(new Vector3(18f, 0f, 0f));
        rightElbowSpring.AddImpulse(new Vector3(18f, 0f, 0f));
        leftWristSpring.AddImpulse(new Vector3(15f, 0f, 0f));
        rightWristSpring.AddImpulse(new Vector3(15f, 0f, 0f));
        pelvisDropSpring.AddImpulse(-0.005f);
        irisApertureSpring.AddImpulse(-0.015f);
    }

    /// <summary>
    /// Evaluates the Dual Repulsor Pulse pose:
    /// Shoulder elevation to -85 deg + recoil kick (+12 deg),
    /// Elbow tension flexion (35 deg) snapping to 6.5 deg micro-bend on blast + recoil kick (+18 deg),
    /// Wrist hyperextension cocking to -85 deg pointing palms forward + recoil kick (+15 deg),
    /// Repulsor iris blade dilation (+0.85mm, +12.5 deg rotation) during charge, snapping to -0.30mm collimation on blast,
    /// Cervical spine compression (-0.50mm), targeting pitch dip (-4.0 deg), chin tuck, +2.2 deg blast recoil snap,
    /// Sternal expansion (+0.55mm), intercostal rib expansion (+4.5 deg), abdominal cylinder cycling,
    /// Pelvic crouch (-1.25mm to -1.65mm), hip micro-yaw (+/- 0.8 deg), polycentric knee flex (11.5 deg to 14.0 deg),
    /// Achilles rod compression (-0.92mm), with strict boot sole ground invariance at Y = 0.000000m.
    /// </summary>
    public void EvaluateRepulsorPose(float dt, float chargeProgress, bool isFiring, bool isCooling)
    {
        if (!isInitialized) InitializeRig();

        // 1. Advance dynamics
        armorJitter.Update(dt, chargeProgress, isFiring, 1f);
        float recoil = recoilImpulse.Update(dt);

        // 2. Multi-Joint Arm Kinematics
        // A. Shoulder elevation (-85 deg) + recoil kick (+12 deg)
        float armElevation = Mathf.Lerp(0f, -85f, Mathf.SmoothStep(0f, 1f, chargeProgress));
        Vector3 targetLeftShoulder = new Vector3(armElevation + recoil * 12f, 3.0f, 3.5f);
        Vector3 targetRightShoulder = new Vector3(armElevation + recoil * 12f, -3.0f, -3.5f);

        Vector3 leftShoulderEuler = leftShoulderSpring.Update(dt, targetLeftShoulder);
        Vector3 rightShoulderEuler = rightShoulderSpring.Update(dt, targetRightShoulder);

        if (leftArmPivot != null) leftArmPivot.localRotation = leftArmRestRot * Quaternion.Euler(leftShoulderEuler);
        if (rightArmPivot != null) rightArmPivot.localRotation = rightArmRestRot * Quaternion.Euler(rightShoulderEuler);

        // B. Elbow tension flexion (35 deg) snapping to 6.5 deg micro-bend on blast + recoil kick (+18 deg)
        float targetElbowFlex = isFiring ? 6.5f : Mathf.Lerp(0f, 35f, chargeProgress);
        targetElbowFlex += recoil * 18.0f;

        Vector3 leftElbowEuler = leftElbowSpring.Update(dt, new Vector3(targetElbowFlex, 0f, 0f));
        Vector3 rightElbowEuler = rightElbowSpring.Update(dt, new Vector3(targetElbowFlex, 0f, 0f));

        if (leftElbowPivot != null) leftElbowPivot.localRotation = leftElbowRestRot * Quaternion.Euler(leftElbowEuler);
        if (rightElbowPivot != null) rightElbowPivot.localRotation = rightElbowRestRot * Quaternion.Euler(rightElbowEuler);

        // C. Wrist hyperextension cocking to -85 deg (pointing palms directly forward along Z+) + recoil kick (+15 deg)
        float targetWristPitch = Mathf.Lerp(0f, -85f, chargeProgress) + (recoil * 15.0f);
        Vector3 leftWristEuler = leftWristSpring.Update(dt, new Vector3(targetWristPitch, 0f, 0f));
        Vector3 rightWristEuler = rightWristSpring.Update(dt, new Vector3(targetWristPitch, 0f, 0f));

        if (leftWristPivot != null) leftWristPivot.localRotation = leftWristRestRot * Quaternion.Euler(leftWristEuler);
        if (rightWristPivot != null) rightWristPivot.localRotation = rightWristRestRot * Quaternion.Euler(rightWristEuler);

        // D. Repulsor iris blade dilation (+0.85mm, +12.5 deg rotation) during charge, snapping to -0.30mm collimation on blast
        float targetIrisDilation = isFiring ? -0.00030f : (isCooling ? 0f : Mathf.Lerp(0f, 0.00085f, chargeProgress));
        float currentIrisDilation = irisApertureSpring.Update(dt, targetIrisDilation);
        float irisRotation = (currentIrisDilation / 0.00085f) * 12.5f;

        if (leftRepulsorIrisHub != null) leftRepulsorIrisHub.localRotation = leftRepulsorIrisHubRestRot * Quaternion.Euler(0f, 0f, irisRotation);
        if (rightRepulsorIrisHub != null) rightRepulsorIrisHub.localRotation = rightRepulsorIrisHubRestRot * Quaternion.Euler(0f, 0f, -irisRotation);

        ApplyIrisDilation(leftIrisBlades, currentIrisDilation);
        ApplyIrisDilation(rightIrisBlades, currentIrisDilation);

        // 3. Torso Pitch & Recoil (+5.5 deg backward lean on blast)
        float targetTorsoPitch = (-2.0f * chargeProgress) + (5.5f * recoil);
        Vector3 torsoEuler = torsoSpring.Update(dt, new Vector3(targetTorsoPitch, 0f, 0f));
        if (torsoTransform != null)
        {
            torsoTransform.localRotation = torsoRestRot * Quaternion.Euler(torsoEuler);
            torsoTransform.localPosition = torsoRestPos + armorJitter.TranslationJitter;
        }

        // Sternal expansion (+0.55mm forward surge)
        float sternalSurge = Mathf.Lerp(0f, 0.00055f, chargeProgress);
        if (sternalPlate != null) sternalPlate.localPosition = sternalRestPos + new Vector3(0f, 0f, sternalSurge);

        // Intercostal rib expansion (+4.5 deg roll)
        float ribAngle = Mathf.Lerp(0f, 4.5f, chargeProgress);
        if (leftIntercostalRib != null) leftIntercostalRib.localRotation = leftIntercostalRestRot * Quaternion.Euler(0f, 0f, ribAngle);
        if (rightIntercostalRib != null) rightIntercostalRib.localRotation = rightIntercostalRestRot * Quaternion.Euler(0f, 0f, -ribAngle);

        // Abdominal cylinder cycling
        float abCycle = Mathf.Sin(Time.time * 12.0f) * 0.0004f * chargeProgress;
        if (leftAbPiston != null) leftAbPiston.localPosition = leftAbPistonRestPos + new Vector3(0f, abCycle, 0f);
        if (rightAbPiston != null) rightAbPiston.localPosition = rightAbPistonRestPos + new Vector3(0f, abCycle, 0f);

        // 4. Head Targeting Lock, Cervical Spine Compression (-0.50mm), Chin Tuck & +2.2 deg blast recoil snap
        float targetHeadPitch = Mathf.Lerp(0f, -4.0f, chargeProgress) + (recoil * 2.2f);
        float headCompY = Mathf.Lerp(0f, -0.00050f, chargeProgress);
        Vector3 headEuler = headSpring.Update(dt, new Vector3(targetHeadPitch, 0f, 0f));

        if (headTransform != null)
        {
            headTransform.localRotation = headRestRot * Quaternion.Euler(headEuler) * Quaternion.Euler(armorJitter.RotationJitter * 0.4f);
            headTransform.localPosition = headRestPos + new Vector3(0f, headCompY, 0f) + (armorJitter.TranslationJitter * 0.4f);
        }
        if (cervicalSpine != null) cervicalSpine.localPosition = cervicalSpineRestPos + new Vector3(0f, headCompY * 0.6f, 0f);
        if (chinExhaustCowl != null) chinExhaustCowl.localPosition = chinExhaustRestPos + new Vector3(0f, headCompY * 0.4f, 0f);

        // 5. Pelvis Crouch (-1.25mm to -1.65mm) & Hip Micro-Yaw (+/- 0.8 deg)
        float targetCrouchY = isCooling ? 0f : (Mathf.Lerp(0f, -0.00125f, chargeProgress) - (recoil * 0.00040f));
        float crouchY = pelvisDropSpring.Update(dt, targetCrouchY);
        float hipYaw = Mathf.Sin(chargeProgress * Mathf.PI) * 0.8f;

        if (pelvisTransform != null)
        {
            pelvisTransform.localPosition = pelvisRestPos + new Vector3(0f, crouchY, 0f);
            pelvisTransform.localRotation = pelvisRestRot * Quaternion.Euler(0f, hipYaw, 0f);
        }

        // 6. Polycentric Knee Flex (11.5 deg to 14.0 deg) & Achilles Rod Compression (-0.92mm)
        // Strictly preserves boot sole planar ground invariance at Y = 0.000000m
        float crouchDist = -crouchY;
        float kneeFlex;
        if (crouchDist <= 0.00125f)
            kneeFlex = Mathf.Clamp01(crouchDist / 0.00125f) * 11.5f;
        else
            kneeFlex = 11.5f + Mathf.Clamp01((crouchDist - 0.00125f) / 0.00040f) * 2.5f;

        if (leftLegTransform != null) leftLegTransform.localRotation = leftLegRestRot * Quaternion.Euler(kneeFlex, 0f, 0f);
        if (rightLegTransform != null) rightLegTransform.localRotation = rightLegRestRot * Quaternion.Euler(kneeFlex, 0f, 0f);

        float crouchRatio = Mathf.Clamp01(crouchDist / 0.00165f);
        float achillesComp = crouchRatio * 0.00092f;
        if (leftAchillesRod != null) leftAchillesRod.localPosition = leftAchillesRestPos + new Vector3(0f, -achillesComp, 0f);
        if (rightAchillesRod != null) rightAchillesRod.localPosition = rightAchillesRestPos + new Vector3(0f, -achillesComp, 0f);
    }

    /// <summary>
    /// Evaluates the Chest Arc Reactor Unibeam Laser pose with Bilateral Flank Power Brace:
    /// Shoulder retraction (+24 deg), abduction (+10.5 deg), convergence (-3.5 deg L / +3.5 deg R),
    /// Anterior elbow flexion (-96.5 deg) strictly forward along +Z,
    /// Wrist semi-pronated hammer power grip (-25 deg pitch, -60 deg roll, +6 deg yaw L / -6 deg yaw R),
    /// Distal fist clench (+20 deg), iris aperture shielding (-0.55mm),
    /// Torso forward surge (+3.5mm) & sustained firing shudder/recoil, torso pitch (-6.0 deg),
    /// Dorsal air-brakes open (+18.0 deg / +26.0 deg cooling),
    /// Head targeting lock forward, cervical compression (-0.50mm),
    /// Sternal expansion (+0.55mm), intercostal rib expansion (+4.5 deg), abdominal cylinder cycling,
    /// Pelvic crouch (-1.25mm to -1.65mm), hip micro-yaw (+/- 0.8 deg), polycentric knee flex (11.5 deg to 14.0 deg),
    /// Achilles rod compression (-0.92mm), with strict boot sole ground invariance at Y = 0.000000m.
    /// </summary>
    public void EvaluateUnibeamPose(float dt, float chargeProgress, bool isFiring, bool isCooling)
    {
        if (!isInitialized) InitializeRig();

        // 1. Advance dynamics, recoil, and compute sustained firing timer
        armorJitter.Update(dt, chargeProgress, isFiring, 1.25f);
        float recoil = recoilImpulse.Update(dt);
        float armProg = Mathf.SmoothStep(0f, 1f, chargeProgress);

        if (isFiring) sustainedFiringTimer += dt;
        else sustainedFiringTimer = 0f;
        float sustainedThrust = isFiring ? 1.0f : 0f;
        float pulse18 = isFiring ? Mathf.Sin(sustainedFiringTimer * 2f * Mathf.PI * 18.5f) : 0f;
        float flutter44 = isFiring ? Mathf.Cos(sustainedFiringTimer * 2f * Mathf.PI * 44.0f + 1.2f) : 0f;
        float hydraulicShudder = pulse18 * 0.18f + flutter44 * 0.08f;

        // 2. Torso surge and pitch
        float surgeZ = isCooling ? Mathf.Lerp(0.0008f, 0f, 1f - chargeProgress) : Mathf.Lerp(0f, 0.0035f, armProg);
        if (isFiring) surgeZ -= (recoil * 0.0017f) + (hydraulicShudder * 0.00015f);
        float targetTorsoPitch;
        if (isCooling) targetTorsoPitch = Mathf.Lerp(-2.0f, -7.0f, Mathf.Exp(-chargeProgress * 6f));
        else targetTorsoPitch = Mathf.Lerp(0f, -6.0f, chargeProgress) + (recoil * 8.5f) + (sustainedThrust * 4.5f) + (pulse18 * 0.8f);

        Vector3 torsoEuler = torsoSpring.Update(dt, new Vector3(targetTorsoPitch, 0f, 0f));

        if (torsoTransform != null)
        {
            torsoTransform.localPosition = torsoRestPos + new Vector3(0f, 0f, surgeZ) + armorJitter.TranslationJitter;
            torsoTransform.localRotation = torsoRestRot * Quaternion.Euler(torsoEuler);
        }

        // Sternal expansion (+0.55mm forward surge)
        float sternalSurge = Mathf.Lerp(0f, 0.00055f, armProg);
        if (sternalPlate != null) sternalPlate.localPosition = sternalRestPos + new Vector3(0f, 0f, sternalSurge);

        // Intercostal rib expansion (+4.5 deg roll)
        float ribAngle = Mathf.Lerp(0f, 4.5f, chargeProgress);
        if (leftIntercostalRib != null) leftIntercostalRib.localRotation = leftIntercostalRestRot * Quaternion.Euler(0f, 0f, ribAngle);
        if (rightIntercostalRib != null) rightIntercostalRib.localRotation = rightIntercostalRestRot * Quaternion.Euler(0f, 0f, -ribAngle);

        // Abdominal cylinder cycling
        float abCycle = Mathf.Sin(Time.time * 12.0f) * 0.0004f * chargeProgress;
        if (leftAbPiston != null) leftAbPiston.localPosition = leftAbPistonRestPos + new Vector3(0f, abCycle, 0f);
        if (rightAbPiston != null) rightAbPiston.localPosition = rightAbPistonRestPos + new Vector3(0f, abCycle, 0f);

        // 3. Bilateral Flank Power Brace with True Shoulder Adduction
        // - Pitch: Anterior brace flexion (-16.0 deg), swinging humerus forward into anterior plane
        // - Yaw: Medial convergence (Left +14.0 deg, Right -14.0 deg), turning arms inward toward Arc Reactor
        // - Roll: Coronal adduction clamp (Left +10.0 deg, Right -10.0 deg), clamping upper arms against pectoral armor
        float shoulderPitchTarget = Mathf.Lerp(0f, -16.0f, armProg) + (recoil * 10.0f) + (sustainedThrust * 2.0f) + (pulse18 * 1.5f);
        float shoulderYawTarget = Mathf.Lerp(0f, 14.0f, armProg) - (recoil * 2.5f) + (pulse18 * 0.8f);
        float shoulderRollTarget = Mathf.Lerp(0f, 10.0f, armProg) + (recoil * 2.0f);

        if (isCooling)
        {
            shoulderPitchTarget = Mathf.Lerp(0f, -5.0f, chargeProgress);
            shoulderYawTarget = Mathf.Lerp(0f, 4.0f, chargeProgress);
            shoulderRollTarget = Mathf.Lerp(0f, 3.0f, chargeProgress);
        }

        Vector3 targetLeftShoulder = new Vector3(shoulderPitchTarget, shoulderYawTarget, shoulderRollTarget);
        Vector3 targetRightShoulder = new Vector3(shoulderPitchTarget, -shoulderYawTarget, -shoulderRollTarget);

        Vector3 leftShoulderEuler = leftShoulderSpring.Update(dt, targetLeftShoulder);
        Vector3 rightShoulderEuler = rightShoulderSpring.Update(dt, targetRightShoulder);

        if (leftArmPivot != null) leftArmPivot.localRotation = leftArmRestRot * Quaternion.Euler(leftShoulderEuler);
        if (rightArmPivot != null) rightArmPivot.localRotation = rightArmRestRot * Quaternion.Euler(rightShoulderEuler);

        // 4. Anterior Elbow Flexion (-82.0 deg)
        // With shoulder adducted forward (-16 deg pitch) and inward (+14 deg yaw), -82.0 deg flexion
        // projects forearms forward flanking the Arc Reactor collimators
        float elbowFlexTarget = Mathf.Lerp(0f, -82.0f, armProg) - (recoil * 10.0f) - (sustainedThrust * 3.0f) + (pulse18 * 2.0f);
        if (isCooling) elbowFlexTarget = Mathf.Lerp(0f, -25.0f, chargeProgress);

        Vector3 leftElbowEuler = leftElbowSpring.Update(dt, new Vector3(elbowFlexTarget, 0f, 0f));
        Vector3 rightElbowEuler = rightElbowSpring.Update(dt, new Vector3(elbowFlexTarget, 0f, 0f));

        if (leftElbowPivot != null) leftElbowPivot.localRotation = leftElbowRestRot * Quaternion.Euler(leftElbowEuler);
        if (rightElbowPivot != null) rightElbowPivot.localRotation = rightElbowRestRot * Quaternion.Euler(rightElbowEuler);

        // 5. Wrist inward framing & semi-pronated hammer grip
        float wristPitchTarget = Mathf.Lerp(0f, -15.0f, armProg) - (recoil * 8.0f) - (sustainedThrust * 1.5f) + (flutter44 * 1.0f);
        float wristYawTarget = Mathf.Lerp(0f, 8.0f, armProg);
        float wristRollTarget = Mathf.Lerp(0f, -45.0f, armProg);
        if (isCooling)
        {
            wristPitchTarget = Mathf.Lerp(0f, -5.0f, chargeProgress);
            wristYawTarget = Mathf.Lerp(0f, 2.0f, chargeProgress);
            wristRollTarget = Mathf.Lerp(0f, -15.0f, chargeProgress);
        }

        Vector3 leftWristEuler = leftWristSpring.Update(dt, new Vector3(wristPitchTarget, wristYawTarget, wristRollTarget));
        Vector3 rightWristEuler = rightWristSpring.Update(dt, new Vector3(wristPitchTarget, -wristYawTarget, -wristRollTarget));

        if (leftWristPivot != null) leftWristPivot.localRotation = leftWristRestRot * Quaternion.Euler(leftWristEuler);
        if (rightWristPivot != null) rightWristPivot.localRotation = rightWristRestRot * Quaternion.Euler(rightWristEuler);

        // 6. Distal fist clench
        float distalClench = isCooling ? Mathf.Lerp(0f, 5.0f, chargeProgress) : Mathf.Lerp(0f, 20.0f, armProg);
        if (leftRepulsorIrisHub != null) leftRepulsorIrisHub.localRotation = leftRepulsorIrisHubRestRot * Quaternion.Euler(distalClench, 0f, 0f);
        if (rightRepulsorIrisHub != null) rightRepulsorIrisHub.localRotation = rightRepulsorIrisHubRestRot * Quaternion.Euler(distalClench, 0f, 0f);

        // 7. Iris aperture shielding
        float targetIrisDilation = isCooling ? Mathf.Lerp(0f, -0.00010f, chargeProgress) : Mathf.Lerp(0f, -0.00055f, armProg);
        float currentIrisDilation = irisApertureSpring.Update(dt, targetIrisDilation);
        ApplyIrisDilation(leftIrisBlades, currentIrisDilation);
        ApplyIrisDilation(rightIrisBlades, currentIrisDilation);

        // 8. Dorsal air-brakes
        float flapPitch = isCooling ? Mathf.Lerp(0f, 26.0f, chargeProgress) : (Mathf.Lerp(0f, 18.0f, chargeProgress) + (pulse18 * 1.5f));
        if (leftAirBrakeShell != null) leftAirBrakeShell.localRotation = leftAirBrakeRestRot * Quaternion.Euler(flapPitch, 0f, 0f);
        if (rightAirBrakeShell != null) rightAirBrakeShell.localRotation = rightAirBrakeRestRot * Quaternion.Euler(flapPitch, 0f, 0f);

        // 9. Head and cervical spine
        float targetHeadPitch = isCooling ? Mathf.Lerp(-1.5f, -5.5f, Mathf.Exp(-chargeProgress * 5f)) : (Mathf.Lerp(0f, -4.0f, chargeProgress) + (recoil * 2.2f));
        float headCompY = Mathf.Lerp(0f, -0.00050f, chargeProgress);
        Vector3 headEuler = headSpring.Update(dt, new Vector3(targetHeadPitch, 0f, 0f));
        if (headTransform != null) {
            headTransform.localRotation = headRestRot * Quaternion.Euler(headEuler) * Quaternion.Euler(armorJitter.RotationJitter * 0.5f);
            headTransform.localPosition = headRestPos + new Vector3(0f, headCompY, 0f) + (armorJitter.TranslationJitter * 0.5f);
        }
        if (cervicalSpine != null) cervicalSpine.localPosition = cervicalSpineRestPos + new Vector3(0f, headCompY * 0.6f, 0f);
        if (chinExhaustCowl != null) chinExhaustCowl.localPosition = chinExhaustRestPos + new Vector3(0f, headCompY * 0.4f, 0f);

        // 10. Pelvis and knees preserving exact Y = 0.000000m
        float targetCrouchY = isCooling ? Mathf.Lerp(0f, -0.00060f, chargeProgress) : (Mathf.Lerp(0f, -0.00125f, chargeProgress) - (recoil * 0.00040f) - (sustainedThrust * 0.00020f));
        float crouchY = pelvisDropSpring.Update(dt, targetCrouchY);
        float hipYaw = Mathf.Sin(chargeProgress * Mathf.PI) * 0.8f;
        if (pelvisTransform != null) {
            pelvisTransform.localPosition = pelvisRestPos + new Vector3(0f, crouchY, 0f);
            pelvisTransform.localRotation = pelvisRestRot * Quaternion.Euler(0f, hipYaw, 0f);
        }
        float crouchDist = -crouchY;
        float kneeFlex = (crouchDist <= 0.00125f) ? Mathf.Clamp01(crouchDist / 0.00125f) * 11.5f : 11.5f + Mathf.Clamp01((crouchDist - 0.00125f) / 0.00040f) * 2.5f;
        if (leftLegTransform != null) leftLegTransform.localRotation = leftLegRestRot * Quaternion.Euler(kneeFlex, 0f, 0f);
        if (rightLegTransform != null) rightLegTransform.localRotation = rightLegRestRot * Quaternion.Euler(kneeFlex, 0f, 0f);
        float crouchRatio = Mathf.Clamp01(crouchDist / 0.00165f);
        float achillesComp = crouchRatio * 0.00092f;
        if (leftAchillesRod != null) leftAchillesRod.localPosition = leftAchillesRestPos + new Vector3(0f, -achillesComp, 0f);
        if (rightAchillesRod != null) rightAchillesRod.localPosition = rightAchillesRestPos + new Vector3(0f, -achillesComp, 0f);
    }

    /// <summary>
    /// Smoothly steps all dynamic springs towards their rest pose (State 5 Recovery / Reset).
    /// </summary>
    public bool SettleToRest(float dt)
    {
        if (!isInitialized) return true;

        Vector3 tEuler = torsoSpring.Update(dt, Vector3.zero);
        Vector3 lShoulderEuler = leftShoulderSpring.Update(dt, Vector3.zero);
        Vector3 rShoulderEuler = rightShoulderSpring.Update(dt, Vector3.zero);
        Vector3 lElbowEuler = leftElbowSpring.Update(dt, Vector3.zero);
        Vector3 rElbowEuler = rightElbowSpring.Update(dt, Vector3.zero);
        Vector3 lWristEuler = leftWristSpring.Update(dt, Vector3.zero);
        Vector3 rWristEuler = rightWristSpring.Update(dt, Vector3.zero);
        Vector3 hEuler = headSpring.Update(dt, Vector3.zero);
        float irisDilation = irisApertureSpring.Update(dt, 0f);
        float crouchY = pelvisDropSpring.Update(dt, 0f);

        if (torsoTransform != null)
        {
            torsoTransform.localRotation = torsoRestRot * Quaternion.Euler(tEuler);
            torsoTransform.localPosition = Vector3.Lerp(torsoTransform.localPosition, torsoRestPos, dt * 10f);
        }
        if (leftArmPivot != null) leftArmPivot.localRotation = leftArmRestRot * Quaternion.Euler(lShoulderEuler);
        if (rightArmPivot != null) rightArmPivot.localRotation = rightArmRestRot * Quaternion.Euler(rShoulderEuler);
        if (leftElbowPivot != null) leftElbowPivot.localRotation = leftElbowRestRot * Quaternion.Euler(lElbowEuler);
        if (rightElbowPivot != null) rightElbowPivot.localRotation = rightElbowRestRot * Quaternion.Euler(rElbowEuler);
        if (leftWristPivot != null) leftWristPivot.localRotation = leftWristRestRot * Quaternion.Euler(lWristEuler);
        if (rightWristPivot != null) rightWristPivot.localRotation = rightWristRestRot * Quaternion.Euler(rWristEuler);

        if (leftRepulsorIrisHub != null) leftRepulsorIrisHub.localRotation = Quaternion.Slerp(leftRepulsorIrisHub.localRotation, leftRepulsorIrisHubRestRot, dt * 10f);
        if (rightRepulsorIrisHub != null) rightRepulsorIrisHub.localRotation = Quaternion.Slerp(rightRepulsorIrisHub.localRotation, rightRepulsorIrisHubRestRot, dt * 10f);
        ApplyIrisDilation(leftIrisBlades, irisDilation);
        ApplyIrisDilation(rightIrisBlades, irisDilation);

        if (headTransform != null)
        {
            headTransform.localRotation = headRestRot * Quaternion.Euler(hEuler);
            headTransform.localPosition = Vector3.Lerp(headTransform.localPosition, headRestPos, dt * 10f);
        }
        if (pelvisTransform != null)
        {
            pelvisTransform.localPosition = pelvisRestPos + new Vector3(0f, crouchY, 0f);
            pelvisTransform.localRotation = Quaternion.Slerp(pelvisTransform.localRotation, pelvisRestRot, dt * 10f);
        }

        float crouchDist = -crouchY;
        float kneeFlex;
        if (crouchDist <= 0.00125f)
            kneeFlex = Mathf.Clamp01(crouchDist / 0.00125f) * 11.5f;
        else
            kneeFlex = 11.5f + Mathf.Clamp01((crouchDist - 0.00125f) / 0.00040f) * 2.5f;

        if (leftLegTransform != null) leftLegTransform.localRotation = leftLegRestRot * Quaternion.Euler(kneeFlex, 0f, 0f);
        if (rightLegTransform != null) rightLegTransform.localRotation = rightLegRestRot * Quaternion.Euler(kneeFlex, 0f, 0f);

        if (leftAirBrakeShell != null) leftAirBrakeShell.localRotation = Quaternion.Slerp(leftAirBrakeShell.localRotation, leftAirBrakeRestRot, dt * 10f);
        if (rightAirBrakeShell != null) rightAirBrakeShell.localRotation = Quaternion.Slerp(rightAirBrakeShell.localRotation, rightAirBrakeRestRot, dt * 10f);
        if (chinExhaustCowl != null) chinExhaustCowl.localPosition = Vector3.Lerp(chinExhaustCowl.localPosition, chinExhaustRestPos, dt * 10f);
        if (sternalPlate != null) sternalPlate.localPosition = Vector3.Lerp(sternalPlate.localPosition, sternalRestPos, dt * 10f);
        if (cervicalSpine != null) cervicalSpine.localPosition = Vector3.Lerp(cervicalSpine.localPosition, cervicalSpineRestPos, dt * 10f);
        if (leftIntercostalRib != null) leftIntercostalRib.localRotation = Quaternion.Slerp(leftIntercostalRib.localRotation, leftIntercostalRestRot, dt * 10f);
        if (rightIntercostalRib != null) rightIntercostalRib.localRotation = Quaternion.Slerp(rightIntercostalRib.localRotation, rightIntercostalRestRot, dt * 10f);
        if (leftAbPiston != null) leftAbPiston.localPosition = Vector3.Lerp(leftAbPiston.localPosition, leftAbPistonRestPos, dt * 10f);
        if (rightAbPiston != null) rightAbPiston.localPosition = Vector3.Lerp(rightAbPiston.localPosition, rightAbPistonRestPos, dt * 10f);
        if (leftAchillesRod != null) leftAchillesRod.localPosition = Vector3.Lerp(leftAchillesRod.localPosition, leftAchillesRestPos, dt * 10f);
        if (rightAchillesRod != null) rightAchillesRod.localPosition = Vector3.Lerp(rightAchillesRod.localPosition, rightAchillesRestPos, dt * 10f);

        // Settling tolerance check (< 0.05 deg and < 0.01mm)
        bool settled = tEuler.sqrMagnitude < 0.0025f &&
                       lShoulderEuler.sqrMagnitude < 0.0025f &&
                       rShoulderEuler.sqrMagnitude < 0.0025f &&
                       lElbowEuler.sqrMagnitude < 0.0025f &&
                       rElbowEuler.sqrMagnitude < 0.0025f &&
                       lWristEuler.sqrMagnitude < 0.0025f &&
                       rWristEuler.sqrMagnitude < 0.0025f &&
                       hEuler.sqrMagnitude < 0.0025f &&
                       Mathf.Abs(crouchY) < 0.00002f &&
                       Mathf.Abs(irisDilation) < 0.00002f;

        if (settled)
        {
            ForceResetToRest();
        }
        return settled;
    }

    /// <summary>
    /// Instantly forces all bones and sub-joints to strictly stored rest positions and rotations.
    /// </summary>
    public void ForceResetToRest()
    {
        if (!isInitialized) return;

        if (headTransform != null) { headTransform.localPosition = headRestPos; headTransform.localRotation = headRestRot; }
        if (torsoTransform != null) { torsoTransform.localPosition = torsoRestPos; torsoTransform.localRotation = torsoRestRot; }
        if (leftArmPivot != null) { leftArmPivot.localPosition = leftArmRestPos; leftArmPivot.localRotation = leftArmRestRot; }
        if (rightArmPivot != null) { rightArmPivot.localPosition = rightArmRestPos; rightArmPivot.localRotation = rightArmRestRot; }
        if (pelvisTransform != null) { pelvisTransform.localPosition = pelvisRestPos; pelvisTransform.localRotation = pelvisRestRot; }
        if (leftLegTransform != null) { leftLegTransform.localPosition = leftLegRestPos; leftLegTransform.localRotation = leftLegRestRot; }
        if (rightLegTransform != null) { rightLegTransform.localPosition = rightLegRestPos; rightLegTransform.localRotation = rightLegRestRot; }

        if (leftElbowPivot != null) { leftElbowPivot.localPosition = leftElbowRestPos; leftElbowPivot.localRotation = leftElbowRestRot; }
        if (rightElbowPivot != null) { rightElbowPivot.localPosition = rightElbowRestPos; rightElbowPivot.localRotation = rightElbowRestRot; }
        if (leftWristPivot != null) { leftWristPivot.localPosition = leftWristRestPos; leftWristPivot.localRotation = leftWristRestRot; }
        if (rightWristPivot != null) { rightWristPivot.localPosition = rightWristRestPos; rightWristPivot.localRotation = rightWristRestRot; }
        if (leftRepulsorIrisHub != null) { leftRepulsorIrisHub.localPosition = leftRepulsorIrisHubRestPos; leftRepulsorIrisHub.localRotation = leftRepulsorIrisHubRestRot; }
        if (rightRepulsorIrisHub != null) { rightRepulsorIrisHub.localPosition = rightRepulsorIrisHubRestPos; rightRepulsorIrisHub.localRotation = rightRepulsorIrisHubRestRot; }

        ApplyIrisDilation(leftIrisBlades, 0f);
        ApplyIrisDilation(rightIrisBlades, 0f);

        if (leftAirBrakeShell != null) leftAirBrakeShell.localRotation = leftAirBrakeRestRot;
        if (rightAirBrakeShell != null) rightAirBrakeShell.localRotation = rightAirBrakeRestRot;
        if (chinExhaustCowl != null) { chinExhaustCowl.localPosition = chinExhaustRestPos; chinExhaustCowl.localRotation = chinExhaustRestRot; }
        if (sternalPlate != null) { sternalPlate.localPosition = sternalRestPos; sternalPlate.localRotation = sternalRestRot; }
        if (cervicalSpine != null) { cervicalSpine.localPosition = cervicalSpineRestPos; cervicalSpine.localRotation = cervicalSpineRestRot; }
        if (leftIntercostalRib != null) leftIntercostalRib.localRotation = leftIntercostalRestRot;
        if (rightIntercostalRib != null) rightIntercostalRib.localRotation = rightIntercostalRestRot;
        if (leftAbPiston != null) leftAbPiston.localPosition = leftAbPistonRestPos;
        if (rightAbPiston != null) rightAbPiston.localPosition = rightAbPistonRestPos;
        if (leftAchillesRod != null) leftAchillesRod.localPosition = leftAchillesRestPos;
        if (rightAchillesRod != null) rightAchillesRod.localPosition = rightAchillesRestPos;

        leftShoulderSpring.Reset(Vector3.zero);
        rightShoulderSpring.Reset(Vector3.zero);
        leftElbowSpring.Reset(Vector3.zero);
        rightElbowSpring.Reset(Vector3.zero);
        leftWristSpring.Reset(Vector3.zero);
        rightWristSpring.Reset(Vector3.zero);
        irisApertureSpring.Reset(0f);
        torsoSpring.Reset(Vector3.zero);
        pelvisDropSpring.Reset(0f);
        headSpring.Reset(Vector3.zero);

        armorJitter.Reset();
        recoilImpulse.Reset();
        sustainedFiringTimer = 0f;
    }
}
