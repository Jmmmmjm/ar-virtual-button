using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Vuforia;

/// <summary>
/// BoxBot Interactive AR Controller
/// Robust Physical Finger Touch with:
/// 1. Correct Top-Down Screen to Camera-Space mapping (fixed inverted Y).
/// 2. Directional occlusion (detects finger darkness/shadow drop, ignores global camera flicker).
/// 3. Debounce filter (requires consistent detection across consecutive frames).
/// 4. Live camera swatch previews in the Diagnostic HUD so you can see what the camera sees under each button.
/// </summary>
public class BoxBotController : MonoBehaviour
{
    [Header("=== Robot Part References ===")]
    [SerializeField] private Renderer leftEyeRenderer;
    [SerializeField] private Renderer rightEyeRenderer;
    [SerializeField] private Renderer visorRenderer;
    [SerializeField] private Renderer reactorCoreRenderer;
    [SerializeField] private Renderer leftRepulsorRenderer;
    [SerializeField] private Renderer rightRepulsorRenderer;
    [SerializeField] private Renderer leftSoleRepulsorRenderer;
    [SerializeField] private Renderer rightSoleRepulsorRenderer;
    [SerializeField] private Transform leftArmPivot;
    [SerializeField] private Transform rightArmPivot;

    [Header("=== Action Mode Subsystems ===")]
    [SerializeField] private BoxBotActionSequencer actionSequencer;
    [SerializeField] private BoxBotVFXController vfxController;

    [Header("=== Mood Materials (Cycle Order) ===")]
    [SerializeField] private Material[] moodMaterials;

    [Header("=== Sound Effects ===")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip[] robotSounds;

    [Header("=== Button Roots (Transforms) ===")]
    [SerializeField] private Transform moodButtonTransform;
    [SerializeField] private Transform actionButtonTransform;
    [SerializeField] private Transform soundButtonTransform;

    [Header("=== Button Cap Renderers (Indicators) ===")]
    [SerializeField] private Renderer moodIndicator;
    [SerializeField] private Renderer actionIndicator;
    [SerializeField] private Renderer soundIndicator;

    [Header("=== Physical Finger Occlusion Settings ===")]
    [Tooltip("Enable optical occlusion detection so physical fingers touching the card trigger buttons")]
    [SerializeField] private bool enablePhysicalFingerTouch = true;
    
    [Tooltip("Luminance drop required to trigger a button press (recommended 25-45)")]
    [SerializeField] private float occlusionThreshold = 28f;

    [Header("=== Settings ===")]
    [SerializeField] private float waveDuration = 1.2f;
    [SerializeField] private float waveAngle = 60f;
    [SerializeField] private float buttonCooldown = 0.5f;

    [Header("=== Diagnostics ===")]
    [Tooltip("Enable OnGUI Diagnostic HUD overlay in Editor or Development Builds")]
    [SerializeField] private bool showDiagnosticsHUD = true;

    // Internal state
    private int currentMoodIndex = 0;
    private bool isWaving = false;
    private float lastButtonPressTime = -1f;
    private Camera mainCamera;
    private ObserverBehaviour observerBehaviour;

    // Occlusion tracking & diagnostics
    private float moodBaseline = -1f;
    private float actionBaseline = -1f;
    private float soundBaseline = -1f;
    private float moodCurrentLum = 0f;
    private float actionCurrentLum = 0f;
    private float soundCurrentLum = 0f;
    private float moodDrop = 0f;
    private float actionDrop = 0f;
    private float soundDrop = 0f;

    // Consecutive frame debounce counters
    private int moodTriggerFrames = 0;
    private int actionTriggerFrames = 0;
    private int soundTriggerFrames = 0;

    private Vector2 moodImgCoord;
    private Vector2 actionImgCoord;
    private Vector2 soundImgCoord;

    private Vector3 moodScreenPos;
    private Vector3 actionScreenPos;
    private Vector3 soundScreenPos;

    private bool moodOccluded = false;
    private bool actionOccluded = false;
    private bool soundOccluded = false;

    private bool formatRegistered = false;
    private PixelFormat pixelFormat = PixelFormat.GRAYSCALE;
    private string lastDebugMessage = "Ready";
    private int cameraImageWidth = 0;
    private int cameraImageHeight = 0;

    private void Awake()
    {
        observerBehaviour = GetComponent<ObserverBehaviour>();
        if (actionSequencer == null) actionSequencer = GetComponentInChildren<BoxBotActionSequencer>();
        if (actionSequencer == null)
        {
            Transform boxBot = transform.Find("BoxBot_Root") ?? transform;
            actionSequencer = boxBot.GetComponent<BoxBotActionSequencer>() ?? boxBot.gameObject.AddComponent<BoxBotActionSequencer>();
        }
        if (vfxController == null) vfxController = GetComponentInChildren<BoxBotVFXController>();
        if (vfxController == null)
        {
            Transform boxBot = transform.Find("BoxBot_Root") ?? transform;
            vfxController = boxBot.GetComponent<BoxBotVFXController>() ?? boxBot.gameObject.AddComponent<BoxBotVFXController>();
        }
    }

    private void Start()
    {
        mainCamera = Camera.main;

        if (moodMaterials != null && moodMaterials.Length > 0)
        {
            ApplyMoodMaterial(moodMaterials[0]);
        }

        VuforiaApplication.Instance.OnVuforiaStarted += OnVuforiaStarted;

        if (observerBehaviour != null)
        {
            observerBehaviour.OnTargetStatusChanged += OnTargetStatusChanged;
        }
    }

    private void OnDestroy()
    {
        VuforiaApplication.Instance.OnVuforiaStarted -= OnVuforiaStarted;

        if (observerBehaviour != null)
        {
            observerBehaviour.OnTargetStatusChanged -= OnTargetStatusChanged;
        }
    }

    private void OnTargetStatusChanged(ObserverBehaviour behaviour, TargetStatus targetStatus)
    {
        if (targetStatus.Status == Status.NO_POSE || targetStatus.Status == Status.LIMITED)
        {
            if (actionSequencer != null)
            {
                actionSequencer.AbortActionImmediate();
            }
        }
    }

    private void OnVuforiaStarted()
    {
        RegisterCameraFormat();
    }

    private void RegisterCameraFormat()
    {
        if (VuforiaBehaviour.Instance != null && VuforiaBehaviour.Instance.CameraDevice != null)
        {
            bool success = VuforiaBehaviour.Instance.CameraDevice.SetFrameFormat(pixelFormat, true);
            formatRegistered = success;
            lastDebugMessage = $"Camera format {pixelFormat} registered: {success}";
            Debug.Log($"[BoxBot] {lastDebugMessage}");
        }
    }

    private void Update()
    {
        // 1. Mouse / Touchscreen tap fallback
        HandleScreenInput();

        // 2. Physical Finger Occlusion detection
        if (enablePhysicalFingerTouch)
        {
            HandlePhysicalFingerOcclusion();
        }
    }

    #region Screen Input (Mouse / Touch)
    private void HandleScreenInput()
    {
        bool inputDetected = false;
        Vector2 inputPosition = Vector2.zero;

        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
        {
            inputDetected = true;
            inputPosition = Touchscreen.current.primaryTouch.position.ReadValue();
        }
        else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            inputDetected = true;
            inputPosition = Mouse.current.position.ReadValue();
        }

        if (!inputDetected || mainCamera == null) return;
        if (Time.time - lastButtonPressTime < buttonCooldown) return;

        Ray ray = mainCamera.ScreenPointToRay(inputPosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f))
        {
            string hitName = hit.collider.gameObject.name;

            if (hitName.Contains("Mood"))
            {
                TriggerMoodButton();
            }
            else if (hitName.Contains("Action"))
            {
                TriggerActionButton();
            }
            else if (hitName.Contains("Sound"))
            {
                TriggerSoundButton();
            }
        }
    }
    #endregion

    #region Physical Finger Occlusion (Real-world Fingers)
    private void HandlePhysicalFingerOcclusion()
    {
        if (mainCamera == null) return;

        bool tracked = IsTargetTracked();
        if (!tracked)
        {
            moodBaseline = -1f;
            actionBaseline = -1f;
            soundBaseline = -1f;
            moodTriggerFrames = 0;
            actionTriggerFrames = 0;
            soundTriggerFrames = 0;
            return;
        }

        if (!formatRegistered)
        {
            RegisterCameraFormat();
            if (!formatRegistered) return;
        }

        Vuforia.Image image = VuforiaBehaviour.Instance.CameraDevice.GetCameraImage(pixelFormat);
        if (image == null || image.Pixels == null || image.Pixels.Length == 0)
        {
            lastDebugMessage = "Camera image is null or empty.";
            return;
        }

        cameraImageWidth = image.Width;
        cameraImageHeight = image.Height;

        // Process each button
        ProcessButtonOcclusion(moodButtonTransform, ref moodBaseline, ref moodCurrentLum, ref moodDrop, ref moodTriggerFrames, ref moodImgCoord, ref moodScreenPos, ref moodOccluded, TriggerMoodButton);
        ProcessButtonOcclusion(actionButtonTransform, ref actionBaseline, ref actionCurrentLum, ref actionDrop, ref actionTriggerFrames, ref actionImgCoord, ref actionScreenPos, ref actionOccluded, TriggerActionButton);
        ProcessButtonOcclusion(soundButtonTransform, ref soundBaseline, ref soundCurrentLum, ref soundDrop, ref soundTriggerFrames, ref soundImgCoord, ref soundScreenPos, ref soundOccluded, TriggerSoundButton);
    }

    private void ProcessButtonOcclusion(
        Transform btnTransform, 
        ref float baseline, 
        ref float currentLum, 
        ref float drop, 
        ref int triggerFrames,
        ref Vector2 outImgCoord,
        ref Vector3 outScreenPos,
        ref bool isOccluded, 
        System.Action onPress)
    {
        if (btnTransform == null) return;

        outScreenPos = mainCamera.WorldToScreenPoint(btnTransform.position);
        if (outScreenPos.z <= 0 || 
            outScreenPos.x < 0 || outScreenPos.x > Screen.width || 
            outScreenPos.y < 0 || outScreenPos.y > Screen.height)
        {
            return;
        }

        Vuforia.Image image = VuforiaBehaviour.Instance.CameraDevice.GetCameraImage(pixelFormat);
        if (image == null) return;

        int imgW = image.Width;
        int imgH = image.Height;

        // CRITICAL FIX: Vuforia's ConvertScreenToImageSpace expects screen coordinates
        // with origin at TOP-LEFT (standard GUI space), whereas Unity's WorldToScreenPoint has origin at BOTTOM-LEFT.
        float topDownY = Screen.height - outScreenPos.y;
        Vector2 vuforiaCoord = VuforiaRuntimeUtilities.ConvertScreenToImageSpace(new Vector2(outScreenPos.x, topDownY));

        int cx, cy;
        // Verify converted coordinates
        if (vuforiaCoord.x > 1.0f && vuforiaCoord.y > 1.0f && vuforiaCoord.x < imgW && vuforiaCoord.y < imgH)
        {
            cx = (int)vuforiaCoord.x;
            cy = (int)vuforiaCoord.y;
            outImgCoord = vuforiaCoord;
        }
        else if (vuforiaCoord.x >= 0f && vuforiaCoord.x <= 1.0f && vuforiaCoord.y >= 0f && vuforiaCoord.y <= 1.0f)
        {
            // Normalized: scale up to image dimensions
            cx = Mathf.Clamp((int)(vuforiaCoord.x * imgW), 0, imgW - 1);
            cy = Mathf.Clamp((int)(vuforiaCoord.y * imgH), 0, imgH - 1);
            outImgCoord = new Vector2(cx, cy);
        }
        else
        {
            // Direct Viewport Mapping: Viewport (0,0) is bottom-left, Camera Image (0,0) is top-left
            Vector3 vp = mainCamera.WorldToViewportPoint(btnTransform.position);
            cx = Mathf.Clamp((int)(vp.x * imgW), 0, imgW - 1);
            cy = Mathf.Clamp((int)((1f - vp.y) * imgH), 0, imgH - 1);
            outImgCoord = new Vector2(cx, cy);
        }

        currentLum = SampleLuminance(image, cx, cy, 7);

        // Initialize baseline
        if (baseline < 0f)
        {
            baseline = currentLum;
            return;
        }

        // A finger covering a bright phone screen/white card creates a sharp DROP in brightness
        drop = baseline - currentLum;

        if (!isOccluded)
        {
            // Slowly adapt to ambient light drift when unoccluded
            if (drop < 10f)
            {
                baseline = Mathf.Lerp(baseline, currentLum, 0.03f);
            }

            // Debounce: require 2 consecutive frames of drop > threshold
            if (drop > occlusionThreshold)
            {
                triggerFrames++;
                if (triggerFrames >= 2 && Time.time - lastButtonPressTime > buttonCooldown)
                {
                    isOccluded = true;
                    triggerFrames = 0;
                    onPress?.Invoke();
                    lastDebugMessage = $"TRIGGERED: {btnTransform.name} (Drop={drop:F1} > {occlusionThreshold})";
                    Debug.Log($"[BoxBot] {lastDebugMessage}");
                }
            }
            else
            {
                triggerFrames = 0;
            }
        }
        else
        {
            // Release: when finger is removed and luminance returns toward baseline
            if (drop < occlusionThreshold * 0.4f)
            {
                isOccluded = false;
                triggerFrames = 0;
                baseline = currentLum;
                lastDebugMessage = $"RELEASED: {btnTransform.name}";
                Debug.Log($"[BoxBot] {lastDebugMessage}");
            }
        }
    }

    private float SampleLuminance(Vuforia.Image image, int cx, int cy, int radius)
    {
        int w = image.Width;
        int h = image.Height;
        byte[] pixels = image.Pixels;

        float sum = 0f;
        int count = 0;
        int stride = image.Stride > 0 ? image.Stride : w;

        for (int dy = -radius; dy <= radius; dy++)
        {
            int y = cy + dy;
            if (y < 0 || y >= h) continue;
            int row = y * stride;
            for (int dx = -radius; dx <= radius; dx++)
            {
                int x = cx + dx;
                if (x < 0 || x >= w) continue;
                sum += pixels[row + x];
                count++;
            }
        }

        return count > 0 ? (sum / count) : 0f;
    }

    public bool IsTargetTracked()
    {
        if (observerBehaviour == null) return false;
        var status = observerBehaviour.TargetStatus.Status;
        return status == Status.TRACKED || status == Status.EXTENDED_TRACKED;
    }
    #endregion

    #region Button Actions & Feedback
    private void TriggerMoodButton()
    {
        lastButtonPressTime = Time.time;
        CycleMood();
        AnimateButtonPress(moodButtonTransform, moodIndicator);
    }

    private void TriggerActionButton()
    {
        lastButtonPressTime = Time.time;
        bool triggered = false;

        if (actionSequencer != null)
        {
            triggered = actionSequencer.TriggerAction();
        }
        else if (!isWaving)
        {
            StartCoroutine(MoveBothArmsCoroutine());
            triggered = true;
        }

        if (triggered)
        {
            AnimateButtonPress(actionButtonTransform, actionIndicator);
        }
    }

    private void TriggerSoundButton()
    {
        lastButtonPressTime = Time.time;
        PlayRandomSound();
        AnimateButtonPress(soundButtonTransform, soundIndicator);
    }

    private void CycleMood()
    {
        if (moodMaterials == null || moodMaterials.Length == 0) return;

        currentMoodIndex = (currentMoodIndex + 1) % moodMaterials.Length;
        Material newMat = moodMaterials[currentMoodIndex];
        ApplyMoodMaterial(newMat);

        Debug.Log($"[BoxBot] Mood changed to: {newMat.name} (index {currentMoodIndex})");
    }

    private void ApplyMoodMaterial(Material mat)
    {
        if (mat == null) return;

        // Button indicator on the card tracks the current mood material
        if (moodIndicator != null) moodIndicator.sharedMaterial = mat;

        // Delegate directly to VFXController to mutate dynamicSuitEmissiveMaterial.
        // Overriding individual sharedMaterials with static assets breaks SRP Batcher
        // and disconnects runtime emissive overdrive during action sequences.
        Color moodHdr = mat.HasProperty("_EmissionColor")
            ? mat.GetColor("_EmissionColor")
            : (mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") * 4.0f : Color.cyan * 4.0f);

        if (vfxController == null)
        {
            vfxController = GetComponentInChildren<BoxBotVFXController>();
        }

        if (vfxController != null)
        {
            vfxController.SetMoodColor(moodHdr);
        }
    }

    /// <summary>
    /// Smoothly swings both arms back and forth in an alternating robotic movement
    /// </summary>
    private IEnumerator MoveBothArmsCoroutine()
    {
        isWaving = true;
        Quaternion leftStartRot = leftArmPivot != null ? leftArmPivot.localRotation : Quaternion.identity;
        Quaternion rightStartRot = rightArmPivot != null ? rightArmPivot.localRotation : Quaternion.identity;

        float totalDuration = 1.8f;
        float elapsed = 0f;
        int cycles = 2; // 2 complete back-and-forth swing cycles
        float maxSwingAngle = 45f;

        while (elapsed < totalDuration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / totalDuration);
            // Smooth bell envelope so it starts and ends at rest (0)
            float envelope = Mathf.Sin(progress * Mathf.PI);
            float oscillation = Mathf.Sin(progress * cycles * 2f * Mathf.PI);
            float currentAngle = oscillation * maxSwingAngle * envelope;

            if (leftArmPivot != null)
            {
                // Left arm swings forward on X
                leftArmPivot.localRotation = leftStartRot * Quaternion.Euler(currentAngle, 0, 0);
            }
            if (rightArmPivot != null)
            {
                // Right arm swings backward on X (alternating back and forth)
                rightArmPivot.localRotation = rightStartRot * Quaternion.Euler(-currentAngle, 0, 0);
            }

            yield return null;
        }

        if (leftArmPivot != null) leftArmPivot.localRotation = leftStartRot;
        if (rightArmPivot != null) rightArmPivot.localRotation = rightStartRot;

        isWaving = false;
        Debug.Log("[BoxBot] Both arms back-and-forth motion complete.");
    }

    private void PlayRandomSound()
    {
        if (audioSource == null || robotSounds == null || robotSounds.Length == 0) return;

        AudioClip clip = robotSounds[Random.Range(0, robotSounds.Length)];
        audioSource.pitch = Random.Range(0.9f, 1.15f);
        audioSource.PlayOneShot(clip);
        Debug.Log($"[BoxBot] Playing sound: {clip.name}");
    }

    private void AnimateButtonPress(Transform btnRoot, Renderer indicator)
    {
        if (btnRoot != null)
        {
            Transform cap = btnRoot.Find(btnRoot.name + "_Cap");
            if (cap != null)
            {
                StartCoroutine(DepressCapCoroutine(cap));
            }
        }

        if (indicator != null)
        {
            StartCoroutine(FlashCoroutine(indicator));
        }
    }

    private static readonly WaitForSeconds waitDepress = new WaitForSeconds(0.15f);
    private static readonly WaitForSeconds waitFlash = new WaitForSeconds(0.18f);

    private IEnumerator DepressCapCoroutine(Transform cap)
    {
        Vector3 origPos = cap.localPosition;
        Vector3 pressedPos = origPos - new Vector3(0, 0.001f, 0);

        cap.localPosition = pressedPos;
        yield return waitDepress;
        cap.localPosition = origPos;
    }

    private IEnumerator FlashCoroutine(Renderer indicator)
    {
        if (indicator == null) yield break;
        Material mat = indicator.sharedMaterial;
        if (mat == null) yield break;

        Color originalColor = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : Color.white;
        Color flashColor = new Color(1f, 1f, 1f, 0.8f);

        mat.SetColor("_BaseColor", flashColor);
        yield return waitFlash;
        mat.SetColor("_BaseColor", originalColor);
    }
    #endregion

#if DEVELOPMENT_BUILD || UNITY_EDITOR
    #region OnGUI Diagnostic HUD & Reticles
    private void OnGUI()
    {
        if (!showDiagnosticsHUD) return;

        GUI.Box(new Rect(10, 10, 430, 260), "BoxBot AR Diagnostics");

        bool tracked = IsTargetTracked();
        string trackStatus = observerBehaviour != null ? observerBehaviour.TargetStatus.Status.ToString() : "No Observer";
        
        GUI.color = tracked ? Color.green : Color.red;
        GUI.Label(new Rect(20, 32, 410, 20), $"Target Tracked: {tracked} ({trackStatus})");
        GUI.color = Color.white;

        GUI.Label(new Rect(20, 52, 410, 20), $"Camera Feed: {cameraImageWidth} x {cameraImageHeight} (Format Registered: {formatRegistered})");

        // Button statuses with pixel coordinates and Drop
        string activeMood = currentMoodIndex == 0 ? "BLUE" : "RED";
        GUI.color = moodOccluded ? (currentMoodIndex == 0 ? Color.cyan : Color.red) : Color.white;
        GUI.Label(new Rect(20, 75, 410, 20), $"Mood [{activeMood}]: Img=({moodImgCoord.x:F0},{moodImgCoord.y:F0}) Lum={moodCurrentLum:F0} Base={moodBaseline:F0} Drop={moodDrop:F1} {(moodOccluded ? "[PRESSED]" : "")}");

        GUI.color = actionOccluded ? Color.yellow : Color.white;
        GUI.Label(new Rect(20, 95, 410, 20), $"Action(Gold): Img=({actionImgCoord.x:F0},{actionImgCoord.y:F0}) Lum={actionCurrentLum:F0} Base={actionBaseline:F0} Drop={actionDrop:F1} {(actionOccluded ? "[PRESSED]" : "")}");

        GUI.color = soundOccluded ? Color.green : Color.white;
        GUI.Label(new Rect(20, 115, 410, 20), $"Sound (Green): Img=({soundImgCoord.x:F0},{soundImgCoord.y:F0}) Lum={soundCurrentLum:F0} Base={soundBaseline:F0} Drop={soundDrop:F1} {(soundOccluded ? "[PRESSED]" : "")}");
        GUI.color = Color.white;

        GUI.Label(new Rect(20, 140, 410, 20), $"Status: {lastDebugMessage}");
        GUI.Label(new Rect(20, 160, 410, 20), $"Threshold: {occlusionThreshold} | Cooldown: {buttonCooldown}s");

        // Action Mode Status & Phase
        if (actionSequencer != null)
        {
            string modeName = actionSequencer.CurrentMode == BoxBotActionSequencer.ActionMode.RepulsorPulse ? "REPULSOR PULSE" : "UNIBEAM LASER";
            GUI.color = actionSequencer.IsActionActive ? Color.yellow : Color.white;
            GUI.Label(new Rect(20, 182, 410, 20), $"Combat Mode: [{modeName}] Phase: {actionSequencer.CurrentPhase}");
            GUI.color = Color.white;
        }

        // Quick On-Screen sensitivity & action controls
        if (GUI.Button(new Rect(20, 210, 95, 24), "More Sensitive"))
        {
            occlusionThreshold = Mathf.Max(15f, occlusionThreshold - 4f);
        }
        if (GUI.Button(new Rect(122, 210, 95, 24), "Less Sensitive"))
        {
            occlusionThreshold = Mathf.Min(50f, occlusionThreshold + 4f);
        }
        if (GUI.Button(new Rect(224, 210, 95, 24), "Reset Baselines"))
        {
            moodBaseline = -1f;
            actionBaseline = -1f;
            soundBaseline = -1f;
        }
        if (GUI.Button(new Rect(326, 210, 98, 24), "Cycle Attack"))
        {
            if (actionSequencer != null)
            {
                var next = actionSequencer.CurrentMode == BoxBotActionSequencer.ActionMode.RepulsorPulse
                    ? BoxBotActionSequencer.ActionMode.UnibeamLaser
                    : BoxBotActionSequencer.ActionMode.RepulsorPulse;
                actionSequencer.SetActionMode(next);
            }
        }

        // Draw reticles directly over button screen positions
        if (tracked)
        {
            DrawScreenReticle(moodScreenPos, Color.cyan, "M");
            DrawScreenReticle(actionScreenPos, Color.yellow, "A");
            DrawScreenReticle(soundScreenPos, Color.green, "S");
        }
    }

    private void DrawScreenReticle(Vector3 screenPos, Color color, string label)
    {
        if (screenPos.z <= 0) return;
        float guiY = Screen.height - screenPos.y;
        float size = 32f;
        Rect r = new Rect(screenPos.x - size / 2f, guiY - size / 2f, size, size);

        Color old = GUI.color;
        GUI.color = color;
        GUI.Box(r, label);
        GUI.color = old;
    }
    #endregion
#endif
}
