// 사방망의 위치 선택, 왕복 파워 게이지, 물리 투척과 실패 재시도를 관리한다.
using TMPro;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

public class SabangStone : MonoBehaviour
{
    public enum ThrowState { Idle, Selecting, Charging, Flying, Result, Complete }

    [Header("직접 배치한 참조")]
    [SerializeField] private Map map;
    [SerializeField] private Rigidbody stoneBody;
    [SerializeField] private Collider stoneCollider;
    [SerializeField] private Transform throwPoint;
    [SerializeField] private Transform rayOrigin;
    [Tooltip("선택 사항. 오른손 조준 전용 Ray Interactor. 연결하지 않으면 기존 Ray Origin을 사용한다.")]
    [SerializeField] private XRRayInteractor aimingRay;
    [Tooltip("Aiming Ray와 같은 오브젝트의 Line Visual. 위치 선택 중에만 표시한다.")]
    [SerializeField] private XRInteractorLineVisual aimingLineVisual;
    [SerializeField] private InputActionReference rightTrigger;
    [Tooltip("오른손 실제 Trigger와 연결된 Action의 눌림 변화를 Console에 출력한다. 문제 확인 후 끌 수 있다.")]
    [SerializeField] private bool logTriggerDiagnostics = true;
    [SerializeField] private Transform targetMarker;
    [SerializeField] private LayerMask tileLayers;
    [SerializeField] private LayerMask groundLayers;
    [SerializeField] private LayerMask borderLayers;
    [SerializeField, Min(0.1f)] private float rayDistance = 10f;
    [Tooltip("표시용 마커만 표면 위로 띄우는 거리(m). 실제 목표 위치에는 적용하지 않는다.")]
    [SerializeField, Min(0f)] private float markerSurfaceOffset = 0.005f;

    [Header("플레이 HUD 표시")]
    [SerializeField] private UIManager uiManager;

    // 기존 씬·프리팹의 UI 참조를 보존하기 위한 이관 필드. 표시 로직은 UIManager에만 둔다.
    [SerializeField, HideInInspector] private GameObject gaugeRoot;
    [SerializeField, HideInInspector] private Slider powerSlider;
    [SerializeField, HideInInspector] private RectTransform successBand;
    [SerializeField, HideInInspector] private TMP_Text resultText;

    [Header("파워와 비행")]
    [SerializeField, Range(1, 8)] private int targetNumber = 1;
    [SerializeField] private float[] requiredPowers = { 25f, 30f, 40f, 50f, 55f, 65f, 75f, 80f };
    [SerializeField, Range(0f, 50f)] private float tolerance = 10f;
    [SerializeField, Min(1f)] private float powerPerSecond = 60f;
    [Tooltip("허용 범위를 벗어난 파워 1당 짧거나 길게 떨어질 거리(m)")]
    [SerializeField, Min(0.001f)] private float missDistancePerPower = 0.04f;
    [SerializeField, Min(0.1f)] private float flightTime = 1f;
    [SerializeField, Min(0.01f)] private float restingCenterHeight = 0.03f;
    [SerializeField, Min(0.01f)] private float settleSpeed = 0.15f;
    [Tooltip("착지 안정화로 인정할 최대 회전 속도(rad/s)")]
    [SerializeField, Min(0.01f)] private float settleAngularSpeed = 0.5f;
    [SerializeField, Min(0.01f)] private float settleDuration = 0.2f;
    [SerializeField, Min(1f)] private float flightTimeout = 5f;
    [SerializeField, Min(0.1f)] private float resultDuration = 1.2f;
    [SerializeField] private bool startAutomatically = false;

    [Header("외부 게임 진행 연결")]
    [SerializeField] private UnityEvent onThrowSucceeded = new UnityEvent();
    [SerializeField] private UnityEvent onThrowFailed = new UnityEvent();

    public ThrowState State { get; private set; }
    public int FailureCount { get; private set; }
    public float SelectedPower { get; private set; }
    public Vector3 SelectedPoint => selectedPoint;
    public int TargetNumber => targetNumber;
    public bool StartsAutomatically => startAutomatically;
    public event System.Action<ThrowState> StateChanged;

    private float elapsed, stableTime, chargingStartedAt;
    private Vector3 selectedPoint;
    private bool triggerReleased, powerMatched, touchedBorder;
    private bool previousActionPressed, previousDevicePressed, reportedMissingInput;
    private UnityEngine.XR.InputDevice diagnosticRightDevice;
    private readonly HashSet<Collider> groundContacts = new HashSet<Collider>();
    private bool capturedAimVisual;
    private bool originalLineEnabled;
    private bool originalSetLineColor;
    private Gradient originalValidGradient, originalInvalidGradient, originalBlockedGradient;
    private Gradient validAimGradient, invalidAimGradient;

    private void Start()
    {
        if (logTriggerDiagnostics) ReportTriggerConfiguration();
        if (State != ThrowState.Idle) return;
        if (PrepareUI() && uiManager.ValidateConfiguration(transform)) uiManager.ResetPresentation();
        SetTargetMarker(false);
        if (startAutomatically) BeginRound(targetNumber);
    }

    public void BeginRound(int number)
    {
        TryBeginRound(number);
    }

    public bool CanBeginRound(int number)
    {
        if (!isActiveAndEnabled) return ConfigurationError("SabangStone을 활성화하세요.");
        if (map == null || !map.isActiveAndEnabled) return ConfigurationError("활성화된 Map을 연결하세요.");
        if (stoneBody == null || stoneBody.gameObject != gameObject)
            return ConfigurationError("SabangStone과 같은 오브젝트의 Rigidbody를 Stone Body에 연결하세요.");
        if (stoneCollider == null || stoneCollider.attachedRigidbody != stoneBody ||
            !stoneCollider.enabled || stoneCollider.isTrigger)
            return ConfigurationError("Stone Body에 속한 활성 Collider를 연결하고 Is Trigger를 끄세요.");
        if (throwPoint == null || throwPoint.IsChildOf(transform))
            return ConfigurationError("망 자신이나 자식이 아닌 Throw Point를 연결하세요.");
        if (aimingRay == null && rayOrigin == null) return ConfigurationError("오른손 Aiming Ray 또는 Ray Origin을 연결하세요.");
        if (aimingRay != null)
        {
            if (!aimingRay.isActiveAndEnabled || aimingRay.lineType != XRRayInteractor.LineType.StraightLine
                || aimingRay.hitDetectionType != XRRayInteractor.HitDetectionType.Raycast
                || aimingRay.raycastTriggerInteraction != QueryTriggerInteraction.Collide
                || (aimingRay.raycastMask.value & tileLayers.value) != tileLayers.value
                || aimingRay.enableUIInteraction)
                return ConfigurationError("Aiming Ray를 활성화하고 Straight Line, Raycast, Trigger Collide, Tile Layers 포함, UI Interaction 끄기로 설정하세요.");
            if (aimingLineVisual != null && aimingLineVisual.gameObject != aimingRay.gameObject)
                return ConfigurationError("Aiming Ray와 같은 오브젝트의 Aiming Line Visual을 연결하세요.");
        }
        if (rightTrigger == null || rightTrigger.action == null || !rightTrigger.action.enabled)
            return ConfigurationError("오른손 Trigger Input Action을 연결하고 XR Origin의 Input Action Manager로 활성화하세요.");
        if (targetMarker == null || transform.IsChildOf(targetMarker))
            return ConfigurationError("망 자신이나 부모가 아닌 별도 Target Marker를 연결하세요.");
        if (tileLayers.value == 0 || (tileLayers.value & (1 << map.gameObject.layer)) == 0)
            return ConfigurationError("Tile Layers에 Map 오브젝트의 Layer를 포함하세요.");
        if ((tileLayers.value & (groundLayers.value | borderLayers.value)) != 0)
            return ConfigurationError("Tile Layers는 Ground Layers 및 Border Layers와 분리하세요.");
        if (groundLayers.value == 0 || borderLayers.value == 0)
            return ConfigurationError("Ground Layers와 Border Layers를 지정하세요.");
        if (!PrepareUI()) return false;
        if (!uiManager.ValidateConfiguration(transform)) return false;
        if (flightTime <= 0f || flightTimeout <= flightTime || settleDuration <= 0f || resultDuration <= 0f)
            return ConfigurationError("비행·안정화·결과 시간은 양수이며 Flight Timeout은 Flight Time보다 커야 합니다.");
        if (number < 1 || number > 8 || requiredPowers == null || requiredPowers.Length != 8)
            return ConfigurationError("목표 번호는 1~8, Required Powers는 8개로 설정하세요.");
        return true;
    }

    private bool ConfigurationError(string message)
    {
        Debug.LogError("SabangStone: " + message, this);
        return false;
    }

    public bool TryBeginRound(int number)
    {
        // On Yes 등에 BeginRound가 중복 연결되어도 진행 중인 선택을 초기화하지 않는다.
        if (State != ThrowState.Idle && State != ThrowState.Complete) return false;
        if (!CanBeginRound(number)) return false;
        targetNumber = number;
        CaptureAimVisual();
        ResetAttempt();
        return true;
    }

    private void ResetAttempt()
    {
        FreezeStone();
        stoneBody.position = throwPoint.position;
        stoneBody.rotation = throwPoint.rotation;
        touchedBorder = powerMatched = false;
        selectedPoint = Vector3.zero;
        SelectedPower = 0f;
        triggerReleased = false;
        elapsed = stableTime = 0f;
        uiManager?.HideGauge();
        uiManager?.ClearResult();
        SetTargetMarker(false);
        SetState(ThrowState.Selecting);
    }

    private void LateUpdate()
    {
        if (State == ThrowState.Idle || State == ThrowState.Complete) return;
        // 비행 중 입력도 소비하여 다음 단계로 눌림이 넘어가지 않게 한다.
        bool confirm = ReadConfirmation();
        switch (State)
        {
            case ThrowState.Selecting:
                UpdateSelection(confirm);
                break;
            case ThrowState.Charging:
                UpdateCharging(confirm);
                break;
            case ThrowState.Result:
                elapsed += Time.deltaTime;
                if (elapsed >= resultDuration) ResetAttempt();
                break;
        }
    }

    private bool ReadConfirmation()
    {
        InputAction action = rightTrigger != null ? rightTrigger.action : null;
        if (action == null || !action.enabled)
        {
            if (!reportedMissingInput)
                Debug.LogError("SabangStone: 영역 선택 중 Right Trigger Action이 없거나 비활성화되었습니다. Input Action Manager와 Action 연결을 확인하세요.", this);
            reportedMissingInput = true;
            triggerReleased = false;
            if (logTriggerDiagnostics) TraceTriggerInput(action, false);
            return false;
        }
        reportedMissingInput = false;
        bool pressed = action.IsPressed();
        if (logTriggerDiagnostics) TraceTriggerInput(action, pressed);
        if (!pressed) triggerReleased = true;
        bool confirm = pressed && triggerReleased;
        if (confirm) triggerReleased = false;
        return confirm;
    }

    [ContextMenu("진단/오른손 Trigger 연결 확인")]
    private void ReportTriggerConfiguration()
    {
        InputAction action = rightTrigger != null ? rightTrigger.action : null;
        if (action == null)
        {
            Debug.LogWarning("SabangStone 입력 진단: Right Trigger Action이 연결되지 않았습니다.", this);
            return;
        }
        var details = new System.Text.StringBuilder();
        details.AppendLine($"Action={action.actionMap?.name}/{action.name}, Type={action.type}, Enabled={action.enabled}, State={State}.");
        foreach (InputBinding binding in action.bindings)
            details.AppendLine($"Binding={binding.effectivePath}.");
        foreach (var control in action.controls)
            details.AppendLine($"Resolved Control={control.path}, Device={control.device.displayName}.");
        Debug.Log("SabangStone 입력 진단:\n" + details, this);
    }

    private void TraceTriggerInput(InputAction action, bool actionPressed)
    {
        if (!diagnosticRightDevice.isValid)
            diagnosticRightDevice = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
        bool devicePressed = false;
        bool readable = diagnosticRightDevice.isValid && diagnosticRightDevice.TryGetFeatureValue(
            UnityEngine.XR.CommonUsages.triggerButton, out devicePressed);
        if (actionPressed != previousActionPressed || devicePressed != previousDevicePressed)
        {
            Debug.Log($"SabangStone Trigger 진단: State={State}, DeviceReadable={readable}, DevicePressed={devicePressed}, "
                + $"Action={action?.actionMap?.name}/{action?.name}, Enabled={action?.enabled}, ActionPressed={actionPressed}, Released={triggerReleased}.", this);
        }
        previousActionPressed = actionPressed;
        previousDevicePressed = devicePressed;
    }

    private void UpdateSelection(bool confirm)
    {
        bool valid = TryGetAimHit(out RaycastHit hit)
            && InLayers(hit.collider, tileLayers)
            && map.TryGetRegion(hit, out Map.Region region) && (int)region == targetNumber;
        UpdateAimColor(valid);
        if (targetMarker != null)
        {
            SetTargetMarker(valid);
            if (valid) targetMarker.position = hit.point + map.transform.up * markerSurfaceOffset;
        }
        if (!confirm || !valid)
        {
            if (confirm && logTriggerDiagnostics)
                Debug.Log($"SabangStone 영역 확정 거부: Collider={hit.collider?.name}, Target={targetNumber}. 목표 칸 내부를 다시 가리키세요.", this);
            return;
        }
        selectedPoint = hit.point;
        chargingStartedAt = Time.time;
        uiManager.ShowGauge(requiredPowers[targetNumber - 1], tolerance);
        SetState(ThrowState.Charging);
        if (logTriggerDiagnostics)
            Debug.Log($"SabangStone 영역 확정 성공: Target={targetNumber}, State={State}, Point={selectedPoint}. 게이지 표시를 요청했습니다.", this);
    }

    private bool TryGetAimHit(out RaycastHit hit)
    {
        // XRI의 표시와 확정 위치에 같은 충돌 결과를 사용하여 이중 Raycast를 피한다.
        if (aimingRay != null)
        {
            hit = default;
            return aimingRay.isActiveAndEnabled && aimingRay.TryGetCurrent3DRaycastHit(out hit);
        }
        return Physics.Raycast(rayOrigin.position, rayOrigin.forward, out hit,
            rayDistance, tileLayers, QueryTriggerInteraction.Collide);
    }

    private void CaptureAimVisual()
    {
        if (capturedAimVisual || aimingRay == null || aimingLineVisual == null) return;
        originalLineEnabled = aimingLineVisual.enabled;
        originalSetLineColor = aimingLineVisual.setLineColorGradient;
        originalValidGradient = aimingLineVisual.validColorGradient;
        originalInvalidGradient = aimingLineVisual.invalidColorGradient;
        originalBlockedGradient = aimingLineVisual.blockedColorGradient;
        validAimGradient = originalValidGradient;
        invalidAimGradient = originalInvalidGradient;
        capturedAimVisual = true;
        aimingLineVisual.setLineColorGradient = true;
    }

    private void UpdateAimColor(bool valid)
    {
        if (!capturedAimVisual || aimingLineVisual == null) return;
        // Map Collider는 XR Interactable이 아니므로 선 색상을 게임의 구역 판정에 맞춘다.
        Gradient color = valid ? validAimGradient : invalidAimGradient;
        aimingLineVisual.validColorGradient = color;
        aimingLineVisual.invalidColorGradient = color;
        aimingLineVisual.blockedColorGradient = color;
    }

    private void RestoreAimVisual()
    {
        if (!capturedAimVisual) return;
        if (aimingLineVisual != null)
        {
            aimingLineVisual.enabled = originalLineEnabled;
            aimingLineVisual.setLineColorGradient = originalSetLineColor;
            aimingLineVisual.validColorGradient = originalValidGradient;
            aimingLineVisual.invalidColorGradient = originalInvalidGradient;
            aimingLineVisual.blockedColorGradient = originalBlockedGradient;
        }
        capturedAimVisual = false;
    }

    private void UpdateCharging(bool confirm)
    {
        float power = Mathf.PingPong((Time.time - chargingStartedAt) * powerPerSecond, 100f);
        uiManager.SetPower(power);
        if (confirm) Throw(power);
    }

    private void Throw(float power)
    {
        if (State != ThrowState.Charging) return;
        SelectedPower = power;
        uiManager?.HideGauge();
        SetTargetMarker(false);
        float error = power - Mathf.Clamp(requiredPowers[targetNumber - 1], 0f, 100f);
        powerMatched = Mathf.Abs(error) <= tolerance;
        // 적정 파워는 선택 지점에 도달하고, 범위 밖에서는 오차에 비례해 거리가 변한다.
        float miss = powerMatched ? 0f : Mathf.Sign(error) * (Mathf.Abs(error) - tolerance) * missDistancePerPower;
        Vector3 up = map.transform.up;
        Vector3 direction = Vector3.ProjectOnPlane(selectedPoint - throwPoint.position, up).normalized;
        Vector3 destination = selectedPoint + direction * miss + up * restingCenterHeight;
        stoneBody.position = throwPoint.position;
        stoneBody.rotation = throwPoint.rotation;
        stoneBody.isKinematic = false;
        stoneBody.useGravity = true;
        stoneBody.angularVelocity = Vector3.zero;
        // Rigidbody 위치가 아니라 Collider 중심이 목표 높이에 도달하도록 보정한다.
        Physics.SyncTransforms();
        Vector3 centerOffset = stoneCollider.bounds.center - stoneBody.position;
        stoneBody.linearVelocity = (destination - centerOffset - stoneBody.position) / flightTime
            - 0.5f * Physics.gravity * flightTime;
        elapsed = stableTime = 0f;
        touchedBorder = false;
        groundContacts.Clear();
        SetState(ThrowState.Flying);
    }

    private void FixedUpdate()
    {
        if (State != ThrowState.Flying) return;
        elapsed += Time.fixedDeltaTime;
        // 잠든 Rigidbody에는 OnCollisionStay가 생략되므로 Enter/Exit 사이 접촉을 보존한다.
        groundContacts.RemoveWhere(IsInactiveCollider);
        stableTime = groundContacts.Count > 0 && stoneBody.linearVelocity.sqrMagnitude <= settleSpeed * settleSpeed
            && stoneBody.angularVelocity.sqrMagnitude <= settleAngularSpeed * settleAngularSpeed
            ? stableTime + Time.fixedDeltaTime : 0f;
        if (stableTime >= settleDuration) FinishThrow(false);
        else if (elapsed >= flightTimeout) FinishThrow(true);
    }

    private void FinishThrow(bool timedOut)
    {
        if (State != ThrowState.Flying) return;
        bool success = !timedOut && powerMatched && !touchedBorder
            && map.TryGetRegion(stoneCollider.bounds.center, out Map.Region region)
            && (int)region == targetNumber;
        FreezeStone();
        elapsed = 0f;
        uiManager?.HideGauge();
        SetTargetMarker(false);
        uiManager.ShowThrowResult(success);
        if (!success) FailureCount++;
        SetState(success ? ThrowState.Complete : ThrowState.Result);
        if (success) onThrowSucceeded.Invoke();
        else onThrowFailed.Invoke();
    }

    private void FreezeStone()
    {
        if (stoneBody == null) return;
        if (!stoneBody.isKinematic)
        {
            stoneBody.linearVelocity = Vector3.zero;
            stoneBody.angularVelocity = Vector3.zero;
        }
        stoneBody.isKinematic = true;
        groundContacts.Clear();
    }

    private static bool IsInactiveCollider(Collider collider)
    {
        return collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy;
    }

    private void OnCollisionEnter(Collision collision) { RegisterCollision(collision.collider); }
    private void OnCollisionStay(Collision collision) { RegisterCollision(collision.collider); }
    private void OnCollisionExit(Collision collision) { groundContacts.Remove(collision.collider); }
    private void RegisterCollision(Collider other)
    {
        if (State != ThrowState.Flying) return;
        if (InLayers(other, groundLayers)) groundContacts.Add(other);
        if (InLayers(other, borderLayers)) touchedBorder = true;
    }
    private void OnTriggerEnter(Collider other) { RegisterBorder(other); }
    private void OnTriggerStay(Collider other) { RegisterBorder(other); }
    private void RegisterBorder(Collider other)
    {
        if (State == ThrowState.Flying && InLayers(other, borderLayers)) touchedBorder = true;
    }
    private static bool InLayers(Collider other, LayerMask layers)
    {
        return other != null && (layers.value & (1 << other.gameObject.layer)) != 0;
    }

    private bool PrepareUI()
    {
        if (uiManager == null)
            return ConfigurationError("UIManager를 별도 UI 오브젝트에 추가하고 UI Manager에 연결하세요.");
        uiManager.PreserveExistingReferences(gaugeRoot, powerSlider, successBand, resultText);
        return true;
    }

#if UNITY_EDITOR
    [ContextMenu("UI/기존 HUD 참조를 UIManager로 복사")]
    private void CopyExistingUIReferences()
    {
        if (uiManager == null)
        {
            ConfigurationError("먼저 UI Manager를 연결하세요.");
            return;
        }
        UnityEditor.Undo.RecordObject(uiManager, "Copy existing Sabang HUD references");
        uiManager.PreserveExistingReferences(gaugeRoot, powerSlider, successBand, resultText);
        UnityEditor.EditorUtility.SetDirty(uiManager);
        if (uiManager.gameObject.scene.IsValid())
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(uiManager.gameObject.scene);
    }
#endif
    private void SetTargetMarker(bool visible)
    {
        if (targetMarker != null && targetMarker.gameObject.activeSelf != visible)
            targetMarker.gameObject.SetActive(visible);
    }
    private void OnDisable()
    {
        FreezeStone();
        uiManager?.HideGauge();
        uiManager?.ClearResult();
        SetTargetMarker(false);
        SetState(ThrowState.Idle);
        RestoreAimVisual();
    }

    public void StopThrowing()
    {
        FreezeStone();
        uiManager?.HideGauge();
        uiManager?.ClearResult();
        SetTargetMarker(false);
        SetState(ThrowState.Idle);
        RestoreAimVisual();
    }

    private void SetState(ThrowState next)
    {
        if (State == next) return;
        State = next;
        if (capturedAimVisual && aimingLineVisual != null)
            aimingLineVisual.enabled = next == ThrowState.Selecting;
        StateChanged?.Invoke(next);
    }
}
