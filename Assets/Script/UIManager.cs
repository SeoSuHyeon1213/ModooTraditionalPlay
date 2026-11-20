// 사방치기 HUD의 목표 칸 안내, 투척 결과 문구와 클리어 화면을 표시하며 배치와 형태는 에디터 설정을 유지한다.
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
