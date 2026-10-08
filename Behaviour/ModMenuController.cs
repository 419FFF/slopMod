using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace SlopMod
{
    /// <summary>
    /// Adds the slopMod entry to the real main menu, placed directly under the existing entries and
    /// styled from the game's own buttons (the unselected look). Pressing it opens
    /// <see cref="SlopExtrasMenu"/>.
    ///
    /// Robustness notes:
    ///   * The game stores its menu buttons in SerializeField fields on <see cref="MainMenu"/>
    ///     (<c>_startSelectedButton</c>, <c>_newGameSelectedButton</c>, <c>_optionsSelectedButton</c>,
    ///     <c>_loadGameSelectedButton</c>). Those objects are not guaranteed to be children of the
    ///     MainMenu GameObject, so we read the fields with reflection instead of relying on
    ///     <c>GetComponentsInChildren</c>.
    ///   * The menu is built/animated across several frames, so we retry for a few seconds.
    ///   * The clone is parented like the real buttons (or to the Canvas root if a LayoutGroup is
    ///     present) and forced to the last sibling so it always draws on top.
    ///   * Everything is logged.
    /// </summary>
    internal sealed class ModMenuController : MonoBehaviour
    {
        private const float FallbackSpacing = 70f;
        private const float BuildTimeoutSeconds = 5f;
        private const string MainMenuLabel = "SLOPMOD";

        private readonly List<GameObject> _clones = new List<GameObject>();
        private MainMenu _menu;
        private bool _built;

        public static void AttachTo(MainMenu menu)
        {
            if (menu == null) return;
            if (menu.GetComponent<ModMenuController>() != null) return;

            ModMenuController controller = menu.gameObject.AddComponent<ModMenuController>();
            controller._menu = menu;
        }

        private void Start()
        {
            StartCoroutine(BuildRoutine());
        }

        private IEnumerator BuildRoutine()
        {
            float deadline = Time.realtimeSinceStartup + BuildTimeoutSeconds;
            while (!_built && Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForEndOfFrame();
                TryBuild();
            }

            if (!_built)
            {
                SlopModPlugin.Log.LogWarning("ModMenuController: gave up adding the slopMod EXTRAS entry (no active menu buttons found).");
            }
        }

        private void TryBuild()
        {
            if (_built) return;

            List<Button> active = CollectActiveButtons();
            if (active.Count == 0) return;

            // Top-most button first, so [last] is the bottom of the menu stack.
            active.Sort(delegate (Button a, Button b) { return b.transform.position.y.CompareTo(a.transform.position.y); });

            Button template = active[0];
            Button bottom = active[active.Count - 1];

            float spacing = FallbackSpacing;
            if (active.Count >= 2)
            {
                float gap = Mathf.Abs(active[0].transform.position.y - active[1].transform.position.y);
                if (gap > 1f) spacing = gap;
            }

            Transform parent = template.transform.parent;
            if (parent == null || parent.GetComponent<LayoutGroup>() != null)
            {
                parent = UiKit.CanvasRootOf(template, template.transform);
            }
            if (parent == null) parent = template.transform;

            Vector3 topPosition = template.transform.position;
            Vector3 bottomPosition = bottom.transform.position;

            // Draw the entry ourselves, styled from the game's own unselected button. A bare clone of a
            // game button only renders the state whose art object lives inside it (part of the art is
            // external), so the clone is kept purely for the menu's navigation / selection bookkeeping
            // while slopMod supplies the visible art.
            UiKit.MenuStyle style = UiKit.MenuStyle.From(template);

            Button entry = UiKit.CloneStyledRow(template, parent,
                new Vector3(bottomPosition.x, bottomPosition.y - spacing, bottomPosition.z), style, MainMenuLabel);
            if (entry == null) return;

            entry.onClick.AddListener(delegate { SlopExtrasMenu.Open(template, topPosition, spacing); });
            UiKit.StyleNavigation(entry, bottom, template);
            _clones.Add(entry.gameObject);

            _built = true;

            SlopModPlugin.Log.LogInfo(string.Concat(
                "ModMenuController: added ", MainMenuLabel, ". template='", template.name,
                "' bottom='", bottom.name,
                "' spacing=", spacing.ToString("0"),
                " parent='", parent.name,
                "' activeButtons=", active.Count.ToString()));
        }

        /// <summary>
        /// Collects the menu buttons that should be treated as "the main menu". Prefers the exact
        /// SerializeField references the game itself uses; falls back to child Buttons.
        /// </summary>
        private List<Button> CollectActiveButtons()
        {
            List<Button> result = new List<Button>();

            if (_menu != null)
            {
                FieldInfo[] fields = typeof(MainMenu).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < fields.Length; i++)
                {
                    if (!typeof(Button).IsAssignableFrom(fields[i].FieldType)) continue;

                    ButtonGOChange value = fields[i].GetValue(_menu) as ButtonGOChange;
                    if (value == null) continue;

                    Button button = value;
                    if (button.gameObject.activeInHierarchy && !result.Contains(button)) result.Add(button);
                }
            }

            if (result.Count > 0) return result;

            Button[] all = GetComponentsInChildren<Button>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].gameObject.activeInHierarchy) result.Add(all[i]);
            }
            return result;
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _clones.Count; i++)
            {
                if (_clones[i] != null) Destroy(_clones[i]);
            }
            _clones.Clear();
        }
    }
}
