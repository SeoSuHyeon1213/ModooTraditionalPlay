// 사방치기 HUD의 게이지, 성공 구간과 결과 문구를 표시하며 배치와 형태는 에디터 설정을 유지한다.
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    [Header("에디터에서 배치한 플레이 HUD")]
    [SerializeField] private GameObject gaugeRoot;
    [SerializeField] private Slider powerSlider;
    [Tooltip("게이지 전체 폭을 가진 부모 아래의 성공 구간. 게임 값에 따라 Slider의 진행 축 범위만 변경한다.")]
    [SerializeField] private RectTransform successBand;
    [Tooltip("Gauge Root 바깥에 배치한다. 성공 시 게이지를 숨겨도 결과는 표시되어야 한다.")]
    [SerializeField] private TMP_Text resultText;

    [Header("투척 결과 문구")]
    [SerializeField] private string successMessage = "성공";
    [SerializeField] private string failureMessage = "실패";
    [SerializeField] private SabangGameManager gameManager;
    public GameObject explainText;
    public GameObject startText;
    public GameObject nextButton;
    public GameObject startButton;


    // 컴포넌트 간 이동으로 사라질 수 있는 기존 씬 참조만 보완한다. 새 Inspector 연결을 우선한다.
    internal void PreserveExistingReferences(GameObject root, Slider slider, RectTransform band, TMP_Text text)
    {
        if (gaugeRoot == null) gaugeRoot = root;
        if (powerSlider == null) powerSlider = slider;
        if (successBand == null) successBand = band;
        if (resultText == null) resultText = text;
    }

    public bool ValidateConfiguration(Transform stone)
    {
        if (!isActiveAndEnabled) return ConfigurationError("UIManager를 활성화하세요.");
        if (gaugeRoot == null || powerSlider == null || successBand == null || resultText == null)
            return ConfigurationError("Gauge Root, Power Slider, Success Band, Result Text를 연결하세요.");
        if (transform.IsChildOf(gaugeRoot.transform)
            || (stone != null && stone.IsChildOf(gaugeRoot.transform))
            || resultText.transform.IsChildOf(gaugeRoot.transform))
            return ConfigurationError("UIManager, 망과 Result Text는 꺼지는 Gauge Root 바깥에 배치하세요.");
        if (!powerSlider.transform.IsChildOf(gaugeRoot.transform)
            || !successBand.IsChildOf(gaugeRoot.transform))
            return ConfigurationError("Power Slider와 Success Band는 Gauge Root 안에 배치하세요.");
        if (powerSlider.wholeNumbers || Mathf.Approximately(powerSlider.minValue, powerSlider.maxValue))
            return ConfigurationError("Power Slider의 Whole Numbers를 끄고 Min Value와 Max Value를 다르게 지정하세요.");
        return true;
    }

    private bool ConfigurationError(string message)
    {
        Debug.LogError("UIManager: " + message, this);
        return false;
    }

    public void ShowGauge(float requiredPower, float tolerance)
    {
        SetSuccessRange(requiredPower, tolerance);
        SetPower(0f);
        if (gaugeRoot != null && !gaugeRoot.activeSelf) gaugeRoot.SetActive(true);
    }

    public void HideGauge()
    {
        if (gaugeRoot != null && gaugeRoot.activeSelf) gaugeRoot.SetActive(false);
    }

    public void SetPower(float power)
    {
        if (powerSlider == null) return;
        float value = Mathf.Lerp(powerSlider.minValue, powerSlider.maxValue, Mathf.Clamp01(power / 100f));
        // 표시 갱신을 입력 이벤트로 전달하지 않는다. Inspector의 Slider 범위도 유지한다.
        powerSlider.SetValueWithoutNotify(value);
    }

    private void SetSuccessRange(float requiredPower, float tolerance)
    {
        if (successBand == null || powerSlider == null) return;
        float required = Mathf.Clamp(requiredPower, 0f, 100f);
        float margin = Mathf.Max(0f, tolerance);
        float lower = Mathf.Clamp01((required - margin) / 100f);
        float upper = Mathf.Clamp01((required + margin) / 100f);
        Slider.Direction direction = powerSlider.direction;
        bool horizontal = direction == Slider.Direction.LeftToRight || direction == Slider.Direction.RightToLeft;
        bool reversed = direction == Slider.Direction.RightToLeft || direction == Slider.Direction.TopToBottom;
        int axis = horizontal ? 0 : 1;
        Vector2 min = successBand.anchorMin, max = successBand.anchorMax;
        min[axis] = reversed ? 1f - upper : lower;
        max[axis] = reversed ? 1f - lower : upper;
        successBand.anchorMin = min;
        successBand.anchorMax = max;
        Vector2 low = successBand.offsetMin, high = successBand.offsetMax;
        low[axis] = high[axis] = 0f;
        successBand.offsetMin = low;
        successBand.offsetMax = high;
    }

    public void ShowThrowResult(bool success)
    {
        SetResult(success ? successMessage : failureMessage);
    }

    public void ClearResult()
    {
        SetResult("");
    }

    public void ResetPresentation()
    {
        HideGauge();
        ClearResult();
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
