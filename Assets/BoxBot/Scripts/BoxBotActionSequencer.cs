using System;
using UnityEngine;

/// <summary>
/// Deterministic 5-Phase Hierarchical Finite State Machine for BoxBot Action Mode.
/// Features:
/// 1. Dual Action Modes: RepulsorPulse (Dual Palm Blast) and UnibeamLaser (Chest Core Beam).
/// 2. Automatic mode alternation on consecutive Action button triggers.
/// 3. Zero-Allocation frame-driven Update() tick (no coroutine garbage).
/// 4. Failsafe interruption recovery (tracking loss / app backgrounding).
/// 5. Coordinated integration with Procedural Motion, VFX Controller, and Audio Synthesizer.
/// </summary>
public class BoxBotActionSequencer : MonoBehaviour
{
    public enum ActionMode
    {
        RepulsorPulse = 0,
        UnibeamLaser = 1
    }

    public enum ActionPhase
    {
        Idle = 0,
        AcquisitionStance = 1,
        EnergyCharge = 2,
        DischargeFiring = 3,
        RecoilVenting = 4,
        DampedRecovery = 5
    }

    [Header("=== Subsystem References ===")]
    [SerializeField] private BoxBotProceduralMotion proceduralMotion;
    [SerializeField] private BoxBotVFXController vfxController;
    [SerializeField] private BoxBotAudioSynthesizer audioSynthesizer;

    [Header("=== Action Mode Configuration ===")]
    [SerializeField] private ActionMode currentMode = ActionMode.RepulsorPulse;
    [SerializeField] private bool alternateModesOnTrigger = true;
    [SerializeField] private float buttonCooldown = 0.5f;

    // State Tracking
    public ActionPhase CurrentPhase { get; private set; } = ActionPhase.Idle;
    public ActionMode CurrentMode => currentMode;
    public bool IsActionActive => CurrentPhase != ActionPhase.Idle;

    public event Action<ActionPhase> OnPhaseChanged;
    public event Action<ActionMode> OnModeChanged;
    public event Action OnActionCompleted;

    // Phase Durations (Tuned for cinematic weight)
    private const float DURATION_PREP = 0.28f;
    private const float DURATION_CHARGE_REPULSOR = 0.55f;
    private const float DURATION_CHARGE_UNIBEAM = 0.85f;
    private const float DURATION_BLAST_REPULSOR = 0.12f;
    private const float DURATION_BLAST_UNIBEAM = 1.45f;
    private const float DURATION_RECOIL = 0.35f;
    private const float DURATION_RECOVERY = 0.38f;

    private float phaseTimer = 0f;
    private float lastActionEndTime = -1f;

    private void Awake()
    {
        if (proceduralMotion == null) proceduralMotion = GetComponent<BoxBotProceduralMotion>() ?? gameObject.AddComponent<BoxBotProceduralMotion>();
        if (vfxController == null) vfxController = GetComponent<BoxBotVFXController>() ?? gameObject.AddComponent<BoxBotVFXController>();
        if (audioSynthesizer == null) audioSynthesizer = GetComponent<BoxBotAudioSynthesizer>() ?? gameObject.AddComponent<BoxBotAudioSynthesizer>();
    }

    public void SetActionMode(ActionMode mode)
    {
        currentMode = mode;
        OnModeChanged?.Invoke(currentMode);
    }

    /// <summary>
    /// Triggers the Action Mode sequence with re-entrancy protection and cooldown.
    /// </summary>
    public bool TriggerAction()
    {
        if (IsActionActive) return false;
        if (Time.time - lastActionEndTime < buttonCooldown) return false;

        TransitionToPhase(ActionPhase.AcquisitionStance);
        return true;
    }

    private void Update()
    {
        if (CurrentPhase == ActionPhase.Idle) return;

        float dt = Time.deltaTime;
        phaseTimer += dt;

        switch (CurrentPhase)
        {
            case ActionPhase.AcquisitionStance:
                EvaluateAcquisitionPhase(dt);
                break;

            case ActionPhase.EnergyCharge:
                EvaluateChargePhase(dt);
                break;

            case ActionPhase.DischargeFiring:
                EvaluateDischargePhase(dt);
                break;

            case ActionPhase.RecoilVenting:
                EvaluateRecoilPhase(dt);
                break;

            case ActionPhase.DampedRecovery:
                EvaluateRecoveryPhase(dt);
                break;
        }
    }

    #region Phase Evaluators

    private void EvaluateAcquisitionPhase(float dt)
    {
        float progress = Mathf.Clamp01(phaseTimer / DURATION_PREP);

        if (currentMode == ActionMode.RepulsorPulse)
        {
            if (proceduralMotion != null) proceduralMotion.EvaluateRepulsorPose(dt, progress * 0.4f, isFiring: false, isCooling: false);
        }
        else
        {
            if (proceduralMotion != null) proceduralMotion.EvaluateUnibeamPose(dt, progress * 0.4f, isFiring: false, isCooling: false);
        }

        if (vfxController != null) vfxController.SetEmissiveOverdrive(Mathf.Lerp(1.0f, 2.2f, progress));

        if (phaseTimer >= DURATION_PREP)
        {
            TransitionToPhase(ActionPhase.EnergyCharge);
        }
    }

    private void EvaluateChargePhase(float dt)
    {
        float duration = currentMode == ActionMode.RepulsorPulse ? DURATION_CHARGE_REPULSOR : DURATION_CHARGE_UNIBEAM;
        float progress = Mathf.Clamp01(phaseTimer / duration);

        if (currentMode == ActionMode.RepulsorPulse)
        {
            if (proceduralMotion != null) proceduralMotion.EvaluateRepulsorPose(dt, Mathf.Lerp(0.4f, 1.0f, progress), isFiring: false, isCooling: false);
            if (vfxController != null)
            {
                vfxController.SetRepulsorMuzzleCharge(progress, true);
                vfxController.SetRepulsorMuzzleCharge(progress, false);
            }
        }
        else
        {
            if (proceduralMotion != null) proceduralMotion.EvaluateUnibeamPose(dt, Mathf.Lerp(0.4f, 1.0f, progress), isFiring: false, isCooling: false);
            if (vfxController != null) vfxController.SetCollimatorProgress(progress);
        }

        // Exponential emission overdrive up to 8.0x HDR with pre-blast flutter
        float expRamp = Mathf.Pow(progress, 2.5f);
        float flutter = progress > 0.8f ? 1f + 0.15f * Mathf.Sin(Time.time * 65f) : 1f;
        if (vfxController != null) vfxController.SetEmissiveOverdrive(Mathf.Lerp(2.2f, 8.0f, expRamp) * flutter);

        if (phaseTimer >= duration)
        {
            TransitionToPhase(ActionPhase.DischargeFiring);
        }
    }

    private void EvaluateDischargePhase(float dt)
    {
        float duration = currentMode == ActionMode.RepulsorPulse ? DURATION_BLAST_REPULSOR : DURATION_BLAST_UNIBEAM;
        float progress = Mathf.Clamp01(phaseTimer / duration);

        if (currentMode == ActionMode.RepulsorPulse)
        {
            if (proceduralMotion != null) proceduralMotion.EvaluateRepulsorPose(dt, 1.0f, isFiring: true, isCooling: false);

            // Fire dual palm beams forward from dynamic repulsor origins
            Vector3 leftOrigin = vfxController != null ? vfxController.LeftRepulsorOrigin : transform.TransformPoint(new Vector3(-0.029f, 0.056f, 0.041f));
            Vector3 rightOrigin = vfxController != null ? vfxController.RightRepulsorOrigin : transform.TransformPoint(new Vector3(0.029f, 0.056f, 0.041f));
            Vector3 fwd = vfxController != null ? vfxController.ChestAimForward : transform.forward;

            if (vfxController != null)
            {
                vfxController.FireRepulsorBeam(leftOrigin, fwd, 0.45f);
                vfxController.FireRepulsorBeam(rightOrigin, fwd, 0.45f);
            }
        }
        else
        {
            if (proceduralMotion != null) proceduralMotion.EvaluateUnibeamPose(dt, 1.0f, isFiring: true, isCooling: false);

            Vector3 coreOrigin = vfxController != null ? vfxController.ArcReactorOrigin : transform.TransformPoint(new Vector3(0f, 0.054f, 0.015f));
            Vector3 fwd = vfxController != null ? vfxController.ChestAimForward : transform.forward;

            if (vfxController != null)
            {
                vfxController.FireUnibeamLaser(coreOrigin, fwd, 0.60f);
            }
        }

        // Overdrive flash at 18x HDR
        if (vfxController != null)
        {
            vfxController.SetEmissiveOverdrive(18.0f);
        }

        if (phaseTimer >= duration)
        {
            TransitionToPhase(ActionPhase.RecoilVenting);
        }
    }

    private void EvaluateRecoilPhase(float dt)
    {
        float progress = Mathf.Clamp01(phaseTimer / DURATION_RECOIL);

        if (currentMode == ActionMode.RepulsorPulse)
        {
            if (proceduralMotion != null) proceduralMotion.EvaluateRepulsorPose(dt, Mathf.Lerp(1.0f, 0.2f, progress), isFiring: false, isCooling: true);
        }
        else
        {
            if (proceduralMotion != null) proceduralMotion.EvaluateUnibeamPose(dt, progress, isFiring: false, isCooling: true);
        }

        // Dual-phase emission cooling
        float decay = Mathf.Exp(-progress * 4.5f);
        if (vfxController != null) vfxController.SetEmissiveOverdrive(Mathf.Lerp(1.0f, 6.0f, decay));

        if (phaseTimer >= DURATION_RECOIL)
        {
            TransitionToPhase(ActionPhase.DampedRecovery);
        }
    }

    private void EvaluateRecoveryPhase(float dt)
    {
        bool settled = proceduralMotion != null && proceduralMotion.SettleToRest(dt);
        if (vfxController != null) vfxController.SetEmissiveOverdrive(1.0f);

        if (phaseTimer >= DURATION_RECOVERY && settled)
        {
            CompleteActionSequence();
        }
    }

    #endregion

    private void TransitionToPhase(ActionPhase nextPhase)
    {
        CurrentPhase = nextPhase;
        phaseTimer = 0f;

        switch (nextPhase)
        {
            case ActionPhase.AcquisitionStance:
                if (audioSynthesizer != null) audioSynthesizer.PlayServoGlide();
                break;

            case ActionPhase.EnergyCharge:
                if (audioSynthesizer != null) audioSynthesizer.PlayChargeWhine();
                break;

            case ActionPhase.DischargeFiring:
                if (proceduralMotion != null) proceduralMotion.TriggerRecoilKick();
                if (currentMode == ActionMode.RepulsorPulse)
                {
                    if (audioSynthesizer != null) audioSynthesizer.PlayRepulsorBlast();
                }
                else
                {
                    if (audioSynthesizer != null) audioSynthesizer.StartUnibeamLoop();
                }
                break;

            case ActionPhase.RecoilVenting:
                if (currentMode == ActionMode.RepulsorPulse)
                {
                    if (vfxController != null) vfxController.ExtinguishRepulsorBeam();
                }
                else
                {
                    if (vfxController != null) vfxController.ExtinguishUnibeamLaser();
                    if (audioSynthesizer != null) audioSynthesizer.StopUnibeamLoop();
                }
                if (audioSynthesizer != null) audioSynthesizer.PlaySteamVent();
                break;
        }

        OnPhaseChanged?.Invoke(CurrentPhase);
    }

    private void CompleteActionSequence()
    {
        CurrentPhase = ActionPhase.Idle;
        phaseTimer = 0f;
        lastActionEndTime = Time.time;

        if (proceduralMotion != null) proceduralMotion.ForceResetToRest();
        if (vfxController != null) vfxController.ResetAllVFX();
        if (audioSynthesizer != null) audioSynthesizer.StopAllAudio();

        // Alternate modes if enabled
        if (alternateModesOnTrigger)
        {
            currentMode = currentMode == ActionMode.RepulsorPulse ? ActionMode.UnibeamLaser : ActionMode.RepulsorPulse;
            OnModeChanged?.Invoke(currentMode);
        }

        OnPhaseChanged?.Invoke(CurrentPhase);
        OnActionCompleted?.Invoke();
    }

    /// <summary>
    /// Failsafe cancellation called immediately if Vuforia tracking is lost or app is paused.
    /// Guarantees clean reset of all articulated sub-joints, line renderers, and audio.
    /// </summary>
    public void AbortActionImmediate()
    {
        bool wasActive = CurrentPhase != ActionPhase.Idle;
        CurrentPhase = ActionPhase.Idle;
        phaseTimer = 0f;
        lastActionEndTime = Time.time;

        if (proceduralMotion != null) proceduralMotion.ForceResetToRest();
        if (vfxController != null) vfxController.ResetAllVFX();
        if (audioSynthesizer != null) audioSynthesizer.StopAllAudio();

        if (wasActive)
        {
            OnPhaseChanged?.Invoke(CurrentPhase);
        }
    }

    private void OnApplicationPause(bool pause)
    {
        if (pause) AbortActionImmediate();
    }
}
