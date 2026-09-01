using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace SkinWeightTools.App
{
    /// <summary>
    /// Builds the whole runtime panel from script. UI Toolkit would need a PanelSettings
    /// asset and a UXML file committed alongside it; uGUI assembled in code keeps the tool
    /// to plain C# that regenerates itself, matching how WorkspaceSceneBuilder treats the
    /// editor scene.
    /// </summary>
    public class SkinFixerRuntimeUI : MonoBehaviour
    {
        public SkinFixerRuntimeApp app = null;
        public GenericRomAnimator romAnimator = null;

        private static readonly Color PanelColour = new Color(0.13f, 0.14f, 0.16f, 0.92f);
        private static readonly Color ButtonColour = new Color(0.24f, 0.26f, 0.30f, 1f);
        private static readonly Color AccentColour = new Color(0.20f, 0.42f, 0.62f, 1f);
        private static readonly Color TextColour = new Color(0.90f, 0.91f, 0.93f, 1f);

        private Font font = null;
        private Text statusLabel = null;
        private Text playLabel = null;
        private Text smoothLabel = null;
        private Text romLabel = null;

        private void Start()
        {
            if (app == null)
            {
                app = GetComponent<SkinFixerRuntimeApp>();
            }

            if (romAnimator == null && app != null)
            {
                romAnimator = app.romAnimator;
            }

            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            EnsureEventSystem();
            BuildPanel();

            if (app != null)
            {
                app.StatusChanged += OnStatusChanged;
                OnStatusChanged(app.Status);
            }
        }

        private void OnDestroy()
        {
            if (app != null)
            {
                app.StatusChanged -= OnStatusChanged;
            }
        }

        private void OnStatusChanged(string message)
        {
            if (statusLabel != null)
            {
                statusLabel.text = message;
            }
        }

        private void Update()
        {
            if (romAnimator != null && playLabel != null)
            {
                if (romAnimator.IsPlaying == true)
                {
                    playLabel.text = "PAUSE  (" + romAnimator.DrivenBoneCount.ToString() + " joints)";
                }
                else
                {
                    playLabel.text = "PLAY ROM  (" + romAnimator.DrivenBoneCount.ToString() + " joints)";
                }
            }
        }

        /// <summary>
        /// The project runs the new input system only, where the legacy StandaloneInputModule
        /// throws on the first click. Any EventSystem already in the scene is upgraded rather
        /// than duplicated.
        /// </summary>
        private void EnsureEventSystem()
        {
            EventSystem existing = Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);

            if (existing == null)
            {
                GameObject holder = new GameObject("EventSystem");
                holder.AddComponent<EventSystem>();
                holder.AddComponent<InputSystemUIInputModule>();
                return;
            }

            if (existing.GetComponent<InputSystemUIInputModule>() == null)
            {
                StandaloneInputModule legacy = existing.GetComponent<StandaloneInputModule>();

                if (legacy != null)
                {
                    Destroy(legacy);
                }

                existing.gameObject.AddComponent<InputSystemUIInputModule>();
            }
        }

        private void BuildPanel()
        {
            GameObject canvasObject = new GameObject("SkinFixerCanvas");
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            canvasObject.AddComponent<GraphicRaycaster>();

            GameObject panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(canvasObject.transform, false);

            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 0f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 0.5f);
            panelRect.sizeDelta = new Vector2(340f, 0f);
            panelRect.anchoredPosition = new Vector2(16f, 0f);

            Image background = panel.AddComponent<Image>();
            background.color = PanelColour;

            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 14, 14);
            layout.spacing = 7f;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childAlignment = TextAnchor.UpperCenter;

            BuildHeading(panel.transform, "SKIN FIXER");

            BuildButton(panel.transform, "LOAD MODEL...", AccentColour, OnLoadPressed);

            BuildSpacer(panel.transform, 6f);
            BuildHeading(panel.transform, "RANGE OF MOTION");

            playLabel = BuildButton(panel.transform, "PLAY ROM", ButtonColour, OnPlayPressed);

            romLabel = BuildLabel(panel.transform, "Bend 45 deg   Speed 0.35", 13, TextAnchor.MiddleLeft);

            BuildSlider(panel.transform, "Bend angle", 5f, 90f, CurrentAmplitude(), OnAmplitudeChanged);
            BuildSlider(panel.transform, "Speed", 0.05f, 2f, CurrentSpeed(), OnSpeedChanged);

            BuildSpacer(panel.transform, 6f);
            BuildHeading(panel.transform, "EVEN OUT  (re-solves from source)");

            GameObject row = BuildRow(panel.transform);
            BuildButton(row.transform, "2", ButtonColour, OnEvenOutTwo);
            BuildButton(row.transform, "3", ButtonColour, OnEvenOutThree);
            BuildButton(row.transform, "4", ButtonColour, OnEvenOutFour);

            BuildSpacer(panel.transform, 6f);
            BuildHeading(panel.transform, "SMOOTH  (stacks per press)");

            smoothLabel = BuildLabel(panel.transform, "Strength 0.50   0 passes", 13, TextAnchor.MiddleLeft);
            BuildSlider(panel.transform, "Strength", 0f, 1f, CurrentSmoothStrength(), OnSmoothStrengthChanged);

            BuildButton(panel.transform, "APPLY SMOOTH PASS", AccentColour, OnSmoothPressed);
            BuildButton(panel.transform, "CULL TO INFLUENCE BUDGET", ButtonColour, OnCullPressed);

            BuildSpacer(panel.transform, 6f);
            BuildButton(panel.transform, "REVERT TO SOURCE WEIGHTS", ButtonColour, OnRevertPressed);

            BuildSpacer(panel.transform, 10f);
            statusLabel = BuildLabel(panel.transform, "", 13, TextAnchor.UpperLeft);
            statusLabel.GetComponent<LayoutElement>().minHeight = 120f;

            // The sliders are seeded from the live components, so bring the readouts in line
            // with them rather than leaving the placeholder text from construction.
            RefreshRomLabel();
            RefreshSmoothLabel();
        }

        private float CurrentAmplitude()
        {
            if (romAnimator == null)
            {
                return 45f;
            }

            return romAnimator.amplitude;
        }

        private float CurrentSpeed()
        {
            if (romAnimator == null)
            {
                return 0.35f;
            }

            return romAnimator.speed;
        }

        private float CurrentSmoothStrength()
        {
            if (app == null)
            {
                return 0.5f;
            }

            return app.smoothStrength;
        }

        // ------------------------------------------------------------------ handlers

        private void OnLoadPressed()
        {
            if (app != null)
            {
                app.LoadModelViaDialog();
            }
        }

        private void OnPlayPressed()
        {
            if (romAnimator != null)
            {
                romAnimator.TogglePlay();
            }
        }

        private void OnEvenOutTwo()
        {
            ApplyEvenOut(2);
        }

        private void OnEvenOutThree()
        {
            ApplyEvenOut(3);
        }

        private void OnEvenOutFour()
        {
            ApplyEvenOut(4);
        }

        private void ApplyEvenOut(int maxInfluences)
        {
            if (app != null)
            {
                app.ApplyEvenOut(maxInfluences);
                RefreshSmoothLabel();
            }
        }

        private void OnSmoothPressed()
        {
            if (app != null)
            {
                app.ApplySmooth();
                RefreshSmoothLabel();
            }
        }

        private void OnCullPressed()
        {
            if (app != null)
            {
                app.CullToBudget();
            }
        }

        private void OnRevertPressed()
        {
            if (app != null)
            {
                app.Revert();
                RefreshSmoothLabel();
            }
        }

        private void OnSmoothStrengthChanged(float value)
        {
            if (app != null)
            {
                app.smoothStrength = value;
            }

            RefreshSmoothLabel();
        }

        private void OnAmplitudeChanged(float value)
        {
            if (romAnimator != null)
            {
                romAnimator.amplitude = value;
            }

            RefreshRomLabel();
        }

        private void OnSpeedChanged(float value)
        {
            if (romAnimator != null)
            {
                romAnimator.speed = value;
            }

            RefreshRomLabel();
        }

        private void RefreshSmoothLabel()
        {
            if (smoothLabel == null || app == null)
            {
                return;
            }

            smoothLabel.text = "Strength " + app.smoothStrength.ToString("F2")
                + "   " + app.SmoothPasses.ToString() + " passes";
        }

        private void RefreshRomLabel()
        {
            if (romLabel == null || romAnimator == null)
            {
                return;
            }

            romLabel.text = "Bend " + romAnimator.amplitude.ToString("F0")
                + " deg   Speed " + romAnimator.speed.ToString("F2");
        }

        // ------------------------------------------------------------------ builders

        private Text BuildLabel(Transform parent, string content, int size, TextAnchor alignment)
        {
            GameObject holder = new GameObject("Label", typeof(RectTransform));
            holder.transform.SetParent(parent, false);

            Text text = holder.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = TextColour;
            text.alignment = alignment;
            text.text = content;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            LayoutElement element = holder.AddComponent<LayoutElement>();
            element.minHeight = size + 6;

            return text;
        }

        private void BuildHeading(Transform parent, string content)
        {
            Text heading = BuildLabel(parent, content, 13, TextAnchor.MiddleLeft);
            heading.color = new Color(0.58f, 0.63f, 0.70f, 1f);
            heading.fontStyle = FontStyle.Bold;
        }

        private void BuildSpacer(Transform parent, float height)
        {
            GameObject holder = new GameObject("Spacer", typeof(RectTransform));
            holder.transform.SetParent(parent, false);

            LayoutElement element = holder.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
        }

        private GameObject BuildRow(Transform parent)
        {
            GameObject holder = new GameObject("Row", typeof(RectTransform));
            holder.transform.SetParent(parent, false);

            HorizontalLayoutGroup layout = holder.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            LayoutElement element = holder.AddComponent<LayoutElement>();
            element.minHeight = 32f;

            return holder;
        }

        /// <summary>Returns the button's text component so callers can retitle it later.</summary>
        private Text BuildButton(Transform parent, string caption, Color colour, UnityEngine.Events.UnityAction action)
        {
            GameObject holder = new GameObject("Button_" + caption, typeof(RectTransform));
            holder.transform.SetParent(parent, false);

            Image image = holder.AddComponent<Image>();
            image.color = colour;

            Button button = holder.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);

            ColorBlock colours = button.colors;
            colours.normalColor = Color.white;
            colours.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colours.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            button.colors = colours;

            LayoutElement element = holder.AddComponent<LayoutElement>();
            element.minHeight = 32f;
            element.preferredHeight = 32f;

            GameObject labelObject = new GameObject("Text", typeof(RectTransform));
            labelObject.transform.SetParent(holder.transform, false);

            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            Text label = labelObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = 14;
            label.color = TextColour;
            label.alignment = TextAnchor.MiddleCenter;
            label.text = caption;

            return label;
        }

        private void BuildSlider(
            Transform parent,
            string caption,
            float minimum,
            float maximum,
            float initial,
            UnityEngine.Events.UnityAction<float> action)
        {
            GameObject holder = new GameObject("Slider_" + caption, typeof(RectTransform));
            holder.transform.SetParent(parent, false);

            LayoutElement element = holder.AddComponent<LayoutElement>();
            element.minHeight = 20f;
            element.preferredHeight = 20f;

            GameObject backgroundObject = new GameObject("Background", typeof(RectTransform));
            backgroundObject.transform.SetParent(holder.transform, false);
            StretchFull(backgroundObject.GetComponent<RectTransform>());
            Image backgroundImage = backgroundObject.AddComponent<Image>();
            backgroundImage.color = new Color(0.09f, 0.10f, 0.11f, 1f);

            GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(holder.transform, false);
            StretchFull(fillArea.GetComponent<RectTransform>());

            GameObject fill = new GameObject("Fill", typeof(RectTransform));
            fill.transform.SetParent(fillArea.transform, false);
            StretchFull(fill.GetComponent<RectTransform>());
            Image fillImage = fill.AddComponent<Image>();
            fillImage.color = AccentColour;

            GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(holder.transform, false);
            StretchFull(handleArea.GetComponent<RectTransform>());

            GameObject handle = new GameObject("Handle", typeof(RectTransform));
            handle.transform.SetParent(handleArea.transform, false);
            RectTransform handleRect = handle.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(12f, 0f);
            Image handleImage = handle.AddComponent<Image>();
            handleImage.color = new Color(0.85f, 0.87f, 0.90f, 1f);

            Slider slider = holder.AddComponent<Slider>();
            slider.fillRect = fill.GetComponent<RectTransform>();
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = minimum;
            slider.maxValue = maximum;
            slider.SetValueWithoutNotify(initial);
            slider.onValueChanged.AddListener(action);
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
