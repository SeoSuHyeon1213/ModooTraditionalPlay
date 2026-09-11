// 사방망의 위치 선택, 왕복 파워 게이지, 물리 투척과 실패 재시도를 관리한다.
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class SabangStone : MonoBehaviour
{
    public enum ThrowState { Idle, Selecting, Charging, Flying, Result, Complete }

    [Header("직접 배치한 참조")]
    [SerializeField] private Map map;
    [SerializeField] private Rigidbody stoneBody;
    [SerializeField] private Collider stoneCollider;
    [SerializeField] private Transform throwPoint;
    [SerializeField] private Transform rayOrigin;
    [SerializeField] private InputActionReference rightTrigger;
    [SerializeField] private Transform targetMarker;
    [SerializeField] private LayerMask tileLayers;
    [SerializeField] private LayerMask groundLayers;
    [SerializeField] private LayerMask borderLayers;
    [SerializeField, Min(0.1f)] private float rayDistance = 10f;

    [Header("직접 배치한 UI")]
    [SerializeField] private GameObject gaugeRoot;
    [SerializeField] private Slider powerSlider;
    [Tooltip("게이지 전체 폭을 가진 부모 아래의 성공 구간 Image. 좌우 anchor를 코드로 변경한다.")]
    [SerializeField] private RectTransform successBand;
    [SerializeField] private TMP_Text resultText;

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
    [SerializeField, Min(0.01f)] private float settleDuration = 0.2f;
    [SerializeField, Min(1f)] private float flightTimeout = 5f;
    [SerializeField, Min(0.1f)] private float resultDuration = 1.2f;
    [SerializeField] private bool startAutomatically = true;

    [Header("외부 게임 진행 연결")]
    [SerializeField] private UnityEvent onThrowSucceeded = new UnityEvent();
    [SerializeField] private UnityEvent onThrowFailed = new UnityEvent();

    public ThrowState State { get; private set; }
    public int FailureCount { get; private set; }
    public float SelectedPower { get; private set; }

    private float elapsed, stableTime, chargingStartedAt;
    private Vector3 selectedPoint;
    private bool triggerReleased, powerMatched, touchedBorder, groundContact;
    private bool enabledInput;

    private void OnEnable()
    {
        if (rightTrigger != null && rightTrigger.action != null && !rightTrigger.action.enabled)
        {
            rightTrigger.action.Enable();
            enabledInput = true;
        }
    }

    private void Start()
    {
        SetGauge(false);
        if (targetMarker != null) targetMarker.gameObject.SetActive(false);
        ShowResult("");
        if (startAutomatically) BeginRound(targetNumber);
    }

    public void BeginRound(int number)
    {
        if (map == null || stoneBody == null || stoneCollider == null || throwPoint == null ||
            rayOrigin == null || rightTrigger == null || rightTrigger.action == null ||
            number < 1 || number > 8 || requiredPowers == null || requiredPowers.Length != 8 ||
            stoneBody.gameObject != gameObject || stoneCollider.attachedRigidbody != stoneBody)
        {
            Debug.LogError("SabangStone: 망 Rigidbody와 같은 오브젝트에 스크립트를 붙이고 참조 및 파워 8개를 확인하세요.", this);
            return;
        }
        targetNumber = number;
        ResetAttempt();
    }

    private void ResetAttempt()
    {
        stoneBody.isKinematic = true;
        stoneBody.position = throwPoint.position;
        stoneBody.rotation = throwPoint.rotation;
        touchedBorder = groundContact = false;
        triggerReleased = false;
        elapsed = stableTime = 0f;
        State = ThrowState.Selecting;
        SetGauge(false);
        ShowResult("");
        if (targetMarker != null) targetMarker.gameObject.SetActive(false);
    }

    private void Update()
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
        bool pressed = rightTrigger.action.IsPressed();
        if (!pressed) triggerReleased = true;
        bool confirm = pressed && triggerReleased;
        if (confirm) triggerReleased = false;
        return confirm;
    }

    private void UpdateSelection(bool confirm)
    {
        bool valid = Physics.Raycast(rayOrigin.position, rayOrigin.forward, out RaycastHit hit,
            rayDistance, tileLayers, QueryTriggerInteraction.Collide)
            && map.TryGetRegion(hit, out Map.Region region) && (int)region == targetNumber;
        if (targetMarker != null)
        {
            if (targetMarker.gameObject.activeSelf != valid) targetMarker.gameObject.SetActive(valid);
            if (valid) targetMarker.position = hit.point;
        }
        if (!confirm || !valid) return;
        selectedPoint = hit.point;
        chargingStartedAt = Time.time;
        State = ThrowState.Charging;
        SetGauge(true);
        UpdateGauge(0f);
    }

    private void UpdateCharging(bool confirm)
    {
        float power = Mathf.PingPong((Time.time - chargingStartedAt) * powerPerSecond, 100f);
        UpdateGauge(power);
        if (confirm) Throw(power);
    }

    private void Throw(float power)
    {
        SelectedPower = power;
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
        touchedBorder = groundContact = false;
        State = ThrowState.Flying;
    }

    private void FixedUpdate()
    {
        if (State != ThrowState.Flying) return;
        elapsed += Time.fixedDeltaTime;
        stableTime = groundContact && stoneBody.linearVelocity.sqrMagnitude <= settleSpeed * settleSpeed
            ? stableTime + Time.fixedDeltaTime : 0f;
        groundContact = false;
        if (stableTime >= settleDuration) FinishThrow(false);
        else if (elapsed >= flightTimeout) FinishThrow(true);
    }

    private void FinishThrow(bool timedOut)
    {
        bool success = !timedOut && powerMatched && !touchedBorder
            && map.TryGetRegion(stoneCollider.bounds.center, out Map.Region region)
            && (int)region == targetNumber;
        stoneBody.isKinematic = true;
        elapsed = 0f;
        SetGauge(false);
        if (targetMarker != null) targetMarker.gameObject.SetActive(false);
        State = success ? ThrowState.Complete : ThrowState.Result;
        ShowResult(success ? "성공" : "실패");
        if (success) onThrowSucceeded.Invoke();
        else { FailureCount++; onThrowFailed.Invoke(); }
    }

    private void OnCollisionEnter(Collision collision) { RegisterCollision(collision.collider); }
    private void OnCollisionStay(Collision collision) { RegisterCollision(collision.collider); }
    private void RegisterCollision(Collider other)
    {
        if (State != ThrowState.Flying) return;
        if (InLayers(other, groundLayers)) groundContact = true;
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
        return (layers.value & (1 << other.gameObject.layer)) != 0;
    }

    private void UpdateGauge(float power)
    {
        if (powerSlider != null) powerSlider.normalizedValue = power / 100f;
    }
    private void SetGauge(bool visible)
    {
        if (gaugeRoot != null) gaugeRoot.SetActive(visible);
        if (!visible || successBand == null) return;
        float required = Mathf.Clamp(requiredPowers[targetNumber - 1], 0f, 100f);
        Vector2 min = successBand.anchorMin, max = successBand.anchorMax;
        min.x = Mathf.Clamp01((required - tolerance) / 100f);
        max.x = Mathf.Clamp01((required + tolerance) / 100f);
        successBand.anchorMin = min;
        successBand.anchorMax = max;
        Vector2 low = successBand.offsetMin, high = successBand.offsetMax;
        low.x = high.x = 0f;
        successBand.offsetMin = low;
        successBand.offsetMax = high;
    }
    private void ShowResult(string message)
    {
        if (resultText != null) resultText.text = message;
    }
    private void OnDisable()
    {
        if (enabledInput && rightTrigger != null) rightTrigger.action.Disable();
        enabledInput = false;
        if (stoneBody != null && State == ThrowState.Flying) stoneBody.isKinematic = true;
        State = ThrowState.Idle;
        SetGauge(false);
        if (targetMarker != null) targetMarker.gameObject.SetActive(false);
    }
}
