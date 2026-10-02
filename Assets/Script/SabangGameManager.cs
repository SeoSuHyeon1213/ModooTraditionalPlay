// 사방치기의 시작과 전체 상태를 관리하고 투척 상태 변경을 연결한다.
using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class SabangGameManager : MonoBehaviour
{
    public enum GameState
    {
        Ready, SelectingPosition, SelectingPower, Throwing, MovingForward,
        Turning, Returning, PickingUpStone, RoundComplete, ClaimingLand, Cleared
    }

    [SerializeField] private SabangStone stone;
    [Tooltip("투척 성공 후 전진 시작 알림. 실제 이동은 후속 구현에서 연결한다.")]
    [SerializeField] private UnityEvent onMovingForward = new UnityEvent();
    [Tooltip("일반 이동·회전·텔레포트 입력 컴포넌트만 연결. XR Origin, 입력 관리자, 추적, QTE 이동 컴포넌트는 제외.")]
    [SerializeField] private Behaviour[] manualMovementComponents = new Behaviour[0];
    [Tooltip("게임 중 UI 클릭을 막을 로비·UI용 Ray Interactor. 조준 전용 Ray는 제외한다.")]
    [SerializeField] private XRRayInteractor[] uiRayInteractors = new XRRayInteractor[0];
    [Tooltip("게임 중 UI 클릭을 막을 좌우 Near-Far Interactor. 추적과 Input Action Manager는 유지한다.")]
    [SerializeField] private NearFarInteractor[] uiNearFarInteractors = new NearFarInteractor[0];
    [Tooltip("게임 중 직접 누르기 UI 입력을 막을 좌우 Poke Interactor.")]
    [SerializeField] private XRPokeInteractor[] uiPokeInteractors = new XRPokeInteractor[0];
    public GameState State { get; private set; } = GameState.Ready;
    public int CurrentTargetNumber { get; private set; } = 1;
    public event System.Action<GameState> StateChanged;

    private SabangStone subscribedStone;
    private bool starting;
    private readonly Dictionary<Behaviour, bool> movementStates = new Dictionary<Behaviour, bool>();
    private readonly Dictionary<XRRayInteractor, bool> rayUIStates = new Dictionary<XRRayInteractor, bool>();
    private readonly Dictionary<NearFarInteractor, bool> nearFarUIStates = new Dictionary<NearFarInteractor, bool>();
    private readonly Dictionary<XRPokeInteractor, bool> pokeUIStates = new Dictionary<XRPokeInteractor, bool>();

    private void OnEnable()
    {
        subscribedStone = stone;
        if (subscribedStone != null) subscribedStone.StateChanged += HandleThrowState;
    }

    public bool CanStartGame()
    {
        if (!isActiveAndEnabled || starting || State != GameState.Ready) return false;
        if (stone == null || subscribedStone != stone || stone.StartsAutomatically)
        {
            Debug.LogError("SabangGameManager: Stone을 연결하고 Start Automatically를 끈 뒤 실행하세요.", this);
            return false;
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
            LockManualMovement();
            LockUIInput();
            started = stone.TryBeginRound(CurrentTargetNumber);
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
        switch (next)
        {
            case SabangStone.ThrowState.Selecting: SetState(GameState.SelectingPosition); break;
            case SabangStone.ThrowState.Charging: SetState(GameState.SelectingPower); break;
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
        }
        if (State == next) return;
        State = next;
        StateChanged?.Invoke(next);
        if (next == GameState.MovingForward) onMovingForward.Invoke();
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
        starting = false;
        SetState(GameState.Ready);
    }
}
