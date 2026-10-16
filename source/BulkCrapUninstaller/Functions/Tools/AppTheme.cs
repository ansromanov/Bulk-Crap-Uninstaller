using BrightIdeasSoftware;
using System;
using System.Collections;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using System.Drawing.Imaging;
using Microsoft.Win32;
using SimpleTreeMap;

namespace BulkCrapUninstaller.Functions.Tools
{
    /// <summary>Applies BCU's light or dark palette to open and newly shown windows.</summary>
    internal static class AppTheme
    {
        internal enum ThemePreference
        {
            System,
            Light,
            Dark
        }

        private static readonly Color Window = Color.FromArgb(32, 32, 32);
        private static readonly Color Surface = Color.FromArgb(45, 45, 48);
        private static readonly Color Text = Color.FromArgb(235, 235, 235);
        private static readonly Color Selection = Color.FromArgb(62, 91, 125);
        private static readonly Color HeaderHot = Color.FromArgb(58, 58, 62);
        private const float DarkHighlightBlend = 0.28f;
        private static bool _dark;
        private static ThemePreference _preference = ThemePreference.System;
        private static bool _watching;
        private static bool _watchingSystemTheme;
        private static System.Windows.Forms.Timer _windowDiscoveryTimer;
        private static readonly HashSet<Form> WatchedForms = new();
        private static readonly ConditionalWeakTable<Control, OriginalControlStyle> OriginalStyles = new();
        private static readonly ConditionalWeakTable<ToolStripItem, OriginalItemStyle> OriginalItemStyles = new();

        internal static bool IsDark => _dark;
        internal static Color GetDefaultListItemBackColor(bool dark) => dark ? Surface : SystemColors.Window;
        internal static Color GetDefaultListItemForeColor(bool dark) => dark ? Text : SystemColors.WindowText;
        internal static Color GetListHighlightBackColor(Color color, bool dark) => dark
            ? Blend(Surface, color, DarkHighlightBlend)
            : color;

        internal static ThemePreference ParseThemePreference(string value) =>
            Enum.TryParse(value, true, out ThemePreference preference) && Enum.IsDefined(preference)
                ? preference
                : ThemePreference.System;

        internal static bool ResolveDark(ThemePreference preference, bool systemDark) => preference switch
        {
            ThemePreference.Dark => true,
            ThemePreference.Light => false,
            _ => systemDark
        };

        public static void Start(ThemePreference preference)
        {
            _preference = preference;
            if (!_watching)
            {
                _watching = true;
                _windowDiscoveryTimer = new System.Windows.Forms.Timer { Interval = 100 };
                _windowDiscoveryTimer.Tick += DiscoverWindows;
                _windowDiscoveryTimer.Start();
            }
            if (!_watchingSystemTheme)
            {
                _watchingSystemTheme = true;
                SystemEvents.UserPreferenceChanged += SystemPreferenceChanged;
            }
            _dark = ResolveDark(_preference, IsSystemDark());
            ApplyToOpenWindows();
        }

        internal static void SetThemePreference(ThemePreference preference)
        {
            _preference = preference;
            _dark = ResolveDark(_preference, IsSystemDark());
            ApplyToOpenWindows();
        }

        private static void SystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (_preference != ThemePreference.System) return;

            var dark = ResolveDark(_preference, IsSystemDark());
            if (_dark == dark) return;

            _dark = dark;
            ApplyToOpenWindows();
        }

        private static bool IsSystemDark() => Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme", 1) is int appsUseLightTheme && appsUseLightTheme == 0;

        internal static void ApplyToForm(Form form)
        {
            if (WatchedForms.Add(form))
            {
                form.Shown += FormShown;
                form.FormClosed += (s, args) => WatchedForms.Remove(form);
            }

            CaptureTree(form);
            Apply(form);
        }

        public static Color GetReadableTextColor(Color background) =>
            IsDarkColor(background) ? Text : Color.FromArgb(35, 35, 35);

        private static Color Blend(Color baseColor, Color accent, float accentWeight) => Color.FromArgb(
            (int)(baseColor.R * (1 - accentWeight) + accent.R * accentWeight),
            (int)(baseColor.G * (1 - accentWeight) + accent.G * accentWeight),
            (int)(baseColor.B * (1 - accentWeight) + accent.B * accentWeight));

        internal static Image CreateDarkModeImage(Image source)
        {
            var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.DrawImage(source, 0, 0, source.Width, source.Height);

            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.A == 0) continue;

                var range = Math.Max(color.R, Math.Max(color.G, color.B)) - Math.Min(color.R, Math.Min(color.G, color.B));
                if (range > 24) continue;

                var luminance = (color.R * 299 + color.G * 587 + color.B * 114) / 1000;
                var alpha = color.A * (255 - luminance) / 255;
                bitmap.SetPixel(x, y, Color.FromArgb(alpha, 210, 215, 225));
            }

            return bitmap;
        }

        private static void DiscoverWindows(object sender, EventArgs e)
        {
            foreach (Form form in Application.OpenForms)
            {
                if (!WatchedForms.Add(form)) continue;
                form.Shown += FormShown;
                form.FormClosed += (s, args) => WatchedForms.Remove(form);
                if (_dark)
                {
                    CaptureTree(form);
                    Apply(form);
                }
            }
        }

        private static void FormShown(object sender, EventArgs e)
        {
            if (_dark && sender is Form form)
            {
                CaptureTree(form);
                Apply(form);
            }
        }

        private static void ApplyToOpenWindows()
        {
            foreach (Form form in Application.OpenForms.Cast<Form>().ToArray())
            {
                if (form.IsDisposed) continue;

                void UpdateWindow()
                {
                    if (form.IsDisposed) return;
                    CaptureTree(form);
                    Apply(form);
                }

                if (form.IsHandleCreated && form.InvokeRequired)
                {
                    try { form.BeginInvoke((Action)UpdateWindow); }
                    catch (ObjectDisposedException) { }
                    catch (InvalidOperationException) { }
                }
                else
                    UpdateWindow();
            }
        }

        private static void CaptureTree(Control control)
        {
            OriginalStyles.GetValue(control, c => new OriginalControlStyle(c));
            if (control is ToolStrip strip)
                foreach (ToolStripItem item in strip.Items) CaptureItemTree(item);
            if (control.ContextMenuStrip is { } contextMenu)
            {
                OriginalStyles.GetValue(contextMenu, c => new OriginalControlStyle(c));
                foreach (ToolStripItem item in contextMenu.Items) CaptureItemTree(item);
            }
            foreach (Control child in control.Controls) CaptureTree(child);
        }

        private static void CaptureItemTree(ToolStripItem item)
        {
            OriginalItemStyles.GetValue(item, i => new OriginalItemStyle(i));
            if (item is ToolStripDropDownItem dropDown)
                foreach (ToolStripItem child in dropDown.DropDownItems) CaptureItemTree(child);
        }

        private static void Apply(Control control)
        {
            var original = OriginalStyles.GetValue(control, c => new OriginalControlStyle(c));

            if (!_dark)
            {
                control.BackColor = original.BackColor;
                control.ForeColor = original.ForeColor;
                if (control is LinkLabel linkToRestore)
                {
                    linkToRestore.LinkColor = original.LinkColor;
                    linkToRestore.ActiveLinkColor = original.ActiveLinkColor;
                    linkToRestore.VisitedLinkColor = original.VisitedLinkColor;
                }
                if (control is Button buttonToRestore)
                {
                    buttonToRestore.UseVisualStyleBackColor = original.UseVisualStyleBackColor;
                    buttonToRestore.FlatStyle = original.FlatStyle;
                }
                if (control is TabPage pageToRestore)
                    pageToRestore.UseVisualStyleBackColor = original.TabPageUsesVisualStyles;
                if (control is ToolStrip stripToRestore)
                {
                    stripToRestore.Renderer = original.Renderer;
                    stripToRestore.RenderMode = original.RenderMode;
                    foreach (ToolStripItem item in stripToRestore.Items) Apply(item);
                }
                if (control is TabControl tabsToRestore)
                {
                    tabsToRestore.DrawItem -= original.TabDrawHandler;
                    tabsToRestore.DrawMode = original.TabDrawMode;
                }
                if (control is BrightIdeasSoftware.ObjectListView listToRestore)
                {
                    listToRestore.SelectedBackColor = original.SelectedBackColor;
                    listToRestore.SelectedForeColor = original.SelectedForeColor;
                    listToRestore.UnfocusedSelectedBackColor = original.UnfocusedSelectedBackColor;
                    listToRestore.UnfocusedSelectedForeColor = original.UnfocusedSelectedForeColor;
                    listToRestore.HeaderUsesThemes = original.HeaderUsesThemes;
                    listToRestore.HeaderFormatStyle = original.HeaderFormatStyle;
                    RefreshListRows(listToRestore);
                }
                if (control is TreeMap treeMapToRestore)
                    treeMapToRestore.SelectedObjectColor = original.SelectedObjectColor;
                if (IsProductNameLabel(control) && control is Label labelToRestore)
                    labelToRestore.Enabled = original.Enabled;
                if (control is DataGridView gridToRestore && original.GridStyles != null)
                {
                    gridToRestore.BackgroundColor = original.GridBackgroundColor;
                    gridToRestore.DefaultCellStyle = original.GridStyles;
                    gridToRestore.ColumnHeadersDefaultCellStyle = original.GridHeaderStyles;
                    gridToRestore.EnableHeadersVisualStyles = original.GridUsesVisualStyles;
                }
            }
            else
            {
                if (control is Form)
                    control.BackColor = Window;
                else if (IsInputSurface(control))
                    control.BackColor = Surface;
                else if (IsStandardSurface(original.BackColor))
                    control.BackColor = Surface;
                else
                    control.BackColor = original.BackColor;

                if (control is Form)
                    control.ForeColor = Text;
                else if (control is Label || control is CheckBox || control is RadioButton || control is LinkLabel ||
                    control is ButtonBase || control is GroupBox || control is TabPage)
                {
                    var background = IsInputSurface(control) ? Surface : EffectiveOriginalBackColor(control);
                    if (IsStandardSurface(background)) background = control is Form ? Window : Surface;
                    control.ForeColor = IsDarkColor(background) ? Text : Color.FromArgb(35, 35, 35);
                }
                else
                    control.ForeColor = original.ForeColor;

                // The sidebar product name is intentionally disabled in the light theme.
                // WinForms dims disabled labels and ignores their ForeColor, so enable it
                // while dark mode is active to keep the name legible on the dark panel.
                if (IsProductNameLabel(control) && control is Label label)
                    label.Enabled = true;

                if (control is Button button)
                {
                    button.UseVisualStyleBackColor = false;
                    button.BackColor = Surface;
                    button.ForeColor = Text;
                    button.FlatStyle = FlatStyle.Flat;
                }

                if (control is LinkLabel link)
                {
                    link.LinkColor = Color.FromArgb(86, 156, 214);
                    link.ActiveLinkColor = Color.FromArgb(130, 190, 240);
                    link.VisitedLinkColor = Color.FromArgb(190, 150, 220);
                }

                if (IsInputSurface(control))
                {
                    control.BackColor = Surface;
                    control.ForeColor = Text;
                }

                // TabPage defaults to a themed white page even when BackColor is set.
                if (control is TabPage page)
                {
                    page.UseVisualStyleBackColor = false;
                    page.BackColor = Surface;
                }

                if (control is BrightIdeasSoftware.ObjectListView list)
                {
                    list.SelectedBackColor = Selection;
                    list.SelectedForeColor = Text;
                    list.UnfocusedSelectedBackColor = Selection;
                    list.UnfocusedSelectedForeColor = Text;
                    list.HeaderUsesThemes = false;
                    list.HeaderFormatStyle = CreateHeaderFormatStyle();
                    RefreshListRows(list);
                }

                if (control is DataGridView grid)
                {
                    grid.BackgroundColor = Surface;
                    grid.DefaultCellStyle.BackColor = Surface;
                    grid.DefaultCellStyle.ForeColor = Text;
                    grid.DefaultCellStyle.SelectionBackColor = Selection;
                    grid.DefaultCellStyle.SelectionForeColor = Text;
                    grid.ColumnHeadersDefaultCellStyle.BackColor = Window;
                    grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
                    grid.EnableHeadersVisualStyles = false;
                }

                if (control is ToolStrip strip)
                {
                    strip.BackColor = Surface;
                    strip.ForeColor = Text;
                    strip.Renderer = new ToolStripProfessionalRenderer(new ThemeColorTable(true));
                    strip.RenderMode = ToolStripRenderMode.Custom;
                    foreach (ToolStripItem item in strip.Items) Apply(item);
                }

                if (control is TabControl tabs)
                {
                    tabs.BackColor = Surface;
                    tabs.ForeColor = Text;
                    tabs.DrawItem -= original.TabDrawHandler;
                    tabs.DrawItem += original.TabDrawHandler;
                    tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
                    tabs.Invalidate();
                }

                if (control is TreeMap treeMap)
                    treeMap.SelectedObjectColor = Selection;
            }

            if (control.ContextMenuStrip is { } contextMenuStrip)
                Apply(contextMenuStrip);

            foreach (Control child in control.Controls) Apply(child);

            if (control is Form form && form.IsHandleCreated)
                ApplyCaptionTheme(form.Handle, _dark);

            if (control is BrightIdeasSoftware.ObjectListView themedList && themedList.IsHandleCreated)
            {
                themedList.HeaderControl.Invalidate();
                themedList.Invalidate();
            }
        }

        private static HeaderFormatStyle CreateHeaderFormatStyle()
        {
            var style = new HeaderFormatStyle();
            style.SetBackColor(Surface);
            style.SetForeColor(Text);
            style.Hot.BackColor = HeaderHot;
            style.Pressed.BackColor = Selection;
            return style;
        }

        private static void DrawDarkTab(object sender, DrawItemEventArgs e)
        {
            if (sender is not TabControl tabs || e.Index < 0 || e.Index >= tabs.TabPages.Count) return;

            var background = e.Index == tabs.SelectedIndex ? Surface : Window;
            using (var brush = new SolidBrush(background))
                e.Graphics.FillRectangle(brush, e.Bounds);
            using (var pen = new Pen(HeaderHot))
                e.Graphics.DrawRectangle(pen, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);

            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, e.Bounds, Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }

        private static void RefreshListRows(BrightIdeasSoftware.ObjectListView list)
        {
            if (list.VirtualMode || list.Objects == null)
            {
                list.Invalidate();
                return;
            }

            var objects = ((IEnumerable)list.Objects).Cast<object>().ToArray();
            if (objects.Length > 0)
                list.RefreshObjects(objects);
            else
                list.Invalidate();
        }

        private static void ApplyCaptionTheme(IntPtr windowHandle, bool dark)
        {
            var value = dark ? 1 : 0;
            try
            {
                // Windows 10 20H1 and later use attribute 20. Earlier builds used 19.
                var result = DwmSetWindowAttribute(windowHandle, 20, ref value, sizeof(int));
                if (result != 0)
                    DwmSetWindowAttribute(windowHandle, 19, ref value, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int value, int valueSize);

        private static bool IsStandardSurface(Color color) => color == SystemColors.Control ||
            color == SystemColors.ControlLight || color == SystemColors.ControlLightLight ||
            color == SystemColors.Window || color == Color.White ||
            color == Window || color == Surface;

        private static void Apply(ToolStripItem item)
        {
            var original = OriginalItemStyles.GetValue(item, i => new OriginalItemStyle(i));
            item.BackColor = _dark ? Surface : original.BackColor;
            item.ForeColor = _dark ? Text : original.ForeColor;
            item.Image = _dark ? original.GetDarkModeImage() : original.Image;
            if (item is ToolStripDropDownItem dropDown)
                foreach (ToolStripItem child in dropDown.DropDownItems)
                    Apply(child);
        }

        private static bool IsInputSurface(Control control) => control is TextBoxBase || control is ComboBox ||
            control is ListBox || control is ListView || control is TreeView;

        private static bool IsProductNameLabel(Control control) => control is Label { Name: "label1", Parent.Name: "settingsSidebarPanel" };

        private static bool IsDarkColor(Color color) => color.R * 299 + color.G * 587 + color.B * 114 < 145000;

        private static Color EffectiveOriginalBackColor(Control control)
        {
            for (var current = control; current != null; current = current.Parent)
            {
                var backColor = OriginalStyles.GetValue(current, c => new OriginalControlStyle(c)).BackColor;
                if (backColor != Color.Transparent) return backColor;
            }

            return Window;
        }

        private sealed class OriginalControlStyle
        {
            public OriginalControlStyle(Control control)
            {
                BackColor = control.BackColor;
                ForeColor = control.ForeColor;
                Enabled = control.Enabled;
                if (control is Button button)
                {
                    FlatStyle = button.FlatStyle;
                    UseVisualStyleBackColor = button.UseVisualStyleBackColor;
                }
                if (control is ToolStrip strip)
                {
                    Renderer = strip.Renderer;
                    RenderMode = strip.RenderMode;
                }
                if (control is TabControl tabs)
                {
                    TabDrawMode = tabs.DrawMode;
                    TabDrawHandler = DrawDarkTab;
                }
                if (control is TabPage page)
                    TabPageUsesVisualStyles = page.UseVisualStyleBackColor;
                if (control is LinkLabel link)
                {
                    LinkColor = link.LinkColor;
                    ActiveLinkColor = link.ActiveLinkColor;
                    VisitedLinkColor = link.VisitedLinkColor;
                }
                if (control is BrightIdeasSoftware.ObjectListView list)
                {
                    SelectedBackColor = list.SelectedBackColor;
                    SelectedForeColor = list.SelectedForeColor;
                    UnfocusedSelectedBackColor = list.UnfocusedSelectedBackColor;
                    UnfocusedSelectedForeColor = list.UnfocusedSelectedForeColor;
                    HeaderUsesThemes = list.HeaderUsesThemes;
                    HeaderFormatStyle = list.HeaderFormatStyle;
                }
                if (control is TreeMap treeMap)
                    SelectedObjectColor = treeMap.SelectedObjectColor;
                if (control is DataGridView grid)
                {
                    GridBackgroundColor = grid.BackgroundColor;
                    GridStyles = grid.DefaultCellStyle.Clone();
                    GridHeaderStyles = grid.ColumnHeadersDefaultCellStyle.Clone();
                    GridUsesVisualStyles = grid.EnableHeadersVisualStyles;
                }
            }

            public Color BackColor { get; }
            public Color ForeColor { get; }
            public bool Enabled { get; }
            public FlatStyle FlatStyle { get; }
            public bool UseVisualStyleBackColor { get; }
            public ToolStripRenderer Renderer { get; }
            public ToolStripRenderMode RenderMode { get; }
            public TabDrawMode TabDrawMode { get; }
            public DrawItemEventHandler TabDrawHandler { get; }
            public bool TabPageUsesVisualStyles { get; }
            public Color LinkColor { get; }
            public Color ActiveLinkColor { get; }
            public Color VisitedLinkColor { get; }
            public Color SelectedBackColor { get; }
            public Color SelectedForeColor { get; }
            public Color UnfocusedSelectedBackColor { get; }
            public Color UnfocusedSelectedForeColor { get; }
            public bool HeaderUsesThemes { get; }
            public HeaderFormatStyle HeaderFormatStyle { get; }
            public Color SelectedObjectColor { get; }
            public Color GridBackgroundColor { get; }
            public DataGridViewCellStyle GridStyles { get; }
            public DataGridViewCellStyle GridHeaderStyles { get; }
            public bool GridUsesVisualStyles { get; }
        }

        private sealed class OriginalItemStyle
        {
            public OriginalItemStyle(ToolStripItem item)
            {
                BackColor = item.BackColor;
                ForeColor = item.ForeColor;
                Image = item.Image;
            }

            public Image GetDarkModeImage() => Image == null ? null : DarkModeImage ??= CreateDarkModeImage(Image);
            public Color BackColor { get; }
            public Color ForeColor { get; }
            public Image Image { get; }
            private Image DarkModeImage { get; set; }
        }

        private sealed class ThemeColorTable : ProfessionalColorTable
        {
            private readonly bool _dark;
            public ThemeColorTable(bool dark) => _dark = dark;
            private Color Base => _dark ? Surface : SystemColors.Control;
            private Color Border => _dark ? Color.FromArgb(75, 75, 78) : SystemColors.ControlDark;
            public override Color ToolStripGradientBegin => Base;
            public override Color ToolStripGradientMiddle => Base;
            public override Color ToolStripGradientEnd => Base;
            public override Color MenuStripGradientBegin => Base;
            public override Color MenuStripGradientEnd => Base;
            public override Color StatusStripGradientBegin => Base;
            public override Color StatusStripGradientEnd => Base;
            public override Color MenuItemSelected => _dark ? Selection : SystemColors.Highlight;
            public override Color MenuItemBorder => Border;
            public override Color MenuBorder => Border;
            public override Color SeparatorDark => Border;
            public override Color SeparatorLight => Base;
        }
    }
}
