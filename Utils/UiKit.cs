using UnityEngine;
using UnityEngine.UI;

namespace SlopMod
{
    /// <summary>
    /// Helpers for building slopMod menu rows that look like the game's own menu buttons.
    ///
    /// The game keeps each button's "selected" and "unselected" art on SEPARATE objects
    /// (<see cref="ButtonGOChange.m_onSelectedGO"/> / <see cref="ButtonGOChange.m_onUnselectedGO"/>) and
    /// swaps the pill sprite + label colour through
    /// <see cref="UISelectedUnselectedButtonVisualChange"/>. A cloned button therefore only ever renders
    /// the state whose object happens to be inside it (that is why only the focused option used to
    /// show). slopMod now reads both state objects and draws its own row - the normal unselected look by
    /// default and the selected look while focused - so every row is always visible and matches the
    /// main menu.
    /// </summary>
    internal static class UiKit
    {
        private static readonly Color DefaultTextColor = new Color(0.16f, 0.17f, 0.2f, 1f);

        internal static Transform CanvasRootOf(Component context, Transform fallback)
        {
            if (context != null)
            {
                Canvas canvas = context.GetComponentInParent<Canvas>();
                if (canvas != null && canvas.rootCanvas != null) return canvas.rootCanvas.transform;
            }
            return fallback;
        }

        /// <summary>Visual style scraped from one of the game's own menu buttons.</summary>
        internal struct MenuStyle
        {
            public Sprite UnselectedSprite;
            public Sprite SelectedSprite;
            public Font Font;
            public int FontSize;
            public Color UnselectedTextColor;
            public Color SelectedTextColor;
            public Color UnselectedBackgroundColor;
            public Color SelectedBackgroundColor;
            public Vector2 Size;

            public static MenuStyle From(Button template)
            {
                MenuStyle style = new MenuStyle();
                style.Size = new Vector2(520f, 84f);
                style.FontSize = 40;
                style.UnselectedTextColor = DefaultTextColor;
                style.SelectedTextColor = DefaultTextColor;
                style.UnselectedBackgroundColor = Color.white;
                style.SelectedBackgroundColor = Color.white;

                if (template == null)
                {
                    style.Font = BuiltinFont();
                    return style;
                }

                RectTransform templateRect = template.GetComponent<RectTransform>();
                if (templateRect != null && templateRect.rect.width > 1f && templateRect.rect.height > 1f)
                {
                    style.Size = templateRect.rect.size;
                }

                ButtonGOChange goChange = template.GetComponent<ButtonGOChange>();
                GameObject unselectedGO = goChange != null ? goChange.m_onUnselectedGO : null;
                GameObject selectedGO = goChange != null ? goChange.m_onSelectedGO : null;

                // Each state's art lives on its own object, so read the pill sprite from the matching one
                // (reading the button itself used to return the selected, blue-bordered pill).
                style.UnselectedSprite = FindBestSprite(unselectedGO);
                style.SelectedSprite = FindBestSprite(selectedGO);

                UISelectedUnselectedButtonVisualChange change =
                    template.GetComponentInChildren<UISelectedUnselectedButtonVisualChange>(true);
                if (change != null)
                {
                    if (style.UnselectedSprite == null) style.UnselectedSprite = change.m_backgroundUnselectedSprite;
                    if (style.SelectedSprite == null) style.SelectedSprite = change.m_backgroundSelectedSprite;
                }

                if (style.UnselectedSprite == null) style.UnselectedSprite = FindBestSprite(template.gameObject);
                if (style.SelectedSprite == null) style.SelectedSprite = style.UnselectedSprite;

                // Label colours: take the real colour of each state's text (the unselected default used to
                // be white, which was invisible on the light pill).
                Text unselectedText = unselectedGO != null ? FindText(unselectedGO) : null;
                if (unselectedText != null) style.UnselectedTextColor = unselectedText.color;
                else if (goChange != null && goChange.m_textButtonUnselected != null) style.UnselectedTextColor = goChange.m_textButtonUnselected.color;

                Text selectedText = selectedGO != null ? FindText(selectedGO) : null;
                if (selectedText != null) style.SelectedTextColor = selectedText.color;
                else if (goChange != null && goChange.m_textButtonSelected != null) style.SelectedTextColor = goChange.m_textButtonSelected.color;

                Text anyText = unselectedText != null ? unselectedText : selectedText;
                if (anyText == null) anyText = FindText(template.gameObject);
                if (anyText != null)
                {
                    if (anyText.font != null) style.Font = anyText.font;
                    if (anyText.fontSize > 0) style.FontSize = anyText.fontSize;
                }
                if (style.Font == null) style.Font = BuiltinFont();

                return style;
            }
        }

        // === rows ===

        /// <summary>Builds a standalone slopMod row (no game clone). Used by the Extras submenu.</summary>
        internal static Button CreateRow(Transform parent, MenuStyle style, string label)
        {
            GameObject go = new GameObject("SlopRow_" + label, typeof(RectTransform));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = style.Size;

            Image background = go.AddComponent<Image>();
            ApplyBackground(background, style);

            Text text = CreateLabel(rect, style, label);

            Button button = go.AddComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.None; // SlopMenuRow swaps the visuals instead

            ConfigureRow(go.AddComponent<SlopMenuRow>(), background, text, style);
            return button;
        }

        /// <summary>
        /// Clones one of the game's buttons (so its navigation / selection bookkeeping keeps working)
        /// but hides the clone's own art and draws slopMod's instead, so it is always visible and uses
        /// the normal main-menu look. Used for the main-menu entry row.
        /// </summary>
        internal static Button CloneStyledRow(Button template, Transform parent, Vector3 position, MenuStyle style, string label)
        {
            if (template == null) return null;

            GameObject clone = Object.Instantiate(template.gameObject, parent);
            clone.name = "SlopRow_" + label;
            clone.transform.position = position;
            clone.transform.rotation = template.transform.rotation;
            clone.transform.localScale = template.transform.localScale;
            clone.transform.SetAsLastSibling();
            clone.SetActive(true);

            // Hide every graphic the clone brought with it (it renders the wrong / no state anyway).
            Image[] images = clone.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i] != null) images[i].enabled = false;
            }
            Text[] texts = clone.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null) texts[i].enabled = false;
            }

            // Stop the game's select / deselect handler from re-showing that art.
            ButtonGOChange goChange = clone.GetComponent<ButtonGOChange>();
            if (goChange != null)
            {
                goChange.m_onSelectedGO = null;
                goChange.m_onUnselectedGO = null;
            }

            Image background = clone.GetComponent<Image>();
            if (background == null) background = clone.AddComponent<Image>();
            background.enabled = true;
            ApplyBackground(background, style);

            Text text = CreateLabel(clone.GetComponent<RectTransform>(), style, label);

            Button button = clone.GetComponent<Button>();
            if (button != null)
            {
                button.onClick = new Button.ButtonClickedEvent();
                button.targetGraphic = background;
                button.transition = Selectable.Transition.None;
            }

            SlopMenuRow row = clone.GetComponent<SlopMenuRow>();
            if (row == null) row = clone.AddComponent<SlopMenuRow>();
            ConfigureRow(row, background, text, style);

            return button;
        }

        /// <summary>Wires a row's explicit navigation so the stick / D-pad reaches it and wraps around.</summary>
        internal static void StyleNavigation(Button entry, Button up, Button down)
        {
            if (entry == null) return;

            Navigation navigation = entry.navigation;
            navigation.mode = Navigation.Mode.Explicit;
            navigation.selectOnUp = up;
            navigation.selectOnDown = down;
            entry.navigation = navigation;
        }

        // === helpers ===

        private static Text CreateLabel(RectTransform parent, MenuStyle style, string label)
        {
            GameObject go = new GameObject("SlopLabel", typeof(RectTransform));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(24f, 4f);
            rect.offsetMax = new Vector2(-24f, -4f);

            Text text = go.AddComponent<Text>();
            text.font = style.Font != null ? style.Font : BuiltinFont();
            text.fontSize = style.FontSize;
            text.text = label;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = style.UnselectedTextColor;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static void ApplyBackground(Image image, MenuStyle style)
        {
            if (image == null) return;
            image.sprite = style.UnselectedSprite;
            image.type = (style.UnselectedSprite != null && style.UnselectedSprite.border != Vector4.zero)
                ? Image.Type.Sliced : Image.Type.Simple;
            image.color = style.UnselectedBackgroundColor;
            image.raycastTarget = true;
        }

        private static void ConfigureRow(SlopMenuRow row, Image background, Text text, MenuStyle style)
        {
            if (row == null) return;
            row.Background = background;
            row.Label = text;
            row.SelectedSprite = style.SelectedSprite;
            row.UnselectedSprite = style.UnselectedSprite;
            row.SelectedTextColor = style.SelectedTextColor;
            row.UnselectedTextColor = style.UnselectedTextColor;
            row.SelectedBackgroundColor = style.SelectedBackgroundColor;
            row.UnselectedBackgroundColor = style.UnselectedBackgroundColor;
        }

        /// <summary>First sprite on the object, preferring a 9-sliced one (the rounded pill).</summary>
        private static Sprite FindBestSprite(GameObject go)
        {
            if (go == null) return null;
            Image[] images = go.GetComponentsInChildren<Image>(true);
            Sprite fallback = null;
            for (int i = 0; i < images.Length; i++)
            {
                Image image = images[i];
                if (image == null || image.sprite == null) continue;
                if (image.sprite.border != Vector4.zero) return image.sprite;
                if (fallback == null) fallback = image.sprite;
            }
            return fallback;
        }

        private static Text FindText(GameObject go)
        {
            if (go == null) return null;
            Text[] texts = go.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null) return texts[i];
            }
            return null;
        }

        private static Font BuiltinFont()
        {
            try { return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch { }
            try { return Resources.GetBuiltinResource<Font>("Arial.ttf"); }
            catch { }
            return null;
        }
    }
}
