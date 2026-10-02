// 플레이어 접근 시 게임 시작 확인 UI를 표시하고 선택 결과를 처리한다.
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit.UI;

public class UIDoor : MonoBehaviour
{
    public enum DetectionMode { Distance = 0, Trigger = 1 }

    [Header("접근 감지")]
    [SerializeField] private DetectionMode detectionMode = DetectionMode.Distance;
    [Tooltip("UIDoor와 같은 오브젝트에 둔 Trigger Collider. 해당 오브젝트에는 Kinematic Rigidbody도 추가한다.")]
    [SerializeField] private Collider triggerZone;
    [Tooltip("감지할 플레이어의 몸 Collider 또는 CharacterController. 손과 소품은 이 참조와 다르면 무시한다.")]
    [SerializeField] private Collider playerBodyCollider;
    private bool playerInTrigger;

    [Header("Player (defaults to Main Camera)")]
    [SerializeField] private Transform playerHead;
    [Tooltip("Horizontal distance from this cube, in metres.")]
    [SerializeField, Min(0.1f)] private float activationDistance = 1.5f;
    [SerializeField, Min(0.05f)] private float exitMargin = 0.3f;

    [Header("Teleport")]
    [SerializeField] private XROrigin xrOrigin;
    [SerializeField] private Transform spawnPoint;
    [Tooltip("사방치기 시작 시 연결한다. 다른 게임의 문은 비워둘 수 있다.")]
    [SerializeField] private SabangGameManager gameManager;

    [Header("Display")]
    [SerializeField] private TMP_FontAsset koreanFont;
    [SerializeField] private string gameName = "사방치기";
    [SerializeField, Min(0.5f)] private float displayDistance = 1.5f;
    [SerializeField, Min(0.2f)] private float displayWidth = 1.2f;
    [Tooltip("켜면 에디터에서 설정한 Canvas Scale을 유지한다. 끄면 Display Width로 크기를 계산한다.")]
    [SerializeField] private bool preserveEditorScale;
    [Tooltip("Optional fixed display position/rotation. Its forward points away from the viewer.")]
    [SerializeField] private Transform displayAnchor;

    [Header("배치한 확인창 프리팹 인스턴스")]
    [Tooltip("독립된 World Space Canvas 인스턴스를 연결한다. UIDoor를 이 Canvas 아래에 배치하지 않는다.")]
    [SerializeField] private Canvas display;
    [SerializeField] private TMP_Text questionText;
    [SerializeField] private Button yesButton;
    [SerializeField] private Button noButton;

    [Header("Connect your game start function here")]
    [SerializeField] private UnityEvent onYes = new UnityEvent();
    [SerializeField] private UnityEvent onNo = new UnityEvent();

    private Camera playerCamera;
    private bool inside;
    private bool answered;
    private bool displayReady;
    private Button subscribedYesButton;
    private Button subscribedNoButton;

    private void OnEnable()
    {
        if (displayReady) BindButtons();
    }

    private void Start()
    {
        if (xrOrigin == null)
            xrOrigin = FindFirstObjectByType<XROrigin>();
        if (spawnPoint == null)
        {
            GameObject point = GameObject.Find("SpawnPoint");
            if (point != null) spawnPoint = point.transform;
        }
        CachePlayerCamera();

        displayReady = ConfigureDisplay();
        if (displayReady) displayReady = ValidateDetection();
        if (displayReady) BindButtons();
        if (FindFirstObjectByType<XRUIInputModule>() == null)
        {
            Debug.LogWarning(
            "UIDoor: XRI UI를 사용하려면 EventSystem에 XR UI Input Module을 연결하고 다른 Input Module은 비활성화하세요.",
            this);
        }
    }

    private void Update()
    {
        if (!displayReady || display == null) return;
        if (gameManager != null && gameManager.State != SabangGameManager.GameState.Ready)
        {
            display.gameObject.SetActive(false);
            inside = answered = false;
            return;
        }
        if (playerHead == null)
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null) return;
            playerHead = mainCamera.transform;
            playerCamera = mainCamera;
        }

        bool nearby;
        if (detectionMode == DetectionMode.Trigger)
        {
            if (triggerZone == null || !triggerZone.enabled || !triggerZone.gameObject.activeInHierarchy
                || playerBodyCollider == null || !playerBodyCollider.enabled
                || !playerBodyCollider.gameObject.activeInHierarchy)
                playerInTrigger = false;
            nearby = playerInTrigger;
        }
        else
        {
            Vector3 offset = playerHead.position - transform.position;
            offset.y = 0f;
            float limit = inside ? activationDistance + exitMargin : activationDistance;
            nearby = offset.sqrMagnitude <= limit * limit;
        }
        if (nearby == inside) return;
        if (!nearby)
        {
            inside = false;
            answered = false;
            display.gameObject.SetActive(false);
            return;
        }

        inside = true;
        ShowDisplay();
    }

    private void ShowDisplay()
    {
        if (answered || display == null) return;
        if (displayAnchor != null)
            display.transform.SetPositionAndRotation(displayAnchor.position, displayAnchor.rotation);
        else
        {
            Vector3 forward = Vector3.ProjectOnPlane(playerHead.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();
            display.transform.SetPositionAndRotation(
                playerHead.position + forward * displayDistance,
                Quaternion.LookRotation(forward, Vector3.up));
        }
        if (playerCamera == null) CachePlayerCamera();
        display.worldCamera = playerCamera;
        display.gameObject.SetActive(true);
    }

    private void CachePlayerCamera()
    {
        if (playerHead == null && xrOrigin != null && xrOrigin.Camera != null)
            playerHead = xrOrigin.Camera.transform;
        if (playerHead != null) playerCamera = playerHead.GetComponent<Camera>();
    }

    private bool ValidateDetection()
    {
        if (detectionMode != DetectionMode.Trigger) return true;
        if (triggerZone == null || triggerZone.gameObject != gameObject || !triggerZone.isTrigger)
        {
            Debug.LogError("UIDoor: Trigger Zone에 UIDoor와 같은 오브젝트의 Collider를 연결하고 Is Trigger를 켜세요.", this);
            return false;
        }
        if (playerBodyCollider == null || playerBodyCollider == triggerZone)
        {
            Debug.LogError("UIDoor: Player Body Collider에 플레이어 몸의 Collider 또는 CharacterController를 연결하세요.", this);
            return false;
        }
        Rigidbody body = triggerZone.attachedRigidbody;
        if (body == null || body.gameObject != gameObject || !body.isKinematic || !body.detectCollisions)
        {
            Debug.LogError("UIDoor: DoorController에 Rigidbody를 추가하고 Is Kinematic과 Detect Collisions를 켜세요.", this);
            return false;
        }
        if (Physics.GetIgnoreLayerCollision(gameObject.layer, playerBodyCollider.gameObject.layer)
            || Physics.GetIgnoreCollision(triggerZone, playerBodyCollider))
        {
            Debug.LogError("UIDoor: Trigger Zone과 Player Body Collider 사이의 충돌이 무시되고 있습니다. Physics 충돌 설정을 확인하세요.", this);
            return false;
        }
        return true;
    }

    private void OnTriggerEnter(Collider other)
    {
        RegisterPlayerOverlap(other);
    }

    private void OnTriggerStay(Collider other)
    {
        // 영역 안에서 컴포넌트를 다시 켠 경우에도 다음 물리 갱신에서 감지한다.
        RegisterPlayerOverlap(other);
    }

    private void RegisterPlayerOverlap(Collider other)
    {
        if (isActiveAndEnabled && detectionMode == DetectionMode.Trigger
            && other == playerBodyCollider && triggerZone != null && triggerZone.enabled)
            playerInTrigger = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other == playerBodyCollider) playerInTrigger = false;
    }

    public void SelectYes()
    {
        if (!CanAcceptAnswer()) return;
        if (gameManager != null && !gameManager.CanStartGame()) return;
        if (!MovePlayerToSpawnPoint()) return;
        //if (gameManager != null && !gameManager.TryStartGame()) return;
        answered = true;
        display.gameObject.SetActive(false);
        onYes.Invoke();
    }

    private bool MovePlayerToSpawnPoint()
    {
        if (xrOrigin == null || spawnPoint == null || xrOrigin.Camera == null || xrOrigin.Origin == null)
        {
            Debug.LogWarning("UIDoor: XR Origin, Camera, Origin Base GameObject와 Spawn Point를 연결하세요.", this);
            return false;
        }

        Vector3 cameraDestination = spawnPoint.position + Vector3.up * xrOrigin.CameraInOriginSpaceHeight;
        if (!xrOrigin.MoveCameraToWorldLocation(cameraDestination))
        {
            Debug.LogWarning("UIDoor: Could not move the XR Origin to SpawnPoint.", this);
            return false;
        }
        return true;
    }

    public void SelectNo()
    {
        if (TryAcceptAnswer()) onNo.Invoke();
    }

    private bool TryAcceptAnswer()
    {
        if (!CanAcceptAnswer()) return false;
        answered = true;
        display.gameObject.SetActive(false);
        return true;
    }

    private bool CanAcceptAnswer()
    {
        return isActiveAndEnabled && displayReady && inside && !answered && display != null
            && display.gameObject.activeInHierarchy;
    }

    private bool ConfigureDisplay()
    {
        if (display == null || questionText == null || yesButton == null || noButton == null)
        {
            Debug.LogError("UIDoor: 확인창 프리팹을 씬에 배치하고 Display, Question Text, Yes Button, No Button을 연결하세요.", this);
            return false;
        }
        var errors = new System.Text.StringBuilder();
        if (display.gameObject == gameObject)
            errors.AppendLine("- UIDoor와 Display가 같은 오브젝트입니다. UIDoor를 표시 Canvas 바깥의 제어 오브젝트로 옮기세요.");
        else if (transform.IsChildOf(display.transform))
            errors.AppendLine("- UIDoor가 Display의 자식입니다. Canvas를 꺼도 제어 오브젝트는 활성 상태여야 합니다.");
        if (display.renderMode != RenderMode.WorldSpace)
            errors.AppendLine($"- Display의 Render Mode가 {display.renderMode}입니다. World Space로 설정하세요.");
        if (display.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
            errors.AppendLine("- Display Canvas 자체에 Tracked Device Graphic Raycaster가 없습니다. 자식 Canvas에만 추가하면 안 됩니다.");
        if (!questionText.transform.IsChildOf(display.transform))
            errors.AppendLine($"- Question Text '{HierarchyPath(questionText.transform)}'를 Display 아래에 배치하세요.");
        if (!yesButton.transform.IsChildOf(display.transform))
            errors.AppendLine($"- Yes Button '{HierarchyPath(yesButton.transform)}'를 Display 아래에 배치하세요.");
        if (!noButton.transform.IsChildOf(display.transform))
            errors.AppendLine($"- No Button '{HierarchyPath(noButton.transform)}'를 Display 아래에 배치하세요.");
        if (yesButton == noButton)
            errors.AppendLine("- Yes Button과 No Button에 같은 버튼이 연결되어 있습니다. 서로 다른 버튼을 연결하세요.");
        if (errors.Length > 0)
        {
            Debug.LogError($"UIDoor 설정 오류: 제어='{HierarchyPath(transform)}', Display='{HierarchyPath(display.transform)}'.\n{errors}", this);
            return false;
        }
        RectTransform rect = (RectTransform)display.transform;
        if (!preserveEditorScale && rect.rect.width > 0f)
            rect.localScale = Vector3.one * (displayWidth / rect.rect.width);
        if (koreanFont != null) questionText.font = koreanFont;
        questionText.text = "플레이 하시겠습니까?\n: " + gameName;
        display.gameObject.SetActive(false);
        return true;
    }

    private static string HierarchyPath(Transform target)
    {
        string path = target.name;
        while (target.parent != null)
        {
            target = target.parent;
            path = target.name + "/" + path;
        }
        return path;
    }

    private void BindButtons()
    {
        UnbindButtons();
        subscribedYesButton = yesButton;
        subscribedNoButton = noButton;
        if (subscribedYesButton != null) subscribedYesButton.onClick.AddListener(SelectYes);
        if (subscribedNoButton != null) subscribedNoButton.onClick.AddListener(SelectNo);
    }

    private void UnbindButtons()
    {
        if (subscribedYesButton != null) subscribedYesButton.onClick.RemoveListener(SelectYes);
        if (subscribedNoButton != null) subscribedNoButton.onClick.RemoveListener(SelectNo);
        subscribedYesButton = subscribedNoButton = null;
    }

    private void OnDisable()
    {
        UnbindButtons();
        if (display != null) display.gameObject.SetActive(false);
        inside = false;
        answered = false;
        playerInTrigger = false;
    }

    private void OnDestroy()
    {
        UnbindButtons();
    }

    private void OnDrawGizmosSelected()
    {
        if (detectionMode != DetectionMode.Distance) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, activationDistance);
    }
}
