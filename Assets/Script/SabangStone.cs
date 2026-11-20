// 사방망을 XR Grab으로 직접 잡아 던지게 하고, 착지 위치와 경계선 접촉으로 투척 결과와 실패 재시도를 관리한다.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class SabangStone : MonoBehaviour
{
    // 정수 순서를 유지한다. Ready는 잡기 대기, Holding은 손에 들고 있는 상태다.
    public enum ThrowState { Idle, Ready, Holding, Flying, Result, Complete }

    [Header("직접 배치한 참조")]
    [SerializeField] private Map map;
    [SerializeField] private Rigidbody stoneBody;
    [SerializeField] private Collider stoneCollider;
    [Tooltip("망과 같은 오브젝트의 XR Grab Interactable. Throw On Detach를 켜고, 던지는 힘은 Throw Velocity Scale로 조절한다.")]
    [SerializeField] private XRGrabInteractable grabInteractable;
    [Tooltip("투척 대기 중 망이 놓이는 위치. 숙이지 않고 손을 뻗어 잡을 수 있는 높이에 둔다.")]
    [SerializeField] private Transform throwPoint;
    [Tooltip("선택 사항. 땅따먹기 중 망을 놓을 위치. 땅따먹기 위치가 시작 위치와 다를 때만 연결하고, 비우면 Throw Point를 사용한다.")]
    [SerializeField] private Transform claimThrowPoint;
    [SerializeField] private LayerMask groundLayers;
    [SerializeField] private LayerMask borderLayers;

    [Header("플레이 HUD 표시")]
    [SerializeField] private UIManager uiManager;

    [Header("목표와 착지")]
    [SerializeField, Range(1, 8)] private int targetNumber = 1;
    [Tooltip("첫 바닥 착지 때 남길 수평 속도와 회전 속도의 비율. 0이면 그 자리에 멈추고 1이면 물리 그대로 미끄러진다.")]
    [SerializeField, Range(0f, 1f)] private float landingSlideKeep = 0.1f;
    [SerializeField, Min(0.01f)] private float settleSpeed = 0.15f;
    [Tooltip("착지 안정화로 인정할 최대 회전 속도(rad/s)")]
    [SerializeField, Min(0.01f)] private float settleAngularSpeed = 0.5f;
    [SerializeField, Min(0.01f)] private float settleDuration = 0.2f;
    [Tooltip("손에서 놓은 뒤 이 시간 안에 바닥에 멈추지 않으면 실패로 처리한다(초).")]
    [SerializeField, Min(1f)] private float flightTimeout = 5f;
    [SerializeField, Min(0.1f)] private float resultDuration = 1.2f;
    [SerializeField] private bool startAutomatically = false;

    [Header("외부 게임 진행 연결")]
    [SerializeField] private UnityEvent onThrowSucceeded = new UnityEvent();
    [SerializeField] private UnityEvent onThrowFailed = new UnityEvent();

    public ThrowState State { get; private set; }
    public int FailureCount { get; private set; }
    public int TargetNumber => targetNumber;
    // 땅따먹기 투척이면 목표 칸 없이 1~8번 중 착지한 칸을 성공으로 본다.
    public bool IsClaiming { get; private set; }
    // 마지막 성공 투척이 멈춘 칸 번호. 성공 전이나 실패 후에는 0이다.
    public int LandedTile { get; private set; }
    public bool StartsAutomatically => startAutomatically;
    public event System.Action<ThrowState> StateChanged;

    private float elapsed, stableTime;
    private bool touchedBorder, landed;
    private readonly HashSet<Collider> groundContacts = new HashSet<Collider>();
    private XRGrabInteractable subscribedGrab;
    private InteractionLayerMask originalGrabLayers;
    private bool capturedGrabLayers;

    private void OnEnable()
    {
        subscribedGrab = grabInteractable;
        if (subscribedGrab == null) return;
        subscribedGrab.selectEntered.AddListener(HandleGrabbed);
        subscribedGrab.selectExited.AddListener(HandleReleased);
    }

    private void Start()
    {
        if (State != ThrowState.Idle) return;
        ApplyGrabPermission();
        if (uiManager != null && uiManager.ValidateConfiguration()) uiManager.ClearResult();
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
            return ConfigurationError("Stone Body에 속한 활성 Collider를 연결하고 Is Trigger를 끄세요. Mesh Collider는 Convex를 켜야 합니다.");
        if (grabInteractable == null || grabInteractable.gameObject != gameObject || subscribedGrab != grabInteractable)
            return ConfigurationError("망과 같은 오브젝트의 XR Grab Interactable을 Grab Interactable에 연결하세요.");
        if (!grabInteractable.isActiveAndEnabled || !grabInteractable.throwOnDetach)
            return ConfigurationError("XR Grab Interactable을 활성화하고 Throw On Detach를 켜세요.");
        if (throwPoint == null || throwPoint.IsChildOf(transform))
            return ConfigurationError("망 자신이나 자식이 아닌 Throw Point를 연결하세요.");
        if (claimThrowPoint != null && claimThrowPoint.IsChildOf(transform))
            return ConfigurationError("Claim Throw Point는 망 자신이나 자식이 아닌 위치를 연결하세요.");
        if (groundLayers.value == 0 || borderLayers.value == 0)
            return ConfigurationError("Ground Layers와 Border Layers를 지정하세요.");
        if (uiManager == null)
            return ConfigurationError("UIManager를 별도 UI 오브젝트에 추가하고 UI Manager에 연결하세요.");
        if (!uiManager.ValidateConfiguration()) return false;
        if (number < 1 || number > 8) return ConfigurationError("목표 번호는 1~8로 설정하세요.");
        return true;
    }

    private bool ConfigurationError(string message)
    {
        Debug.LogError("SabangStone: " + message, this);
        return false;
    }

    public bool TryBeginRound(int number)
    {
        // 시작 이벤트가 중복 연결되어도 진행 중인 시도를 초기화하지 않는다.
        if (State != ThrowState.Idle && State != ThrowState.Complete) return false;
        if (!CanBeginRound(number)) return false;
        targetNumber = number;
        IsClaiming = false;
        ResetAttempt();
        return true;
    }

    // 8번 라운드 이후의 땅따먹기 투척을 시작한다. 판정 외의 잡기·비행·재시도 흐름은 라운드 투척과 같다.
    public bool TryBeginClaim()
    {
        if (State != ThrowState.Idle && State != ThrowState.Complete) return false;
        if (!CanBeginRound(targetNumber)) return false;
        IsClaiming = true;
        ResetAttempt();
        return true;
    }

    // 새 게임을 시작할 때만 호출한다. 진행 중에는 라운드와 땅따먹기 실패를 모두 누적한다.
    public void ResetFailureCount()
    {
        FailureCount = 0;
    }

    // 망을 Throw Point에 되돌려 같은 목표의 잡기 대기부터 다시 시작한다.
    private void ResetAttempt()
    {
        FreezeStone();
        Transform point = IsClaiming && claimThrowPoint != null ? claimThrowPoint : throwPoint;
        // Kinematic 상태에서 Transform을 옮겨 같은 프레임의 잡기 판정이 새 위치를 쓰게 한다.
        transform.SetPositionAndRotation(point.position, point.rotation);
        touchedBorder = landed = false;
        elapsed = stableTime = 0f;
        LandedTile = 0;
        if (IsClaiming) uiManager.ShowClaimPrompt();
        else uiManager.ShowTargetPrompt(targetNumber);
        SetState(ThrowState.Ready);
    }

    private void HandleGrabbed(SelectEnterEventArgs args)
    {
        // 잡기 허용 Layer로 대기 중에만 잡히지만, 다른 경로로 잡혀도 상태를 건너뛰지 않는다.
        if (State != ThrowState.Ready) return;
        SetState(ThrowState.Holding);
    }

    private void HandleReleased(SelectExitEventArgs args)
    {
        if (State != ThrowState.Holding) return;
        // 추적 끊김이나 Interactor 비활성화로 취소되면 투척으로 세지 않고 다시 잡게 한다.
        if (args.isCanceled)
        {
            ResetAttempt();
            return;
        }
        Release();
    }

    private void Release()
    {
        // XRI는 놓을 때 잡기 전의 Kinematic 값을 복원하므로, 같은 프레임 후반의 Detach가 던지는 속도를 적용하도록 물리를 켠다.
        stoneBody.isKinematic = false;
        stoneBody.useGravity = true;
        elapsed = stableTime = 0f;
        touchedBorder = landed = false;
        groundContacts.Clear();
        SetState(ThrowState.Flying);
    }

    private void Update()
    {
        if (State != ThrowState.Result) return;
        elapsed += Time.deltaTime;
        if (elapsed >= resultDuration) ResetAttempt();
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
        // 바닥에 안정된 망의 중심을 놀이판 면으로 내려 판정한다. Collider 높이나 놓인 방향에 따라 결과가 바뀌지 않게 한다.
        bool onTile = map.TryGetRegion(map.ProjectToSurface(stoneCollider.bounds.center), out Map.Region region)
            && region >= Map.Region.Tile1 && region <= Map.Region.Tile8;
        bool success = !timedOut && !touchedBorder && onTile
            && (IsClaiming || (int)region == targetNumber);
        LandedTile = success ? (int)region : 0;
        FreezeStone();
        elapsed = 0f;
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
        if (InLayers(other, groundLayers))
        {
            groundContacts.Add(other);
            if (!landed) DampLanding();
        }
        if (InLayers(other, borderLayers)) touchedBorder = true;
    }

    // 던진 속도가 그대로 남으면 착지 후 미끄러져 목표 칸을 벗어나므로 첫 착지에서 한 번만 줄인다.
    private void DampLanding()
    {
        landed = true;
        Vector3 up = map.transform.up;
        Vector3 velocity = stoneBody.linearVelocity;
        Vector3 vertical = Vector3.Project(velocity, up);
        stoneBody.linearVelocity = vertical + (velocity - vertical) * landingSlideKeep;
        stoneBody.angularVelocity *= landingSlideKeep;
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

    // Interactable을 끄면 XRI의 놓기 처리(던지는 속도 적용)가 생략되므로 Interaction Layer로만 잡기를 막는다.
    private void ApplyGrabPermission()
    {
        if (grabInteractable == null) return;
        if (!capturedGrabLayers)
        {
            originalGrabLayers = grabInteractable.interactionLayers;
            capturedGrabLayers = true;
        }
        bool allowed = State == ThrowState.Ready || State == ThrowState.Holding;
        grabInteractable.interactionLayers = allowed ? originalGrabLayers : (InteractionLayerMask)0;
    }

    private void CancelHolding()
    {
        if (grabInteractable == null || !grabInteractable.isSelected || grabInteractable.interactionManager == null) return;
        grabInteractable.interactionManager.CancelInteractableSelection((IXRSelectInteractable)grabInteractable);
    }

    private void OnDisable()
    {
        StopThrowing();
        if (subscribedGrab != null)
        {
            subscribedGrab.selectEntered.RemoveListener(HandleGrabbed);
            subscribedGrab.selectExited.RemoveListener(HandleReleased);
            if (capturedGrabLayers) subscribedGrab.interactionLayers = originalGrabLayers;
        }
        subscribedGrab = null;
        capturedGrabLayers = false;
    }

    public void StopThrowing()
    {
        // 먼저 Idle로 바꿔 취소로 인한 놓기 알림이 재시도로 처리되지 않게 한다.
        SetState(ThrowState.Idle);
        CancelHolding();
        FreezeStone();
        IsClaiming = false;
        LandedTile = 0;
        uiManager?.ClearResult();
    }

    private void SetState(ThrowState next)
    {
        if (State == next) return;
        State = next;
        ApplyGrabPermission();
        StateChanged?.Invoke(next);
    }
}
