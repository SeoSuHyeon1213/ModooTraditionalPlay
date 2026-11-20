// 사방치기의 시작과 전체 상태를 관리하고 투척 상태 변경을 연결한다.
using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine.Serialization;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class SabangGameManager : MonoBehaviour
{
    public enum GameState
    {
        Ready, SelectingPosition, SelectingPower, Throwing, MovingForward,
        Turning, Returning, PickingUpStone, RoundComplete, ClaimingLand, Cleared
    }

    [SerializeField] private SabangStone stone;
    [Tooltip("선택 사항. 투척 성공 후 전진 이동 QTE를 진행한다. 비우면 이동 없이 Temporary Round Completion에 따라 진행한다.")]
    [SerializeField] private SabangHopQte hopQte;
    [Tooltip("투척 성공 후 전진 단계 시작 알림. 사운드·연출 등 추가 반응만 연결한다.")]
    [SerializeField] private UnityEvent onMovingForward = new UnityEvent();
    [Tooltip("일반 이동·회전·텔레포트 입력 컴포넌트만 연결. XR Origin, 입력 관리자, 추적, QTE 이동 컴포넌트는 제외.")]
    [SerializeField] private Behaviour[] manualMovementComponents = new Behaviour[0];
    [Tooltip("게임 중 UI 클릭을 막을 로비·UI용 Ray Interactor. 조준 전용 Ray는 제외한다.")]
    [SerializeField] private XRRayInteractor[] uiRayInteractors = new XRRayInteractor[0];
    [Tooltip("게임 중 UI 클릭을 막을 좌우 Near-Far Interactor. 추적과 Input Action Manager는 유지한다.")]
    [SerializeField] private NearFarInteractor[] uiNearFarInteractors = new NearFarInteractor[0];
    [Tooltip("게임 중 직접 누르기 UI 입력을 막을 좌우 Poke Interactor.")]
    [SerializeField] private XRPokeInteractor[] uiPokeInteractors = new XRPokeInteractor[0];
    [Header("프로토타입 라운드 진행")]
    [Tooltip("회전·복귀·망 줍기 구현 전 임시 연결. Hop Qte가 있으면 전진 QTE를 마친 뒤 Start Spawn Point로 돌아가 라운드를 완료하고, 없으면 투척 성공 후 바로 완료한다.")]
    [FormerlySerializedAs("completeRoundOnThrowSuccess")]
    [SerializeField] private bool temporaryRoundCompletion = true;
    [Tooltip("투척 성공 결과를 보여 준 뒤 전진 QTE(또는 임시 라운드 완료)를 시작하기까지의 시간(초)")]
    [SerializeField, Min(0f)] private float nextRoundDelay = 1.5f;
    [Tooltip("이 번호의 라운드까지 성공하면 땅따먹기로 넘어간다. 기본은 8이며, 테스트 때 줄여서 땅따먹기를 빨리 확인할 수 있다.")]
    [SerializeField, Range(1, 8)] private int lastTargetNumber = 8;

    [Header("땅따먹기와 클리어")]
    [Tooltip("자기 땅으로 얻은 칸을 표시한다. 비우면 표시 없이 클리어만 처리한다.")]
    [SerializeField] private SabangLandMarker landMarker;
    [Tooltip("선택 사항. 땅따먹기 시작 시 플레이어를 옮길 1번 칸 앞 위치. 파란 축(forward)이 놀이판을 향하게 둔다. 비우면 현재 위치에서 진행한다.")]
    [SerializeField] private Transform claimSpawnPoint;
    [Tooltip("선택 사항. 다시 하기 때 플레이어를 돌려보낼 게임 시작 위치. UIDoor의 Spawn Point와 같은 오브젝트를 연결한다.")]
    [SerializeField] private Transform startSpawnPoint;
    [Tooltip("Claim Spawn Point나 Start Spawn Point를 쓸 때 플레이어를 옮길 XR Origin.")]
    [SerializeField] private XROrigin xrOrigin;

    public GameState State { get; private set; } = GameState.Ready;
    public int CurrentTargetNumber { get; private set; } = 1;
    // 게임 시작부터 클리어까지의 시간(초). 클리어 후에는 멈춘다.
    public float PlaySeconds => clearedPlaySeconds >= 0f ? clearedPlaySeconds : (gameStartedAt >= 0f ? Time.time - gameStartedAt : 0f);
    // 라운드 투척, 이동 QTE, 땅따먹기 투척의 전체 실패 횟수.
    public int TotalFailures => (stone != null ? stone.FailureCount : 0) + (hopQte != null ? hopQte.FailureCount : 0);
    // 땅따먹기로 얻은 칸 번호. 클리어 전에는 0이다.
    public int ClaimedTile => claimedTile;
    public event System.Action<GameState> StateChanged;

    private SabangStone subscribedStone;
    private SabangHopQte subscribedHopQte;
    private bool starting;
    private float gameStartedAt = -1f, clearedPlaySeconds = -1f;
    private int claimedTile;
    private Coroutine pendingForwardStep;
    private readonly Dictionary<Behaviour, bool> movementStates = new Dictionary<Behaviour, bool>();
    private readonly Dictionary<XRRayInteractor, bool> rayUIStates = new Dictionary<XRRayInteractor, bool>();
    private readonly Dictionary<NearFarInteractor, bool> nearFarUIStates = new Dictionary<NearFarInteractor, bool>();
    private readonly Dictionary<XRPokeInteractor, bool> pokeUIStates = new Dictionary<XRPokeInteractor, bool>();

    private void OnEnable()
    {
        subscribedStone = stone;
        if (subscribedStone != null) subscribedStone.StateChanged += HandleThrowState;
        subscribedHopQte = hopQte;
        if (subscribedHopQte != null) subscribedHopQte.Finished += HandleForwardFinished;
    }

    public bool CanStartGame()
    {
        if (!isActiveAndEnabled || starting || State != GameState.Ready) return false;
        if (stone == null || subscribedStone != stone || stone.StartsAutomatically)
        {
            Debug.LogError("SabangGameManager: Stone을 연결하고 Start Automatically를 끈 뒤 실행하세요.", this);
            return false;
        }
        // 자기 땅 표시는 선택 사항이지만, 연결했다면 클리어 시점이 아니라 시작할 때 설정 오류를 알린다.
        if (landMarker != null && !landMarker.ValidateConfiguration()) return false;
        if (hopQte != null)
        {
            if (subscribedHopQte != hopQte || !hopQte.ValidateConfiguration()) return false;
            if (temporaryRoundCompletion && startSpawnPoint == null)
                Debug.LogWarning("SabangGameManager: 전진 QTE 뒤 임시 라운드 완료에서 플레이어를 되돌리려면 Start Spawn Point와 XR Origin을 연결하세요.", this);
        }
        return stone.CanBeginRound(1) && stone.State == SabangStone.ThrowState.Idle;
    }

    // UIDoor가 SpawnPoint 이동 성공을 확인한 뒤 호출한다.
    public bool TryStartGame()
    {
        if (!CanStartGame()) return false;
        starting = true;
        CurrentTargetNumber = 1;
        bool started = false;
        try
        {
            ResetGameRecord();
            LockManualMovement();
            LockUIInput();
            started = stone.TryBeginRound(CurrentTargetNumber);
            if (started) gameStartedAt = Time.time;
            return started;
        }
        finally
        {
            starting = false;
            if (!started)
            {
                RestoreManualMovement();
                RestoreUIInput();
            }
        }
    }

    private void HandleThrowState(SabangStone.ThrowState next)
    {
        if (!starting && State == GameState.Ready) return;
        if (State == GameState.ClaimingLand)
        {
            // 땅따먹기 중에는 잡기·비행·실패 재시도를 같은 단계로 유지하고 성공만 클리어로 넘긴다.
            if (next == SabangStone.ThrowState.Complete) FinishClaim();
            else if (next == SabangStone.ThrowState.Idle) SetState(GameState.Ready);
            return;
        }
        switch (next)
        {
            // Grab 투척에서는 위치 선택 단계가 망 잡기 대기, 파워 선택 단계가 망을 들고 조준하는 단계다.
            case SabangStone.ThrowState.Ready: SetState(GameState.SelectingPosition); break;
            case SabangStone.ThrowState.Holding: SetState(GameState.SelectingPower); break;
            case SabangStone.ThrowState.Flying:
            case SabangStone.ThrowState.Result: SetState(GameState.Throwing); break;
            case SabangStone.ThrowState.Complete: SetState(GameState.MovingForward); break;
            case SabangStone.ThrowState.Idle: SetState(GameState.Ready); break;
        }
    }

    private void SetState(GameState next)
    {
        if (next == GameState.Ready || next == GameState.Cleared)
        {
            RestoreManualMovement();
            RestoreUIInput();
            CancelPendingForwardStep();
            if (hopQte != null) hopQte.Stop();
        }
        if (State == next) return;
        State = next;
        StateChanged?.Invoke(next);
        if (next != GameState.MovingForward) return;
        onMovingForward.Invoke();
        // 투척 결과 처리 중에 다음 단계를 시작하지 않도록 결과 표시 시간 뒤로 미룬다.
        if (pendingForwardStep == null && isActiveAndEnabled && (hopQte != null || temporaryRoundCompletion))
            pendingForwardStep = StartCoroutine(BeginForwardAfterDelay());
    }

    private System.Collections.IEnumerator BeginForwardAfterDelay()
    {
        yield return new WaitForSeconds(nextRoundDelay);
        pendingForwardStep = null;
        if (State != GameState.MovingForward) yield break;
        if (hopQte == null)
        {
            CompleteRound();
            yield break;
        }
        if (!hopQte.TryBeginForward(CurrentTargetNumber))
            Debug.LogError($"SabangGameManager: {CurrentTargetNumber}번 목표 라운드의 전진 QTE를 시작하지 못했습니다.", this);
    }

    private void CancelPendingForwardStep()
    {
        if (pendingForwardStep == null) return;
        StopCoroutine(pendingForwardStep);
        pendingForwardStep = null;
    }

    // 전진 QTE로 7·8번 구역에 도착했을 때. 회전·복귀·망 줍기가 생기기 전에는 임시로 시작 위치에서 라운드를 완료한다.
    private void HandleForwardFinished()
    {
        if (State != GameState.MovingForward) return;
        if (!temporaryRoundCompletion)
        {
            SetState(GameState.Turning);
            return;
        }
        MovePlayerTo(startSpawnPoint);
        CompleteRound();
    }

    // 라운드 완료 신호. 전진·복귀·망 줍기 진행 코드가 생기면 놀이판 밖으로 나온 시점에 호출한다.
    public bool CompleteRound()
    {
        if (State != GameState.MovingForward) return false;
        SetState(GameState.RoundComplete);
        if (CurrentTargetNumber >= lastTargetNumber)
        {
            BeginClaim();
            return true;
        }
        CurrentTargetNumber++;
        if (!stone.TryBeginRound(CurrentTargetNumber))
            Debug.LogError($"SabangGameManager: {CurrentTargetNumber}번 목표 라운드를 시작하지 못했습니다.", this);
        return true;
    }

    // 마지막 라운드 후 자기 땅 던지기를 시작한다. 위치 이동에 실패해도 현재 위치에서 계속 진행한다.
    private void BeginClaim()
    {
        SetState(GameState.ClaimingLand);
        MovePlayerTo(claimSpawnPoint);
        if (!stone.TryBeginClaim())
            Debug.LogError("SabangGameManager: 땅따먹기 투척을 시작하지 못했습니다.", this);
    }

    private void FinishClaim()
    {
        if (State != GameState.ClaimingLand) return;
        clearedPlaySeconds = gameStartedAt >= 0f ? Time.time - gameStartedAt : 0f;
        claimedTile = stone.LandedTile;
        if (landMarker != null) landMarker.Show(claimedTile);
        SetState(GameState.Cleared);
    }

    // 클리어 화면의 다시 하기. 목표 칸, 망 위치, 자기 땅, 시간, 실패 횟수를 초기화하고 1번 라운드부터 시작한다.
    public bool RestartGame()
    {
        if (State != GameState.Cleared) return false;
        StopGame();
        MovePlayerTo(startSpawnPoint);
        return TryStartGame();
    }

    private void ResetGameRecord()
    {
        gameStartedAt = clearedPlaySeconds = -1f;
        claimedTile = 0;
        if (landMarker != null) landMarker.Clear();
        stone.ResetFailureCount();
        if (hopQte != null) hopQte.ResetFailureCount();
    }

    private void MovePlayerTo(Transform point)
    {
        if (point == null) return;
        if (xrOrigin == null || xrOrigin.Camera == null || xrOrigin.Origin == null)
        {
            Debug.LogWarning($"SabangGameManager: '{point.name}'로 이동하려면 XR Origin을 연결하세요. 현재 위치에서 진행합니다.", this);
            return;
        }
        // 실제로 몸을 돌리지 않도록 시점 방향을 먼저 맞춘 뒤 위치를 옮긴다.
        xrOrigin.MatchOriginUpCameraForward(point.up, point.forward);
        if (!xrOrigin.MoveCameraToWorldLocation(point.position + point.up * xrOrigin.CameraInOriginSpaceHeight))
            Debug.LogWarning($"SabangGameManager: '{point.name}'로 플레이어를 옮기지 못했습니다.", this);
    }

    private void LockManualMovement()
    {
        if (movementStates.Count > 0 || manualMovementComponents == null) return;
        // 중복 참조가 있어도 비활성화 전 상태를 한 번만 저장한다.
        foreach (Behaviour component in manualMovementComponents)
        {
            if (component == null || component == this || component == stone ||
                movementStates.ContainsKey(component)) continue;
            movementStates.Add(component, component.enabled);
        }
        foreach (var entry in movementStates) entry.Key.enabled = false;
    }

    private void RestoreManualMovement()
    {
        foreach (var entry in movementStates)
        {
            if (entry.Key != null) entry.Key.enabled = entry.Value;
        }
        movementStates.Clear();
    }

    public void StopGame()
    {
        if (stone != null && State != GameState.Ready) stone.StopThrowing();
        SetState(GameState.Ready);
    }

    private void LockUIInput()
    {
        if (uiRayInteractors != null)
        {
            foreach (XRRayInteractor ray in uiRayInteractors)
            {
                if (ray == null || rayUIStates.ContainsKey(ray)) continue;
                rayUIStates.Add(ray, ray.enableUIInteraction);
                ray.enableUIInteraction = false;
            }
        }
        if (uiNearFarInteractors != null)
        {
            foreach (NearFarInteractor interactor in uiNearFarInteractors)
            {
                if (interactor == null || nearFarUIStates.ContainsKey(interactor)) continue;
                nearFarUIStates.Add(interactor, interactor.enableUIInteraction);
                interactor.enableUIInteraction = false;
            }
        }
        if (uiPokeInteractors != null)
        {
            foreach (XRPokeInteractor interactor in uiPokeInteractors)
            {
                if (interactor == null || pokeUIStates.ContainsKey(interactor)) continue;
                pokeUIStates.Add(interactor, interactor.enableUIInteraction);
                interactor.enableUIInteraction = false;
            }
        }
    }

    private void RestoreUIInput()
    {
        foreach (var entry in rayUIStates)
        {
            if (entry.Key != null) entry.Key.enableUIInteraction = entry.Value;
        }
        foreach (var entry in nearFarUIStates)
        {
            if (entry.Key != null) entry.Key.enableUIInteraction = entry.Value;
        }
        foreach (var entry in pokeUIStates)
        {
            if (entry.Key != null) entry.Key.enableUIInteraction = entry.Value;
        }
        rayUIStates.Clear();
        nearFarUIStates.Clear();
        pokeUIStates.Clear();
    }

    private void OnDisable()
    {
        if (subscribedStone != null)
        {
            subscribedStone.StateChanged -= HandleThrowState;
            if (State != GameState.Ready) subscribedStone.StopThrowing();
        }
        subscribedStone = null;
        if (subscribedHopQte != null) subscribedHopQte.Finished -= HandleForwardFinished;
        subscribedHopQte = null;
        starting = false;
        SetState(GameState.Ready);
    }
}
