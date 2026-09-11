using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit.UI;

public class UIDoor : MonoBehaviour
{
    [Header("Player (defaults to Main Camera)")]
    [SerializeField] private Transform playerHead;
    [Tooltip("Horizontal distance from this cube, in metres.")]
    [SerializeField, Min(0.1f)] private float activationDistance = 1.5f;
    [SerializeField, Min(0.05f)] private float exitMargin = 0.3f;

    [Header("Teleport")]
    [SerializeField] private XROrigin xrOrigin;
    [SerializeField] private Transform spawnPoint;

    [Header("Display")]
    [SerializeField] private TMP_FontAsset koreanFont;
    [SerializeField] private string gameName = "사방치기";
    [SerializeField, Min(0.5f)] private float displayDistance = 1.5f;
    [SerializeField, Min(0.2f)] private float displayWidth = 1.2f;
    [Tooltip("Optional fixed display position/rotation. Its forward points away from the viewer.")]
    [SerializeField] private Transform displayAnchor;

    [Header("Connect your game start function here")]
    [SerializeField] private UnityEvent onYes = new UnityEvent();
    [SerializeField] private UnityEvent onNo = new UnityEvent();

    private Canvas display;
    private bool inside;
    private bool answered;

    private void Start()
    {
        if (xrOrigin == null)
            xrOrigin = FindFirstObjectByType<XROrigin>();
        if (spawnPoint == null)
        {
            GameObject point = GameObject.Find("SpawnPoint");
            if (point != null) spawnPoint = point.transform;
        }

        BuildDisplay();
        if (koreanFont == null)
            Debug.LogWarning("UIDoor: Assign a TMP font containing Korean characters to Korean Font.", this);
        if (FindFirstObjectByType<XRUIInputModule>() == null)
            Debug.LogWarning("UIDoor: The scene needs an EventSystem with XR UI Input Module for VR button input.", this);
    }

    private void Update()
    {
        if (playerHead == null)
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null) return;
            playerHead = mainCamera.transform;
        }

        // Horizontal proximity also works with room-scale movement and ground-level cubes.
        Vector3 offset = playerHead.position - transform.position;
        offset.y = 0f;
        float limit = inside ? activationDistance + exitMargin : activationDistance;
        bool nearby = offset.sqrMagnitude <= limit * limit;
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
        if (answered) return;
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
        display.worldCamera = playerHead.GetComponent<Camera>();
        display.gameObject.SetActive(true);
    }

    public void SelectYes()
    {
        if (!TryAcceptAnswer()) return;
        MovePlayerToSpawnPoint();
        onYes.Invoke();
    }

    private void MovePlayerToSpawnPoint()
    {
        if (xrOrigin == null || spawnPoint == null)
        {
            Debug.LogWarning("UIDoor: XR Origin or SpawnPoint is not assigned.", this);
            return;
        }

        Vector3 cameraDestination = spawnPoint.position + Vector3.up * xrOrigin.CameraInOriginSpaceHeight;
        if (!xrOrigin.MoveCameraToWorldLocation(cameraDestination))
            Debug.LogWarning("UIDoor: Could not move the XR Origin to SpawnPoint.", this);
    }

    public void SelectNo()
    {
        if (TryAcceptAnswer()) onNo.Invoke();
    }

    private bool TryAcceptAnswer()
    {
        if (!inside || answered || display == null || !display.gameObject.activeSelf) return false;
        answered = true;
        display.gameObject.SetActive(false);
        return true;
    }

    private void BuildDisplay()
    {
        GameObject root = new GameObject("Door Confirmation UI", typeof(RectTransform), typeof(Canvas));
        display = root.GetComponent<Canvas>();
        display.renderMode = RenderMode.WorldSpace;
        RectTransform rect = (RectTransform)root.transform;
        rect.sizeDelta = new Vector2(900f, 520f);
        rect.localScale = Vector3.one * (displayWidth / 900f);
        root.AddComponent<TrackedDeviceGraphicRaycaster>();
        root.AddComponent<GraphicRaycaster>();

        AddPanel("Pink Border", rect, Vector2.zero, rect.sizeDelta, new Color(1f, 0.15f, 0.7f, 0.8f));
        AddPanel("Cyan Border", rect, Vector2.zero, new Vector2(880f, 500f), new Color(0f, 1f, 1f));
        AddPanel("Dark Display", rect, Vector2.zero, new Vector2(868f, 488f), new Color(0.06f, 0.07f, 0.11f, 0.96f));
        AddText("Question", rect, new Vector2(0f, 75f), new Vector2(810f, 230f),
            "플레이 하시겠습니까?\n: " + gameName, 52f);
        AddButton("Yes", rect, new Vector2(-210f, -150f), "네", SelectYes);
        AddButton("No", rect, new Vector2(210f, -150f), "아니오", SelectNo);
        root.SetActive(false);
    }

    private RectTransform MakeRect(string objectName, Transform parent, Vector2 position, Vector2 size)
    {
        RectTransform rect = new GameObject(objectName, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    private Image AddPanel(string objectName, Transform parent, Vector2 position, Vector2 size, Color color)
    {
        Image panel = MakeRect(objectName, parent, position, size).gameObject.AddComponent<Image>();
        panel.color = color;
        panel.raycastTarget = false;
        return panel;
    }

    private void AddText(string objectName, Transform parent, Vector2 position, Vector2 size, string value, float fontSize)
    {
        TextMeshProUGUI label = MakeRect(objectName, parent, position, size).gameObject.AddComponent<TextMeshProUGUI>();
        if (koreanFont != null) label.font = koreanFont;
        label.text = value;
        label.fontSize = fontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;
    }

    private void AddButton(string objectName, Transform parent, Vector2 position, string caption, UnityAction action)
    {
        Image border = AddPanel(objectName + " Border", parent, position, new Vector2(350f, 110f), Color.cyan);
        Image face = AddPanel(objectName, border.transform, Vector2.zero, new Vector2(338f, 98f), Color.white);
        face.raycastTarget = true;
        Button button = face.gameObject.AddComponent<Button>();
        button.targetGraphic = face;
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.85f, 0.16f, 0.23f);
        colors.highlightedColor = new Color(1f, 0.35f, 0.42f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(0.5f, 0.07f, 0.15f);
        button.colors = colors;
        button.onClick.AddListener(action);
        AddText("Label", face.transform, Vector2.zero, new Vector2(320f, 90f), caption, 46f);
    }

    private void OnDisable()
    {
        if (display != null) display.gameObject.SetActive(false);
        inside = false;
        answered = false;
    }

    private void OnDestroy()
    {
        if (display != null) Destroy(display.gameObject);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, activationDistance);
    }
}
