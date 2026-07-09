using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class CalibrateSceneController : MonoBehaviour
{
    private const float TrackWidth = 520f;
    private const float TrackHeight = 72f;
    private const float HandleSize = 42f;

    private TouchCalibrationSettings.Values values;
    private InputField scaleInput;
    private InputField rawVerticalInput;
    private InputField verticalCmInput;
    private InputField rawHorizontalInput;
    private InputField horizontalCmInput;
    private Text statusText;
    private Sprite circleSprite;

    private void Start()
    {
        values = TouchCalibrationSettings.Load();
        EnsureCamera();
        EnsureEventSystem();
        BuildUi();
        RefreshInputs();
    }

    public void SetHorizontalRaw(float rawDistance)
    {
        ApplyInputsToValues();
        values.RawHorizontalDistance = Mathf.Max(rawDistance, 1f);
        SaveValues("Horizontal calibration saved.");
    }

    public void SetVerticalRaw(float rawDistance)
    {
        ApplyInputsToValues();
        values.RawVerticalDistance = Mathf.Max(rawDistance, 1f);
        SaveValues("Vertical calibration saved.");
    }

    private void BuildUi()
    {
        GameObject canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform root = canvasObject.GetComponent<RectTransform>();

        Text title = CreateText("Title", root, "CALIBRATE", 36, TextAnchor.MiddleCenter, Color.white);
        Anchor(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -46f), new Vector2(360f, 54f));

        Text hint = CreateText("Hint", root, "Drag the circle from left to right, then top to bottom. Edit cm/scale if needed.", 18, TextAnchor.MiddleCenter, new Color(0.9f, 0.9f, 0.9f));
        Anchor(hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -92f), new Vector2(720f, 34f));

        RectTransform horizontalTrack = CreateTrack(root, "Horizontal Track", new Vector2(0f, 125f), true);
        CreateDragHandle(horizontalTrack, true);

        RectTransform verticalTrack = CreateTrack(root, "Vertical Track", new Vector2(0f, -10f), false);
        CreateDragHandle(verticalTrack, false);

        RectTransform fields = CreatePanel(root, "Fields", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 105f), new Vector2(760f, 170f), new Color(0f, 0f, 0f, 0.2f));
        GridLayoutGroup grid = fields.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(370f, 32f);
        grid.spacing = new Vector2(12f, 10f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;

        scaleInput = CreateLabeledInput(fields, "Scale", TouchCalibrationSettings.DefaultScaleResearch);
        rawHorizontalInput = CreateLabeledInput(fields, "Raw Horizontal", TouchCalibrationSettings.DefaultRawHorizontalDistance);
        horizontalCmInput = CreateLabeledInput(fields, "Horizontal Cm", TouchCalibrationSettings.DefaultTouchPadHorizontalCmDistance);
        rawVerticalInput = CreateLabeledInput(fields, "Raw Vertical", TouchCalibrationSettings.DefaultRawVerticalDistance);
        verticalCmInput = CreateLabeledInput(fields, "Vertical Cm", TouchCalibrationSettings.DefaultTouchPadVerticalCmDistance);

        RectTransform buttons = CreatePanel(root, "Buttons", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 36f), new Vector2(560f, 46f), new Color(0f, 0f, 0f, 0f));
        HorizontalLayoutGroup layout = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 12f;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        CreateButton(buttons, "Save", () =>
        {
            ApplyInputsToValues();
            SaveValues("Calibration saved.");
        });
        CreateButton(buttons, "Reset", () =>
        {
            TouchCalibrationSettings.ResetToDefaults();
            values = TouchCalibrationSettings.Defaults;
            RefreshInputs();
            SetStatus("Reset to default values.");
        });
        CreateButton(buttons, "Menu", () => SceneManager.LoadScene("Menu", LoadSceneMode.Single));

        statusText = CreateText("Status", root, string.Empty, 18, TextAnchor.MiddleCenter, new Color(0.85f, 0.95f, 1f));
        Anchor(statusText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 15f), new Vector2(720f, 30f));
    }

    private RectTransform CreateTrack(RectTransform parent, string name, Vector2 position, bool horizontal)
    {
        Vector2 size = horizontal ? new Vector2(TrackWidth, TrackHeight) : new Vector2(TrackHeight, TrackWidth * 0.55f);
        RectTransform track = CreatePanel(parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size, new Color(0.08f, 0.08f, 0.08f, 0.88f));

        Text label = CreateText(name + " Label", track, horizontal ? "LEFT TO RIGHT" : "TOP TO BOTTOM", 16, TextAnchor.MiddleCenter, Color.white);
        Anchor(label.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(size.x, 24f));

        Image line = CreateImage(name + " Line", track, new Color(0.4f, 0.75f, 1f, 0.75f));
        RectTransform lineRect = line.rectTransform;
        Anchor(lineRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, horizontal ? new Vector2(size.x - 72f, 6f) : new Vector2(6f, size.y - 72f));

        return track;
    }

    private void CreateDragHandle(RectTransform track, bool horizontal)
    {
        Image handle = CreateImage(horizontal ? "Horizontal Circle" : "Vertical Circle", track, new Color(1f, 0.4f, 0.18f));
        handle.sprite = GetCircleSprite();
        handle.type = Image.Type.Simple;
        RectTransform handleRect = handle.rectTransform;
        Vector2 start = horizontal ? new Vector2(-(TrackWidth * 0.5f) + 36f, 0f) : new Vector2(0f, (TrackWidth * 0.55f * 0.5f) - 36f);
        Anchor(handleRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), start, new Vector2(HandleSize, HandleSize));
        handle.gameObject.AddComponent<CalibrationDragHandle>().Initialize(this, track, horizontal);
    }

    private InputField CreateLabeledInput(RectTransform parent, string labelText, float defaultValue)
    {
        GameObject row = new GameObject(labelText, typeof(RectTransform), typeof(Image));
        row.transform.SetParent(parent, false);
        row.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);

        Text label = CreateText(labelText + " Label", row.GetComponent<RectTransform>(), labelText, 14, TextAnchor.MiddleLeft, Color.white);
        Anchor(label.rectTransform, new Vector2(0f, 0f), new Vector2(0.45f, 1f), new Vector2(8f, 0f), new Vector2(-8f, 0f));

        InputField input = CreateInput(labelText + " Input", row.GetComponent<RectTransform>(), defaultValue);
        Anchor(input.GetComponent<RectTransform>(), new Vector2(0.47f, 0.12f), new Vector2(1f, 0.88f), new Vector2(-8f, 0f), new Vector2(-8f, 0f));
        return input;
    }

    private InputField CreateInput(string name, RectTransform parent, float value)
    {
        GameObject inputObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField));
        inputObject.transform.SetParent(parent, false);
        inputObject.GetComponent<Image>().color = Color.white;

        Text text = CreateText("Text", inputObject.GetComponent<RectTransform>(), FormatFloat(value), 14, TextAnchor.MiddleLeft, Color.black);
        Anchor(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(8f, 0f), new Vector2(-8f, 0f));

        Text placeholder = CreateText("Placeholder", inputObject.GetComponent<RectTransform>(), "0", 14, TextAnchor.MiddleLeft, new Color(0f, 0f, 0f, 0.35f));
        Anchor(placeholder.rectTransform, Vector2.zero, Vector2.one, new Vector2(8f, 0f), new Vector2(-8f, 0f));

        InputField input = inputObject.GetComponent<InputField>();
        input.textComponent = text;
        input.placeholder = placeholder;
        input.contentType = InputField.ContentType.DecimalNumber;
        return input;
    }

    private Button CreateButton(RectTransform parent, string text, UnityEngine.Events.UnityAction action)
    {
        GameObject buttonObject = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        buttonObject.GetComponent<Image>().color = Color.white;
        Button button = buttonObject.GetComponent<Button>();
        button.onClick.AddListener(action);

        Text label = CreateText(text + " Label", buttonObject.GetComponent<RectTransform>(), text, 16, TextAnchor.MiddleCenter, Color.black);
        Anchor(label.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        return button;
    }

    private RectTransform CreatePanel(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, Color color)
    {
        Image image = CreateImage(name, parent, color);
        RectTransform rect = image.rectTransform;
        Anchor(rect, anchorMin, anchorMax, position, size);
        return rect;
    }

    private Image CreateImage(string name, RectTransform parent, Color color)
    {
        GameObject imageObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private Sprite GetCircleSprite()
    {
        if (circleSprite != null)
        {
            return circleSprite;
        }

        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color clear = new Color(1f, 1f, 1f, 0f);
        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float radius = (size - 2) * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                texture.SetPixel(x, y, distance <= radius ? Color.white : clear);
            }
        }

        texture.Apply();
        circleSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        return circleSprite;
    }

    private Text CreateText(string name, RectTransform parent, string text, int size, TextAnchor alignment, Color color)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textObject.transform.SetParent(parent, false);
        Text textComponent = textObject.GetComponent<Text>();
        textComponent.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        textComponent.fontSize = size;
        textComponent.alignment = alignment;
        textComponent.color = color;
        textComponent.text = text;
        return textComponent;
    }

    private void ApplyInputsToValues()
    {
        values.ScaleResearch = ParseInput(scaleInput, values.ScaleResearch);
        values.RawVerticalDistance = ParseInput(rawVerticalInput, values.RawVerticalDistance);
        values.TouchPadVerticalCmDistance = ParseInput(verticalCmInput, values.TouchPadVerticalCmDistance);
        values.RawHorizontalDistance = ParseInput(rawHorizontalInput, values.RawHorizontalDistance);
        values.TouchPadHorizontalCmDistance = ParseInput(horizontalCmInput, values.TouchPadHorizontalCmDistance);
    }

    private void SaveValues(string message)
    {
        TouchCalibrationSettings.Save(values);
        RefreshInputs();
        SetStatus(message);
    }

    private void RefreshInputs()
    {
        scaleInput.text = FormatFloat(values.ScaleResearch);
        rawVerticalInput.text = FormatFloat(values.RawVerticalDistance);
        verticalCmInput.text = FormatFloat(values.TouchPadVerticalCmDistance);
        rawHorizontalInput.text = FormatFloat(values.RawHorizontalDistance);
        horizontalCmInput.text = FormatFloat(values.TouchPadHorizontalCmDistance);
    }

    private float ParseInput(InputField input, float fallback)
    {
        float value;
        return input != null && float.TryParse(input.text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            ? value
            : fallback;
    }

    private string FormatFloat(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }

    private void EnsureCamera()
    {
        if (Camera.main != null)
        {
            return;
        }

        GameObject cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.12f, 0.12f, 0.13f);
    }

    private void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() != null)
        {
            return;
        }

        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }

    private static void Anchor(RectTransform rect, Vector2 min, Vector2 max, Vector2 position, Vector2 size)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
    }
}

public class CalibrationDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private CalibrateSceneController controller;
    private RectTransform track;
    private RectTransform rect;
    private bool horizontal;
    private Vector2 dragStart;

    public void Initialize(CalibrateSceneController owner, RectTransform parentTrack, bool isHorizontal)
    {
        controller = owner;
        track = parentTrack;
        horizontal = isHorizontal;
        rect = GetComponent<RectTransform>();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        dragStart = GetLocalPoint(eventData);
        MoveHandle(dragStart);
    }

    public void OnDrag(PointerEventData eventData)
    {
        MoveHandle(GetLocalPoint(eventData));
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        Vector2 dragEnd = GetLocalPoint(eventData);
        MoveHandle(dragEnd);

        float rawDistance = horizontal
            ? Mathf.Abs(dragEnd.x - dragStart.x)
            : Mathf.Abs(dragEnd.y - dragStart.y);

        if (horizontal)
        {
            controller.SetHorizontalRaw(rawDistance);
        }
        else
        {
            controller.SetVerticalRaw(rawDistance);
        }
    }

    private Vector2 GetLocalPoint(PointerEventData eventData)
    {
        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(track, eventData.position, eventData.pressEventCamera, out localPoint);
        return ClampToTrack(localPoint);
    }

    private Vector2 ClampToTrack(Vector2 point)
    {
        Rect trackRect = track.rect;
        float halfHandle = 21f;
        if (horizontal)
        {
            return new Vector2(Mathf.Clamp(point.x, trackRect.xMin + halfHandle, trackRect.xMax - halfHandle), 0f);
        }

        return new Vector2(0f, Mathf.Clamp(point.y, trackRect.yMin + halfHandle, trackRect.yMax - halfHandle));
    }

    private void MoveHandle(Vector2 point)
    {
        rect.anchoredPosition = ClampToTrack(point);
    }
}
