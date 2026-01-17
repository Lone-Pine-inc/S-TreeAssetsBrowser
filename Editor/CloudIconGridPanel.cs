using Editor;
using Sandbox;
using System;
using System.Collections.Generic;

namespace GeneralGame.Editor;

/// <summary>
/// Panel displaying cloud assets as large icons in a grid layout.
/// </summary>
public class CloudIconGridPanel : Widget, IBrowserPanel
{
    private Widget _toolbar;
    private Label _titleLabel;
    private IconButton _closeBtn;
    private IconButton _moveLeftBtn;
    private IconButton _moveRightBtn;
    private ScrollArea _scrollArea;
    private CloudGridCanvas _gridCanvas;

    public Action OnCloseRequested { get; set; }
    public Action OnMoveLeftRequested { get; set; }
    public Action OnMoveRightRequested { get; set; }

    public bool ShowCloseButton
    {
        get => _closeBtn?.Visible ?? false;
        set
        {
            if (_closeBtn != null)
                _closeBtn.Visible = value;
        }
    }

    public bool ShowMoveLeftButton
    {
        get => _moveLeftBtn?.Visible ?? false;
        set
        {
            if (_moveLeftBtn != null)
                _moveLeftBtn.Visible = value;
        }
    }

    public bool ShowMoveRightButton
    {
        get => _moveRightBtn?.Visible ?? false;
        set
        {
            if (_moveRightBtn != null)
                _moveRightBtn.Visible = value;
        }
    }

    public CloudIconGridPanel(Widget parent) : base(parent)
    {
        SetSizeMode(SizeMode.CanGrow, SizeMode.CanGrow);
        CreateUI();
    }

    private void CreateUI()
    {
        Layout = Layout.Column();
        Layout.Spacing = 4;
        Layout.Margin = 4;

        // Toolbar
        _toolbar = Layout.Add(new Widget(this));
        _toolbar.Layout = Layout.Row();
        _toolbar.Layout.Spacing = 4;
        _toolbar.FixedHeight = 28;

        // Cloud icon
        var cloudIcon = _toolbar.Layout.Add(new Label("cloud", this));
        cloudIcon.SetStyles("font-family: Material Icons; font-size: 16px; color: #888;");
        cloudIcon.FixedWidth = 20;

        _titleLabel = _toolbar.Layout.Add(new Label("Cloud Assets Preview", this));
        _titleLabel.SetStyles("color: #888; font-size: 11px;");

        _toolbar.Layout.AddStretchCell();

        _moveLeftBtn = _toolbar.Layout.Add(new IconButton("chevron_left"));
        _moveLeftBtn.ToolTip = "Move Panel Left";
        _moveLeftBtn.Background = Color.Transparent;
        _moveLeftBtn.OnClick = () => OnMoveLeftRequested?.Invoke();
        _moveLeftBtn.Visible = false;

        _moveRightBtn = _toolbar.Layout.Add(new IconButton("chevron_right"));
        _moveRightBtn.ToolTip = "Move Panel Right";
        _moveRightBtn.Background = Color.Transparent;
        _moveRightBtn.OnClick = () => OnMoveRightRequested?.Invoke();
        _moveRightBtn.Visible = false;

        _closeBtn = _toolbar.Layout.Add(new IconButton("close"));
        _closeBtn.ToolTip = "Close Panel";
        _closeBtn.Background = Color.Transparent;
        _closeBtn.OnClick = () => OnCloseRequested?.Invoke();
        _closeBtn.Visible = false;

        // Scroll area with custom canvas
        _scrollArea = Layout.Add(new ScrollArea(this));
        _scrollArea.SetSizeMode(SizeMode.CanGrow, SizeMode.CanGrow);

        _gridCanvas = new CloudGridCanvas(_scrollArea);
        _scrollArea.Canvas = _gridCanvas;
    }

    /// <summary>
    /// Show cloud packages in the grid
    /// </summary>
    public void ShowPackages(List<Package> packages)
    {
        _gridCanvas.LoadPackages(packages);
        _titleLabel.Text = $"Cloud Assets ({packages.Count})";
    }

    /// <summary>
    /// Clear the grid
    /// </summary>
    public void Clear()
    {
        _gridCanvas.Clear();
        _titleLabel.Text = "Cloud Assets Preview";
    }
}

/// <summary>
/// Canvas that draws cloud package icons
/// </summary>
internal class CloudGridCanvas : Widget
{
    private List<CloudGridItem> _items = new();
    private int _hoveredIndex = -1;
    private int _selectedIndex = -1;

    private const float ItemWidth = 100;
    private const float ItemHeight = 120;
    private const float IconSize = 64;
    private const float Spacing = 8;
    private const float Padding = 8;

    public CloudGridCanvas(Widget parent) : base(parent)
    {
        MinimumHeight = 100;
    }

    public void Clear()
    {
        _items.Clear();
        _hoveredIndex = -1;
        _selectedIndex = -1;
        MinimumHeight = 100;
        Update();
    }

    public void LoadPackages(List<Package> packages)
    {
        _items.Clear();
        _hoveredIndex = -1;
        _selectedIndex = -1;

        foreach (var pkg in packages)
        {
            _items.Add(new CloudGridItem
            {
                Package = pkg,
                Title = pkg.Title ?? pkg.FullIdent,
                Author = pkg.Org?.Title ?? "Unknown",
                FullIdent = pkg.FullIdent,
                Thumb = pkg.Thumb
            });
        }

        UpdateSize();
        Update();
    }

    private void UpdateSize()
    {
        if (_items == null || _items.Count == 0)
        {
            MinimumHeight = 100;
            return;
        }

        float width = 400;
        try { width = Parent?.Width ?? 400; } catch { }
        if (width <= 0) width = 400;

        int columns = Math.Max(1, (int)((width - Padding * 2 + Spacing) / (ItemWidth + Spacing)));
        int rows = (_items.Count + columns - 1) / columns;

        float height = Padding * 2 + rows * ItemHeight + Math.Max(0, rows - 1) * Spacing;
        MinimumHeight = Math.Max(100, height);
    }

    protected override void OnPaint()
    {
        if (_items == null || _items.Count == 0)
        {
            // Draw placeholder
            Paint.SetPen(Theme.Text.WithAlpha(0.3f));
            Paint.SetDefaultFont(10, 400);
            Paint.DrawText(LocalRect, "Select a category in the Cloud Assets tree", TextFlag.Center);
            return;
        }

        float width = Width;
        if (width <= 0) width = 400;

        int columns = Math.Max(1, (int)((width - Padding * 2 + Spacing) / (ItemWidth + Spacing)));

        for (int i = 0; i < _items.Count; i++)
        {
            int col = i % columns;
            int row = i / columns;

            float x = Padding + col * (ItemWidth + Spacing);
            float y = Padding + row * (ItemHeight + Spacing);

            var itemRect = new Rect(x, y, ItemWidth, ItemHeight);
            DrawItem(i, itemRect);
        }
    }

    private void DrawItem(int index, Rect rect)
    {
        var item = _items[index];

        // Background
        if (index == _selectedIndex)
        {
            Paint.SetBrush(Theme.Primary.WithAlpha(0.3f));
            Paint.DrawRect(rect, 4);
        }
        else if (index == _hoveredIndex)
        {
            Paint.SetBrush(Color.White.WithAlpha(0.05f));
            Paint.DrawRect(rect, 4);
        }

        // Icon area
        var iconRect = new Rect(
            rect.Left + (rect.Width - IconSize) / 2,
            rect.Top + 4,
            IconSize,
            IconSize
        );

        // Draw thumbnail if available, otherwise icon based on package type
        if (!string.IsNullOrEmpty(item.Thumb) && item.Thumb.StartsWith("http"))
        {
            Paint.Draw(iconRect, item.Thumb);
        }
        else
        {
            Paint.SetBrush(Theme.WidgetBackground);
            Paint.DrawRect(iconRect, 4);

            var icon = GetIconForType(item.Package.PackageType);
            var iconColor = GetColorForType(item.Package.PackageType);
            Paint.SetPen(iconColor);
            Paint.DrawIcon(iconRect, icon, 48, TextFlag.Center);
        }

        // Title
        var titleRect = new Rect(rect.Left + 2, iconRect.Bottom + 2, rect.Width - 4, 16);
        Paint.SetPen(Theme.Text);
        Paint.SetDefaultFont(8, 500);

        var title = item.Title;
        if (title.Length > 14) title = title.Substring(0, 12) + "..";
        Paint.DrawText(titleRect, title, TextFlag.Center);

        // Author
        var authorRect = new Rect(rect.Left + 2, titleRect.Bottom, rect.Width - 4, 14);
        Paint.SetPen(Theme.Text.WithAlpha(0.5f));
        Paint.SetDefaultFont(7, 400);

        var author = item.Author;
        if (author.Length > 16) author = author.Substring(0, 14) + "..";
        Paint.DrawText(authorRect, author, TextFlag.Center);
    }

    private int GetItemAtPosition(Vector2 pos)
    {
        if (_items == null || _items.Count == 0)
            return -1;

        float width = Width;
        if (width <= 0) return -1;

        int columns = Math.Max(1, (int)((width - Padding * 2 + Spacing) / (ItemWidth + Spacing)));

        int col = (int)((pos.x - Padding) / (ItemWidth + Spacing));
        int row = (int)((pos.y - Padding) / (ItemHeight + Spacing));

        if (col < 0 || col >= columns) return -1;

        int index = row * columns + col;
        if (index < 0 || index >= _items.Count) return -1;

        return index;
    }

    private Vector2 _dragStartPos;
    private bool _isDragging;
    private int _dragItemIndex = -1;

    protected override void OnMouseMove(MouseEvent e)
    {
        base.OnMouseMove(e);
        int newHover = GetItemAtPosition(e.LocalPosition);
        if (newHover != _hoveredIndex)
        {
            _hoveredIndex = newHover;
            Update();
        }

        // Handle drag
        if (e.ButtonState.HasFlag(MouseButtons.Left) && !_isDragging && _dragItemIndex >= 0)
        {
            var delta = e.LocalPosition - _dragStartPos;
            if (delta.Length > 5)
            {
                _isDragging = true;
                StartDrag(_dragItemIndex);
            }
        }
    }

    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();
        _hoveredIndex = -1;
        Update();
    }

    protected override void OnMousePress(MouseEvent e)
    {
        base.OnMousePress(e);
        if (e.LeftMouseButton)
        {
            int index = GetItemAtPosition(e.LocalPosition);
            _dragStartPos = e.LocalPosition;
            _isDragging = false;
            _dragItemIndex = index;

            if (index >= 0)
            {
                _selectedIndex = index;
                Update();
            }
        }
    }

    private void StartDrag(int index)
    {
        if (index < 0 || index >= _items.Count) return;
        var item = _items[index];

        var drag = new Drag(this);
        drag.Data.Text = item.FullIdent;

        // Set thumbnail URL if available
        if (!string.IsNullOrEmpty(item.Thumb))
        {
            drag.Data.Url = new Uri(item.Thumb);
        }

        drag.Execute();
    }

    protected override void OnDoubleClick(MouseEvent e)
    {
        base.OnDoubleClick(e);
        if (e.LeftMouseButton)
        {
            int index = GetItemAtPosition(e.LocalPosition);
            if (index >= 0)
            {
                var item = _items[index];
                // Open package page in browser
                var url = $"https://sbox.game/{item.FullIdent}";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
        }
    }

    protected override void OnContextMenu(ContextMenuEvent e)
    {
        int index = GetItemAtPosition(e.LocalPosition);
        if (index < 0) return;

        var item = _items[index];
        var menu = new ContextMenu(this);

        menu.AddOption("Open in Browser", "open_in_new", () =>
        {
            var url = $"https://sbox.game/{item.FullIdent}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        });

        menu.AddOption("Copy Identifier", "content_copy", () =>
        {
            EditorUtility.Clipboard.Copy(item.FullIdent);
        });

        menu.OpenAtCursor();
        e.Accepted = true;
    }

    private static string GetIconForType(Package.Type type)
    {
        return type switch
        {
            Package.Type.Model => "view_in_ar",
            Package.Type.Material => "texture",
            Package.Type.Sound => "audiotrack",
            Package.Type.Map => "landscape",
            _ => "cloud_download"
        };
    }

    private static Color GetColorForType(Package.Type type)
    {
        return type switch
        {
            Package.Type.Model => new Color(0.9f, 0.6f, 0.3f),
            Package.Type.Material => new Color(0.9f, 0.4f, 0.6f),
            Package.Type.Sound => new Color(0.4f, 0.7f, 1.0f),
            Package.Type.Map => new Color(0.9f, 0.9f, 0.3f),
            _ => Theme.Text.WithAlpha(0.7f)
        };
    }

    private class CloudGridItem
    {
        public Package Package;
        public string Title;
        public string Author;
        public string FullIdent;
        public string Thumb;
    }
}
