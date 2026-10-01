using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoxLab
{
    public enum WorkshopButtonKind { Primary, Secondary, Danger, Attention }
    public enum WorkshopPanelKind { Window, Card, Inset, Parchment }

    /// <summary>Shared Adventure artwork and nine-slice IMGUI styling. No gameplay or layout ownership.</summary>
    public sealed class WorkshopTheme
    {
        public static WorkshopTheme Shared { get { return shared ?? (shared = new WorkshopTheme()); } }
        public static readonly Color Text = new Color(.98f, .96f, .89f);
        public static readonly Color Muted = new Color(.76f, .78f, .75f);
        public static readonly Color Ink = new Color(.22f, .18f, .13f);
        public static readonly Color Accent = new Color(.92f, .72f, .36f);
        public static readonly Color Backdrop = new Color(.235f, .225f, .21f);
        public static readonly Color Success = new Color(.47f, .80f, .58f);
        public static readonly Color Error = new Color(1f, .48f, .40f);
        const string ResourceRoot = "BoxLabUI/Adventure/";
        static WorkshopTheme shared;
        readonly List<Texture2D> generated = new List<Texture2D>();
        readonly Dictionary<string, GUIStyle> buttons = new Dictionary<string, GUIStyle>();
        readonly Dictionary<GUISkin, Font> appliedSkins = new Dictionary<GUISkin, Font>();
        Font font;
        bool ready;
        Texture2D[] normal, hover, pressed, disabled;
        Texture2D window, card, inset, parchment, inputFocus, checkedBox, emptyBox, handle;
        GUIStyle windowStyle, cardStyle, insetStyle, parchmentStyle, toggleStyle, toggleLabel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            if (shared != null) shared.ReleaseGeneratedTextures();
            shared = null;
        }

        /// <summary>Call inside OnGUI with the app's Chinese-capable font. The theme never owns that font.</summary>
        public void Ensure(Font suppliedFont = null)
        {
            Font current = suppliedFont ? suppliedFont : (font ? font : GUI.skin.font);
            if (font != current) { font = current; buttons.Clear(); appliedSkins.Clear(); toggleStyle = toggleLabel = null; }
            if (ready) return;
            ready = true;
            var wood = Load("panel_brown_dark_corners_a", new Color(.55f, .34f, .16f));
            var stone = Load("panel_grey_bolts", new Color(.44f, .54f, .58f));
            var red = Load("panel_grey_bolts_red", new Color(.56f, .25f, .21f));
            var dark = Load("panel_grey_bolts_dark", new Color(.22f, .27f, .29f));
            var plain = Load("panel_grey_dark", new Color(.23f, .27f, .29f));
            parchment = Load("panel_brown_corners_a", new Color(.94f, .83f, .62f));
            emptyBox = Load("checkbox_brown_empty", new Color(.36f, .28f, .18f));
            checkedBox = Load("checkbox_brown_checked", new Color(.71f, .53f, .25f));
            handle = Load("scrollbar_grey", new Color(.55f, .63f, .64f));
            Texture2D[] sources = { wood, stone, red };
            normal = new Texture2D[4]; hover = new Texture2D[4]; pressed = new Texture2D[4]; disabled = new Texture2D[4];
            for (int i = 0; i < 3; i++)
            {
                // Darker middle tones keep Chinese labels clear without flattening the source bevels.
                normal[i] = Tint(sources[i], i == 0 ? new Color(.86f, .83f, .78f) : new Color(.64f, .68f, .70f), 0, "normal");
                hover[i] = Tint(sources[i], i == 0 ? new Color(.96f, .91f, .84f) : new Color(.72f, .76f, .78f), 0, "hover");
                pressed[i] = Tint(sources[i], new Color(.52f, .56f, .57f), 0, "pressed");
                disabled[i] = Tint(sources[i], new Color(.43f, .46f, .46f), .76f, "disabled");
            }
            // Attention changes colours only; the existing Adventure bevel and control metrics stay intact.
            normal[3] = Tint(parchment, new Color(1f, .88f, .59f), 0, "attention normal");
            hover[3] = Tint(parchment, new Color(1f, .96f, .73f), 0, "attention hover");
            pressed[3] = Tint(parchment, new Color(.89f, .73f, .44f), 0, "attention pressed");
            disabled[3] = Tint(parchment, new Color(.62f, .60f, .55f), .76f, "attention disabled");
            window = Tint(dark, new Color(.48f, .50f, .51f), .08f, "window");
            card = Tint(plain, new Color(.58f, .60f, .61f), .1f, "card");
            inset = Tint(plain, new Color(.32f, .35f, .37f), .1f, "inset");
            inputFocus = Tint(plain, new Color(.54f, .56f, .51f), 0, "input focus");
            windowStyle = PanelStyle(window, 11);
            cardStyle = PanelStyle(card, 7);
            insetStyle = PanelStyle(inset, 7);
            parchmentStyle = PanelStyle(parchment, 10);
        }

        public GUIStyle CreateLabel(int size = 16, bool muted = false)
        {
            Ensure();
            return new GUIStyle(GUI.skin.label) { font = font, fontSize = size, richText = false,
                wordWrap = true, alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0),
                normal = { textColor = muted ? Muted : Text }, hover = { textColor = muted ? Muted : Text },
                active = { textColor = muted ? Muted : Text }, focused = { textColor = muted ? Muted : Text } };
        }

        /// <summary>Returns an independent style; callers can change font size, padding and alignment.</summary>
        public GUIStyle CreateButton(int size = 16, WorkshopButtonKind kind = WorkshopButtonKind.Secondary)
        {
            Ensure();
            return new GUIStyle(ButtonStyle(size, kind, true));
        }
        public GUIStyle CreateDisabledButton(int size = 16, WorkshopButtonKind kind = WorkshopButtonKind.Secondary)
        {
            Ensure(); return new GUIStyle(ButtonStyle(size, kind, false));
        }
        GUIStyle ButtonStyle(int size, WorkshopButtonKind kind, bool enabled)
        {
            string key = size + ":" + (int)kind + ":" + enabled;
            GUIStyle style;
            if (buttons.TryGetValue(key, out style)) return style;
            int k = kind == WorkshopButtonKind.Attention ? 3 : Mathf.Clamp((int)kind, 0, 2);
            style = new GUIStyle { font = font, fontSize = size, alignment = TextAnchor.MiddleCenter,
                wordWrap = false, richText = false, clipping = TextClipping.Clip,
                border = new RectOffset(10, 10, 10, 10), padding = new RectOffset(10, 10, 3, 5),
                margin = new RectOffset(0, 0, 0, 0), stretchWidth = true, stretchHeight = true };
            Color text = k == 3 ? (enabled ? Ink : new Color(.30f, .29f, .26f))
                : (enabled ? Text : new Color(.59f, .61f, .59f));
            Set(style.normal, enabled ? normal[k] : disabled[k], text);
            Set(style.hover, enabled ? hover[k] : disabled[k], text);
            Set(style.active, enabled ? pressed[k] : disabled[k], text);
            Set(style.focused, enabled ? hover[k] : disabled[k], text);
            Set(style.onNormal, enabled ? pressed[k] : disabled[k], text);
            Set(style.onHover, enabled ? hover[k] : disabled[k], text);
            Set(style.onActive, enabled ? pressed[k] : disabled[k], text);
            Set(style.onFocused, enabled ? hover[k] : disabled[k], text);
            buttons.Add(key, style); return style;
        }

        public GUIStyle CreateInput(int size = 16)
        {
            Ensure();
            var style = new GUIStyle { font = font, fontSize = size, richText = false,
                alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, wordWrap = false,
                border = new RectOffset(7, 7, 7, 7), padding = new RectOffset(10, 10, 5, 5),
                margin = new RectOffset(0, 0, 0, 0), stretchWidth = true, stretchHeight = true };
            Set(style.normal, inset, Text); Set(style.hover, inputFocus, Text);
            Set(style.active, inputFocus, Text); Set(style.focused, inputFocus, Text);
            Set(style.onNormal, inset, Text); Set(style.onHover, inputFocus, Text);
            Set(style.onActive, inputFocus, Text); Set(style.onFocused, inputFocus, Text);
            return style;
        }

        public void DrawPanel(Rect rect, WorkshopPanelKind kind = WorkshopPanelKind.Window)
        {
            Ensure();
            if (Event.current.type != EventType.Repaint) return;
            GetPanelStyle(kind).Draw(rect, GUIContent.none, false, false, false, false);
        }
        public GUIStyle GetPanelStyle(WorkshopPanelKind kind = WorkshopPanelKind.Window)
        {
            Ensure();
            switch (kind) { case WorkshopPanelKind.Card: return cardStyle; case WorkshopPanelKind.Inset: return insetStyle;
                case WorkshopPanelKind.Parchment: return parchmentStyle; default: return windowStyle; }
        }
        public void DrawBackdrop(Rect rect) { Fill(rect, Backdrop); }
        public void DrawDivider(Rect rect) { Fill(rect, new Color(.52f, .47f, .36f, .6f)); }
        public void DrawScrim(Rect rect) { Fill(rect, new Color(.045f, .055f, .065f, .78f)); }

        public bool Button(Rect rect, string label, bool enabled = true, WorkshopButtonKind kind = WorkshopButtonKind.Secondary, int size = 16)
        {
            Ensure();
            bool previous = GUI.enabled, interactive = previous && enabled;
            GUI.enabled = interactive;
            bool hit = GUI.Button(rect, label, ButtonStyle(size, kind, interactive));
            GUI.enabled = previous;
            if (hit) WorkshopAudio.Play(WorkshopSound.UI);
            return hit;
        }

        /// <summary>Colour a draggable palette card without consuming its pointer events.</summary>
        public void DrawButtonSurface(Rect rect, WorkshopButtonKind kind, bool enabled = true)
        {
            Ensure();
            if (Event.current.type == EventType.Repaint)
                ButtonStyle(16, kind, GUI.enabled && enabled).Draw(rect, GUIContent.none, false, false, false, false);
        }

        /// <summary>Apply once per skin/font, inside OnGUI. Covers controls still using default GUI styles.</summary>
        public void ApplyToSkin(GUISkin skin)
        {
            if (!skin) return;
            Ensure(font ? font : skin.font);
            Font applied;
            if (appliedSkins.TryGetValue(skin, out applied) && applied == font) return;
            skin.font = font; skin.label = CreateLabel(); skin.button = CreateButton();
            skin.box = new GUIStyle(windowStyle) { font = font, fontSize = 16, alignment = TextAnchor.UpperLeft };
            SetTextColors(skin.box, Text);
            skin.textField = CreateInput(); skin.textArea = CreateInput();
            skin.textArea.wordWrap = true; skin.textArea.alignment = TextAnchor.UpperLeft;
            toggleStyle = new GUIStyle { font = font, fontSize = 16, alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(29, 3, 3, 3), margin = new RectOffset(0, 0, 0, 0),
                border = new RectOffset(4, 4, 4, 4), overflow = new RectOffset(0, 0, 0, 0) };
            // IMGUI toggle backgrounds otherwise stretch under the label; a custom fixed-size mark is drawn by Toggle().
            SetTextColors(toggleStyle, Text);
            skin.toggle = toggleStyle;
            ConfigureTrack(skin.horizontalSlider, false); ConfigureTrack(skin.verticalSlider, true);
            ConfigureHandle(skin.horizontalSliderThumb, false); ConfigureHandle(skin.verticalSliderThumb, true);
            ConfigureTrack(skin.horizontalScrollbar, false); ConfigureTrack(skin.verticalScrollbar, true);
            ConfigureHandle(skin.horizontalScrollbarThumb, false); ConfigureHandle(skin.verticalScrollbarThumb, true);
            skin.horizontalScrollbarLeftButton.fixedWidth = 0; skin.horizontalScrollbarRightButton.fixedWidth = 0;
            skin.verticalScrollbarUpButton.fixedHeight = 0; skin.verticalScrollbarDownButton.fixedHeight = 0;
            skin.settings.cursorColor = Text; skin.settings.selectionColor = new Color(.61f, .45f, .21f, .85f);
            appliedSkins[skin] = font;
        }

        public bool Toggle(Rect rect, bool value, string label, bool enabled = true)
        {
            Ensure();
            bool previous = GUI.enabled; GUI.enabled = previous && enabled;
            if (toggleLabel == null) { toggleLabel = CreateLabel(); toggleLabel.alignment = TextAnchor.MiddleLeft; }
            bool next = GUI.Toggle(rect, value, GUIContent.none, GUIStyle.none);
            if (Event.current.type == EventType.Repaint)
            {
                Color old = GUI.color; if (!GUI.enabled) GUI.color *= new Color(.65f, .65f, .65f, 1);
                GUI.DrawTexture(new Rect(rect.x, rect.center.y - 11, 22, 22), value ? checkedBox : emptyBox, ScaleMode.ScaleToFit, true);
                GUI.color = old;
            }
            GUI.Label(new Rect(rect.x + 29, rect.y, Mathf.Max(0, rect.width - 29), rect.height), label, toggleLabel);
            GUI.enabled = previous; return next;
        }
        void ConfigureTrack(GUIStyle style, bool vertical)
        {
            style.border = new RectOffset(6, 6, 6, 6); style.margin = new RectOffset(2, 2, 2, 2);
            style.padding = new RectOffset();
            style.fixedWidth = vertical ? 14 : 0; style.fixedHeight = vertical ? 0 : 14;
            Set(style.normal, inset, Text); Set(style.hover, inset, Text); Set(style.active, inset, Text); Set(style.focused, inset, Text);
        }
        void ConfigureHandle(GUIStyle style, bool vertical)
        {
            style.border = new RectOffset(3, 3, 3, 3); style.margin = new RectOffset(); style.padding = new RectOffset();
            style.fixedWidth = vertical ? 14 : 20; style.fixedHeight = vertical ? 20 : 14;
            Set(style.normal, handle, Text); Set(style.hover, handle, Text); Set(style.active, handle, Text); Set(style.focused, handle, Text);
        }
        static void Set(GUIStyleState state, Texture2D texture, Color text) { state.background = texture; state.textColor = text; }
        static void SetTextColors(GUIStyle style, Color color)
        {
            style.normal.textColor = style.hover.textColor = style.active.textColor = style.focused.textColor = color;
            style.onNormal.textColor = style.onHover.textColor = style.onActive.textColor = style.onFocused.textColor = color;
        }
        static GUIStyle PanelStyle(Texture2D texture, int border)
        {
            var style = new GUIStyle { border = new RectOffset(border, border, border, border),
                padding = new RectOffset(14, 14, 12, 12), margin = new RectOffset() };
            Set(style.normal, texture, Text); return style;
        }
        static void Fill(Rect rect, Color color)
        {
            Color old = GUI.color; GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = old;
        }
        Texture2D Load(string name, Color fallback)
        {
            var texture = Resources.Load<Texture2D>(ResourceRoot + name);
            if (texture) { texture.wrapMode = TextureWrapMode.Clamp; texture.filterMode = FilterMode.Bilinear; return texture; }
            // Removing the optional art folder leaves a usable interface.
            var result = new Texture2D(64, 64, TextureFormat.RGBA32, false) { name = "BoxLab fallback " + name,
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color[4096];
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                pixels[y * 64 + x] = x < 5 || y < 5 || x > 58 || y > 58 ? fallback * .65f : fallback;
            result.SetPixels(pixels); result.Apply(); generated.Add(result); return result;
        }
        Texture2D Tint(Texture2D source, Color multiplier, float desaturate, string suffix)
        {
            Texture2D readable = source;
            RenderTexture temporary = null, previous = RenderTexture.active;
            try
            {
                if (!source.isReadable)
                {
                    temporary = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    Graphics.Blit(source, temporary); RenderTexture.active = temporary;
                    readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                    readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); readable.Apply();
                }
                Color[] pixels = readable.GetPixels();
                for (int i = 0; i < pixels.Length; i++)
                {
                    float alpha = pixels[i].a;
                    Color grey = new Color(pixels[i].grayscale, pixels[i].grayscale, pixels[i].grayscale, alpha);
                    Color c = Color.Lerp(pixels[i], grey, desaturate) * multiplier; c.a = alpha; pixels[i] = c;
                }
                var result = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false)
                { name = "BoxLab Adventure " + suffix, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
                result.SetPixels(pixels); result.Apply(false, true); generated.Add(result); return result;
            }
            catch (Exception error)
            {
                Debug.LogWarning("BoxLab UI state tint unavailable; using the original Adventure panel: " + error.Message);
                return source;
            }
            finally
            {
                RenderTexture.active = previous;
                if (temporary) RenderTexture.ReleaseTemporary(temporary);
                if (readable && readable != source) DestroyTexture(readable);
            }
        }
        void ReleaseGeneratedTextures()
        {
            foreach (var texture in generated) if (texture) DestroyTexture(texture);
            generated.Clear(); buttons.Clear(); appliedSkins.Clear();
        }
        static void DestroyTexture(Texture2D texture)
        { if (Application.isPlaying) UnityEngine.Object.Destroy(texture); else UnityEngine.Object.DestroyImmediate(texture); }
    }
}
