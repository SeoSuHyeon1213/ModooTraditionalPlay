// 사방치기 전진 이동 QTE를 진행한다. 칸 속성에 맞는 한 손·양손 Trigger 입력을 판정하고 성공하면 다음 착지 지점으로 플레이어를 옮긴다.
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;

public class SabangHopQte : MonoBehaviour
{
    // 외발 칸은 EitherHand, 망 때문에 나란한 칸 한쪽만 밟으면 그 칸 쪽 손, 양발 칸은 BothHands다.
    public enum HopInput { EitherHand, LeftHand, RightHand, BothHands }
    // Waiting은 기준점 대기, Confirming은 첫 입력 뒤 다른 손 입력을 확인하는 상태, Failed는 실패 표시 후 재시작 대기다.
    public enum HopState { Idle, Waiting, Confirming, Hopping, Failed, Finished }

    [Header("직접 배치한 참조")]
    [SerializeField] private Map map;
    [Tooltip("QTE 성공 시 옮길 XR Origin.")]
    [SerializeField] private XROrigin xrOrigin;
    [SerializeField] private UIManager uiManager;
    [Tooltip("왼손 Trigger Action. XRI Default Input Actions의 XRI Left Interaction/Activate를 연결하고 XR Origin의 Input Action Manager로 활성화한다.")]
    [SerializeField] private InputActionReference leftTrigger;
    [Tooltip("오른손 Trigger Action. XRI Default Input Actions의 XRI Right Interaction/Activate를 연결한다.")]
    [SerializeField] private InputActionReference rightTrigger;
    [Tooltip("1~8번 칸의 착지 지점(배열 0번이 1번 칸). 비운 칸은 놀이판 칸 모양의 중심을 쓴다. 나란한 칸을 양발로 밟으면 두 지점의 가운데에 선다. XR Origin의 자식으로 두지 않는다.")]
    [SerializeField] private Transform[] landingPoints = new Transform[8];

    [Header("QTE 판정")]
    [Tooltip("QTE 표시가 나타난 뒤 기준점에 들어오기까지의 시간(초)")]
    [SerializeField, Min(0.3f)] private float approachDuration = 1.2f;
    [Tooltip("기준점 앞뒤로 성공을 인정하는 시간(초). 규칙 기본값은 ±0.25초다.")]
    [SerializeField, Min(0.05f)] private float successWindow = 0.25f;
    [Tooltip("양손 입력을 동시로 인정하는 시간 차이(초). 외발 칸에서는 이 시간 안에 다른 손도 눌리면 실패다.")]
    [SerializeField, Min(0.05f)] private float simultaneousWindow = 0.2f;
    [Tooltip("실패 문구를 보여 준 뒤 같은 칸의 QTE를 다시 시작하기까지의 시간(초)")]
    [SerializeField, Min(0.1f)] private float failureDelay = 0.8f;

    [Header("이동 연출")]
    [Tooltip("QTE 성공 후 다음 착지 지점까지 이동하는 시간(초). 0이면 바로 옮긴다. 멀미가 나면 줄인다.")]
    [SerializeField, Min(0f)] private float hopDuration = 0.3f;

    public HopState State { get; private set; }
    // 게임 전체에서 누적한 이동 QTE 실패 횟수. 새 게임 시작 때만 초기화한다.
    public int FailureCount { get; private set; }
    // 마지막 착지 지점까지 이동을 마치면 한 번 알린다.
    public event System.Action Finished;

    private struct HopStep
    {
        public int Tile;        // 외발로 딛는 칸, 양발이면 왼쪽 칸.
        public int PairedTile;  // 양발로 함께 딛는 오른쪽 칸. 외발이면 0이다.
        public HopInput Input;
    }

    // 전진 순서의 구역. 놀이판(Map) 기준으로 3·6번은 외발 칸, 1·2번·4·5번·7·8번은 나란한 양발 칸이다.
    // 두 칸 구역은 놀이판을 바라볼 때 왼쪽 칸, 오른쪽 칸 순서다.
    private static readonly int[][] ForwardZones =
    {
        new[] { 1, 2 }, new[] { 3 }, new[] { 4, 5 }, new[] { 6 }, new[] { 7, 8 }
    };

    private readonly List<HopStep> steps = new List<HopStep>();
    private readonly List<Vector3> outline = new List<Vector3>();
    private int stepIndex;
    private float beatTime, firstPressTime, failedUntil, hopElapsed;
    private bool firstPressLeft;
    private Vector3 hopFrom, hopTo;

    public bool ValidateConfiguration()
    {
        if (!isActiveAndEnabled) return ConfigurationError("SabangHopQte를 활성화하세요.");
        if (map == null || !map.isActiveAndEnabled) return ConfigurationError("활성화된 Map을 연결하세요.");
        if (xrOrigin == null || xrOrigin.Origin == null || xrOrigin.Camera == null)
            return ConfigurationError("Origin과 Camera가 설정된 XR Origin을 연결하세요.");
        if (uiManager == null) return ConfigurationError("UI Manager를 연결하세요.");
        if (!uiManager.ValidateQteConfiguration()) return false;
        if (!IsEnabled(leftTrigger) || !IsEnabled(rightTrigger))
            return ConfigurationError("Left Trigger·Right Trigger에 XRI Left/Right Interaction의 Activate를 연결하고 XR Origin의 Input Action Manager로 활성화하세요.");
        if (leftTrigger.action == rightTrigger.action)
            return ConfigurationError("Left Trigger와 Right Trigger에 서로 다른 손의 Action을 연결하세요.");
        if (landingPoints != null)
        {
            foreach (Transform point in landingPoints)
            {
                if (point != null && point.IsChildOf(xrOrigin.transform))
                    return ConfigurationError($"Landing Point '{point.name}'를 XR Origin 밖에 두세요. 플레이어와 함께 움직이면 착지 위치가 어긋납니다.");
            }
        }
        return true;
    }

    private bool ConfigurationError(string message)
    {
        Debug.LogError("SabangHopQte: " + message, this);
        return false;
    }

    private static bool IsEnabled(InputActionReference reference)
    {
        return reference != null && reference.action != null && reference.action.enabled;
    }

    private static bool WasPressed(InputActionReference reference)
    {
        return reference != null && reference.action != null && reference.action.WasPressedThisFrame();
    }

    // 투척 성공 후 호출한다. 목표 칸을 제외하고 1·2번 구역부터 7·8번 구역까지 전진한다.
    public bool TryBeginForward(int targetNumber)
    {
        if (State != HopState.Idle && State != HopState.Finished) return false;
        if (targetNumber < 1 || targetNumber > 8) return ConfigurationError("목표 번호는 1~8이어야 합니다.");
        if (!ValidateConfiguration()) return false;
        BuildForwardSteps(targetNumber);
        stepIndex = 0;
        BeginStep();
        return true;
    }

    public void ResetFailureCount()
    {
        FailureCount = 0;
    }

    // 게임 중단·초기화 때 호출한다. 진행 중인 판정과 표시만 멈추고 플레이어 위치는 호출한 쪽이 정한다.
    public void Stop()
    {
        if (State == HopState.Idle) return;
        SetState(HopState.Idle);
        if (uiManager != null) uiManager.HideQte();
    }

    private void BuildForwardSteps(int target)
    {
        steps.Clear();
        foreach (int[] zone in ForwardZones)
        {
            if (zone.Length == 1)
            {
                // 망이 있는 외발 칸은 칸 전체를 건너뛴다.
                if (zone[0] != target) steps.Add(new HopStep { Tile = zone[0], Input = HopInput.EitherHand });
                continue;
            }
            int left = zone[0], right = zone[1];
            // 나란한 칸 중 망이 있는 칸은 밟지 않고, 비어 있는 칸 쪽 손으로 외발 착지한다.
            if (target == left) steps.Add(new HopStep { Tile = right, Input = HopInput.RightHand });
            else if (target == right) steps.Add(new HopStep { Tile = left, Input = HopInput.LeftHand });
            else steps.Add(new HopStep { Tile = left, PairedTile = right, Input = HopInput.BothHands });
        }
    }

    private void BeginStep()
    {
        HopStep step = steps[stepIndex];
        beatTime = Time.time + approachDuration;
        string tiles = step.PairedTile > 0 ? $"{step.Tile}·{step.PairedTile}번" : $"{step.Tile}번";
        uiManager.ShowQte(tiles, step.Input);
        SetState(HopState.Waiting);
    }

    private void Update()
    {
        switch (State)
        {
            case HopState.Waiting: UpdateWaiting(); break;
            case HopState.Confirming: UpdateConfirming(); break;
            case HopState.Hopping: UpdateHop(); break;
            case HopState.Failed:
                if (Time.time >= failedUntil) BeginStep();
                break;
        }
    }

    private void UpdateWaiting()
    {
        float now = Time.time;
        uiManager.SetQteApproach(1f - (beatTime - now) / approachDuration);
        bool left = WasPressed(leftTrigger), right = WasPressed(rightTrigger);
        if (!left && !right)
        {
            if (now > beatTime + successWindow) Fail();
            return;
        }
        // 기준점 앞뒤 성공 구간 밖의 입력은 실패다.
        if (Mathf.Abs(now - beatTime) > successWindow)
        {
            Fail();
            return;
        }
        HopInput input = steps[stepIndex].Input;
        if (left && right)
        {
            if (input == HopInput.BothHands) Succeed();
            else Fail();
            return;
        }
        if ((input == HopInput.LeftHand && !left) || (input == HopInput.RightHand && !right))
        {
            Fail();
            return;
        }
        firstPressTime = now;
        firstPressLeft = left;
        SetState(HopState.Confirming);
    }

    private void UpdateConfirming()
    {
        float now = Time.time;
        uiManager.SetQteApproach(1f - (beatTime - now) / approachDuration);
        bool otherPressed = firstPressLeft ? WasPressed(rightTrigger) : WasPressed(leftTrigger);
        bool withinPair = now - firstPressTime <= simultaneousWindow;
        if (steps[stepIndex].Input == HopInput.BothHands)
        {
            // 양발 칸은 두 번째 손도 성공 구간 안에서, 첫 입력과 동시 인정 시간 안에 눌러야 한다.
            bool inWindow = now <= beatTime + successWindow;
            if (otherPressed && withinPair && inWindow) Succeed();
            else if (otherPressed || !withinPair || !inWindow) Fail();
            return;
        }
        // 외발 칸은 동시 인정 시간 안에 다른 손도 눌리면 양발로 보고 실패 처리한다.
        if (otherPressed && withinPair) Fail();
        else if (!withinPair) Succeed();
    }

    private void Fail()
    {
        // 상태가 바뀐 뒤에는 같은 시도에서 다시 실패를 세지 않는다.
        if (State != HopState.Waiting && State != HopState.Confirming) return;
        FailureCount++;
        failedUntil = Time.time + failureDelay;
        uiManager.ShowQteFailure();
        SetState(HopState.Failed);
    }

    private void Succeed()
    {
        if (State != HopState.Waiting && State != HopState.Confirming) return;
        uiManager.HideQte();
        // MoveCameraToWorldLocation과 같은 방식으로, 머리가 착지 지점 위에 오도록 Origin의 목표 위치를 구한다.
        Transform origin = xrOrigin.Origin.transform;
        Vector3 desiredCamera = LandingPosition(steps[stepIndex]) + origin.up * xrOrigin.CameraInOriginSpaceHeight;
        hopFrom = origin.position;
        hopTo = hopFrom + (desiredCamera - xrOrigin.Camera.transform.position);
        hopElapsed = 0f;
        SetState(HopState.Hopping);
        if (hopDuration <= 0f) FinishHop();
    }

    private void UpdateHop()
    {
        hopElapsed += Time.deltaTime;
        float t = Mathf.Clamp01(hopElapsed / hopDuration);
        xrOrigin.Origin.transform.position = Vector3.Lerp(hopFrom, hopTo, Mathf.SmoothStep(0f, 1f, t));
        if (t >= 1f) FinishHop();
    }

    private void FinishHop()
    {
        if (State != HopState.Hopping) return;
        xrOrigin.Origin.transform.position = hopTo;
        stepIndex++;
        if (stepIndex < steps.Count)
        {
            BeginStep();
            return;
        }
        uiManager.ClearResult();
        SetState(HopState.Finished);
        Finished?.Invoke();
    }

    private Vector3 LandingPosition(HopStep step)
    {
        Vector3 position = TileLandingPosition(step.Tile);
        return step.PairedTile > 0 ? (position + TileLandingPosition(step.PairedTile)) * 0.5f : position;
    }

    private Vector3 TileLandingPosition(int tile)
    {
        int index = tile - 1;
        if (landingPoints != null && index < landingPoints.Length && landingPoints[index] != null)
            return landingPoints[index].position;
        // 지정하지 않은 칸은 칸 꼭짓점의 평균(놀이판 표면 높이)을 쓴다. 1~8번 칸은 볼록 다각형이라 칸 안에 들어온다.
        map.TryGetRegionOutline((Map.Region)tile, outline);
        Vector3 sum = Vector3.zero;
        foreach (Vector3 point in outline) sum += point;
        return outline.Count > 0 ? sum / outline.Count : map.transform.position;
    }

    private void SetState(HopState next)
    {
        State = next;
    }

    private void OnDisable()
    {
        Stop();
    }
}
