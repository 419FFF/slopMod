using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SlopMod
{
    /// <summary>
    /// Swaps a slopMod menu row between its "unselected" and "selected" look, mirroring how the game's
    /// own buttons change (background sprite + label colour). This is what lets the slopMod overlay
    /// match the game's menu style without cloning any of the game's (partly external) button art.
    /// </summary>
    internal sealed class SlopMenuRow : MonoBehaviour, ISelectHandler, IDeselectHandler
    {
        internal Image Background;
        internal Text Label;
        internal Sprite SelectedSprite;
        internal Sprite UnselectedSprite;
        internal Color SelectedTextColor = new Color(0.12f, 0.12f, 0.2f, 1f);
        internal Color UnselectedTextColor = Color.white;
        internal Color SelectedBackgroundColor = Color.white;
        internal Color UnselectedBackgroundColor = Color.white;

        private void OnEnable()
        {
            Apply(false);
        }

        public void OnSelect(BaseEventData eventData)
        {
            Apply(true);
        }

        public void OnDeselect(BaseEventData eventData)
        {
            Apply(false);
        }

        private void Apply(bool selected)
        {
            if (Background != null)
            {
                Sprite sprite = selected && SelectedSprite != null ? SelectedSprite : UnselectedSprite;
                if (sprite != null) Background.sprite = sprite;
                Background.color = selected ? SelectedBackgroundColor : UnselectedBackgroundColor;
            }

            if (Label != null) Label.color = selected ? SelectedTextColor : UnselectedTextColor;
        }
    }
}
