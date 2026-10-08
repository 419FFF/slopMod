using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SlopMod
{
    /// <summary>
    /// The slopMod "EXTRAS" submenu.
    ///
    /// Every row here is built by slopMod and drawn over the game's menu (the main-menu entry is drawn
    /// the same way). Cloned game buttons only ever rendered the focused row (the game stores part of
    /// the button art outside the button object), so the options are our own uGUI <see cref="Button"/>s,
    /// which are always visible and selectable. Every row is styled through <see cref="UiKit.MenuStyle"/>
    /// - the game's own unselected pill sprite / label colour by default, swapping to the selected look
    /// while focused. The panel sits on the Canvas root above the menu and is destroyed as soon as the
    /// submenu closes, so nothing lingers over the main menu.
    /// </summary>
    internal sealed class SlopExtrasMenu : MenuController
    {
        private static SlopExtrasMenu _instance;

        private readonly List<Button> _buttons = new List<Button>();

        /// <summary>Opens (creating on first use) the Extras submenu.</summary>
        internal static void Open(Button template, Vector3 topPosition, float spacing)
        {
            if (template == null) { Warn("Extras: no button template available."); return; }

            MenuStackController stack = Object.FindObjectOfType<MenuStackController>();
            if (stack == null) { Warn("Extras: no MenuStackController found."); return; }

            if (_instance == null)
            {
                _instance = Build(template, spacing);
                if (_instance == null) return;
            }

            stack.PushMenuToStack(_instance.gameObject);
            Info("Extras menu opened.");
        }

        private static SlopExtrasMenu Build(Button template, float spacing)
        {
            UiKit.MenuStyle style = UiKit.MenuStyle.From(template);

            Transform parent = UiKit.CanvasRootOf(template, template.transform.parent);

            GameObject panel = new GameObject("SlopModExtrasMenu", typeof(RectTransform));
            RectTransform panelRect = (RectTransform)panel.transform;
            panelRect.SetParent(parent, false);
            Stretch(panelRect);
            panel.transform.SetAsLastSibling();

            GameObject content = new GameObject("Content", typeof(RectTransform));
            RectTransform contentRect = (RectTransform)content.transform;
            contentRect.SetParent(panelRect, false);
            Stretch(contentRect);

            SlopExtrasMenu menu = panel.AddComponent<SlopExtrasMenu>();
            menu._goToEnableDisable = content;

            float step = Mathf.Clamp(spacing, 74f, 120f);
            float top = step * 1.5f;

            CreateTitle(contentRect, style, "EXTRAS", top);

            Button extended = CreateButton(contentRect, style, "PRACTICE EXTENDED", top - step, menu._buttons);
            extended.onClick.AddListener(StartPracticeExtended);

            Button e1 = CreateButton(contentRect, style, "PRACTICE E1 JUMP", top - step * 2f, menu._buttons);
            e1.onClick.AddListener(StartPracticeE1Jump);

            Button back = CreateButton(contentRect, style, "BACK", top - step * 3f, menu._buttons);
            back.onClick.AddListener(GoBack);

            menu.WireNavigation();

            Info("Extras menu built (" + menu._buttons.Count + " options).");
            return menu;
        }

        private static Button CreateButton(RectTransform parent, UiKit.MenuStyle style, string label, float y, List<Button> buttons)
        {
            Button button = UiKit.CreateRow(parent, style, label);
            ((RectTransform)button.transform).anchoredPosition = new Vector2(0f, y);
            buttons.Add(button);
            return button;
        }

        private static void CreateTitle(RectTransform parent, UiKit.MenuStyle style, string label, float y)
        {
            GameObject go = new GameObject("SlopTitle_" + label, typeof(RectTransform));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = style.Size;

            Text text = go.AddComponent<Text>();
            text.font = style.Font;
            text.fontSize = Mathf.RoundToInt(style.FontSize * 1.25f);
            text.fontStyle = FontStyle.Bold;
            text.text = label;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;

            Outline outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.65f);
            outline.effectDistance = new Vector2(2f, -2f);
        }

        private void WireNavigation()
        {
            int count = _buttons.Count;
            for (int i = 0; i < count; i++)
            {
                Navigation navigation = new Navigation();
                navigation.mode = Navigation.Mode.Explicit;
                navigation.selectOnUp = _buttons[(i - 1 + count) % count];
                navigation.selectOnDown = _buttons[(i + 1) % count];
                _buttons[i].navigation = navigation;
            }
        }

        public override void OnMenuGainedFocus()
        {
            base.OnMenuGainedFocus();
            if (_buttons.Count > 0 && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(_buttons[0].gameObject);
            }
        }

        public override void OnMenuLostFocus()
        {
            base.OnMenuLostFocus();
            // The submenu is a leaf, so losing focus means it closed - drop the whole overlay.
            if (_instance == this) _instance = null;
            Destroy(gameObject);
        }

        private static void StartPracticeExtended()
        {
            EpisodeTools.StartPracticeExtended();
        }

        private static void StartPracticeE1Jump()
        {
            EpisodeTools.StartPracticeE1Jump();
        }

        private static void GoBack()
        {
            MenuStackController stack = Object.FindObjectOfType<MenuStackController>();
            if (stack != null) stack.BackButtonPressedByCode();
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
        }

        private static void Info(string message)
        {
            if (SlopModPlugin.Log != null) SlopModPlugin.Log.LogInfo(message);
        }

        private static void Warn(string message)
        {
            if (SlopModPlugin.Log != null) SlopModPlugin.Log.LogWarning(message);
        }

        // (the button style is now read by UiKit.MenuStyle.From.)
    }
}
