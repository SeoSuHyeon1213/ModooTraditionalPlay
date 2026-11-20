// 사방치기 HUD의 목표 칸 안내, 투척 결과 문구, 이동 QTE 표시와 클리어 화면을 표시하며 배치와 형태는 에디터 설정을 유지한다.
using TMPro;
using UnityEngine;

public class UIManager : MonoBehaviour
{
    [Header("에디터에서 배치한 플레이 HUD")]
    [Tooltip("목표 칸 안내와 투척 결과를 표시한다. HMD에서 보이도록 World Space Canvas에 배치한다.")]
    [SerializeField] private TMP_Text resultText;

    [Header("투척 안내와 결과 문구")]
    [Tooltip("{0}에 목표 칸 번호가 들어간다.")]
    [SerializeField] private string targetMessageFormat = "{0}번 칸에 망을 던지세요";
    [SerializeField] private string claimMessage = "자기 땅으로 삼을 칸에 망을 던지세요";
    [SerializeField] private string successMessage = "성공";
    [SerializeField] private string failureMessage = "실패";
    [SerializeField] private SabangGameManager gameManager;
    public GameObject explainText;
    public GameObject startText;
    public GameObject nextButton;
    public GameObject startButton;

    [Header("클리어 화면")]
    [Tooltip("클리어 시 켜는 World Space 패널. 다시 하기 버튼을 이 안에 두고 OnClick에 UIManager.RetryButton을 연결한다.")]
    [SerializeField] private GameObject clearPanel;
    [SerializeField] private TMP_Text clearText;
    [Tooltip("{0}에 완료 시간(분:초), {1}에 전체 실패 횟수, {2}에 얻은 칸 번호가 들어간다.")]
    [SerializeField] private string clearMessageFormat = "클리어!\n{2}번 칸이 자기 땅이 되었습니다\n완료 시간 {0}\n실패 횟수 {1}회";

    [Header("이동 QTE")]
    [Tooltip("이동 QTE 표시 전체. 고정된 기준 원 이미지와 Approach Ring을 자식으로 두고, 기본은 꺼 둔다.")]
    [SerializeField] private GameObject qteRoot;
    [Tooltip("기준 원으로 줄어드는 원. Scale이 1이 되는 순간이 판정 기준점이므로 기준 원과 같은 크기로 만든다.")]
    [SerializeField] private RectTransform qteApproachRing;
    [Tooltip("QTE가 나타날 때 Approach Ring의 시작 Scale")]
    [SerializeField, Min(1.1f)] private float qteRingStartScale = 3f;
    [Tooltip("{0}에 딛을 칸 번호, {1}에 누를 손 안내가 들어간다.")]
    [SerializeField] private string qteMessageFormat = "{0} 칸\n{1}";
    [SerializeField] private string qteEitherHandText = "한 손 Trigger";
    [SerializeField] private string qteLeftHandText = "왼손 Trigger";
    [SerializeField] private string qteRightHandText = "오른손 Trigger";
    [SerializeField] private string qteBothHandsText = "양손 Trigger 동시에";
    [SerializeField] private string qteFailureMessage = "실패 - 다시";

    private bool reportedOverlayCanvas, reportedMissingClearUI;
    private SabangGameManager subscribedManager;

    private void OnEnable()
    {
        subscribedManager = gameManager;
        if (subscribedManager != null) subscribedManager.StateChanged += HandleGameState;
    }

    private void Start()
    {
        if (subscribedManager == null || subscribedManager.State != SabangGameManager.GameState.Cleared) HideClear();
        HideQte();
    }

    private void OnDisable()
    {
        if (subscribedManager != null) subscribedManager.StateChanged -= HandleGameState;
        subscribedManager = null;
    }

    private void HandleGameState(SabangGameManager.GameState state)
    {
        if (state == SabangGameManager.GameState.Cleared) ShowClear();
        else HideClear();
    }

    private void ShowClear()
    {
        if (clearPanel == null || clearText == null)
        {
            if (!reportedMissingClearUI)
                Debug.LogError("UIManager: 클리어 화면을 표시하려면 Clear Panel과 Clear Text를 연결하세요.", this);
            reportedMissingClearUI = true;
            return;
        }
        int seconds = Mathf.FloorToInt(subscribedManager.PlaySeconds);
        string time = $"{seconds / 60:00}:{seconds % 60:00}";
        clearText.text = string.Format(clearMessageFormat, time, subscribedManager.TotalFailures, subscribedManager.ClaimedTile);
        if (!clearPanel.activeSelf) clearPanel.SetActive(true);
    }

    private void HideClear()
    {
        if (clearPanel != null && clearPanel.activeSelf) clearPanel.SetActive(false);
    }

    // 클리어 패널의 다시 하기 버튼 OnClick에 연결한다.
    public void RetryButton()
    {
        if (gameManager == null)
        {
            Debug.LogError("UIManager: Game Manager에 SabangGameManager를 연결하세요.", this);
            return;
        }
        gameManager.RestartGame();
    }

    public bool ValidateConfiguration()
    {
        if (!isActiveAndEnabled) return ConfigurationError("UIManager를 활성화하세요.");
        if (resultText == null) return ConfigurationError("Result Text를 연결하세요.");
        Canvas canvas = resultText.canvas != null ? resultText.canvas.rootCanvas : null;
        if (!reportedOverlayCanvas && canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            // Screen Space - Overlay Canvas는 Game 창에만 그려지고 HMD에는 표시되지 않는다.
            Debug.LogWarning($"UIManager: Result Text의 Canvas '{canvas.name}'가 Screen Space - Overlay입니다. HMD에서 보이도록 World Space로 바꾸세요.", canvas);
            reportedOverlayCanvas = true;
        }
        return true;
    }

    private bool ConfigurationError(string message)
    {
        Debug.LogError("UIManager: " + message, this);
        return false;
    }

    public void ShowTargetPrompt(int targetNumber)
    {
        SetResult(string.Format(targetMessageFormat, targetNumber));
    }

    public void ShowClaimPrompt()
    {
        SetResult(claimMessage);
    }

    // 이동 QTE를 쓸 때 시작 전에 호출한다. 기준점이 보이지 않으면 타이밍을 맞출 수 없으므로 필수로 확인한다.
    public bool ValidateQteConfiguration()
    {
        if (!ValidateConfiguration()) return false;
        if (qteRoot == null || qteApproachRing == null)
            return ConfigurationError("이동 QTE를 표시하려면 QTE Root와 QTE Approach Ring을 연결하세요.");
        if (!qteApproachRing.IsChildOf(qteRoot.transform))
            return ConfigurationError("QTE Approach Ring은 QTE Root의 자식으로 두세요.");
        return true;
    }

    public void ShowQte(string tiles, SabangHopQte.HopInput input)
    {
        string hand = input == SabangHopQte.HopInput.LeftHand ? qteLeftHandText
            : input == SabangHopQte.HopInput.RightHand ? qteRightHandText
            : input == SabangHopQte.HopInput.BothHands ? qteBothHandsText
            : qteEitherHandText;
        SetResult(string.Format(qteMessageFormat, tiles, hand));
        SetQteApproach(0f);
        if (qteRoot != null && !qteRoot.activeSelf) qteRoot.SetActive(true);
    }

    // progress가 0이면 시작 크기, 1이면 기준 원과 같은 크기(판정 기준점)다. 기준점을 지나면 계속 줄어든다.
    public void SetQteApproach(float progress)
    {
        if (qteApproachRing == null) return;
        float scale = Mathf.Max(0f, Mathf.LerpUnclamped(qteRingStartScale, 1f, progress));
        qteApproachRing.localScale = new Vector3(scale, scale, 1f);
    }

    public void ShowQteFailure()
    {
        SetResult(qteFailureMessage);
        HideQte();
    }

    public void HideQte()
    {
        if (qteRoot != null && qteRoot.activeSelf) qteRoot.SetActive(false);
    }

    public void ShowThrowResult(bool success)
    {
        SetResult(success ? successMessage : failureMessage);
    }

    public void ClearResult()
    {
        SetResult("");
    }

    private void SetResult(string message)
    {
        if (resultText != null && resultText.text != message) resultText.text = message;
    }
    public void NextButton()
    {

        if (nextButton == null) return;
        nextButton.SetActive(false);
        explainText.SetActive(false);
        startText.SetActive(true);
        startButton.SetActive(true);
    }
    public void StartButton()
    {
        if (gameManager == null)
        {
            Debug.LogError("UIManager: Game Manager에 SabangGameManager를 연결하세요.", this);
            return;
        }

        // 설정 검증과 중복 시작 방지는 게임 매니저가 담당한다. 실패하면 안내를 유지한다.
        if (!gameManager.TryStartGame()) return;

        if (explainText != null) explainText.SetActive(false);
        if (nextButton != null) nextButton.SetActive(false);
        if (startText != null) startText.SetActive(false);
        if (startButton != null) startButton.SetActive(false);
    }
}
