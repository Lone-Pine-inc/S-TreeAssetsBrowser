using Editor;
using Sandbox;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GeneralGame.Editor;

/// <summary>
/// Panel displaying folder contents or cloud assets as large icons in a grid layout.
/// </summary>
public class IconGridPanel : Widget, IBrowserPanel
{
    private Widget _toolbar;
    private Label _pathLabel;
    private IconButton _closeBtn;
    private IconButton _upBtn;
    private IconButton _moveLeftBtn;
    private IconButton _moveRightBtn;
    private ScrollArea _scrollArea;
    private IconGridCanvas _gridCanvas;

    private string _currentFolder;
    private bool _isShowingCloud;
    private FileSystemWatcher _watcher;

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

    public IconGridPanel(Widget parent) : base(parent)
    {
        SetSizeMode(SizeMode.CanGrow, SizeMode.CanGrow);
        CreateUI();
        SetupFileWatcher();
    }

    private void SetupFileWatcher()
    {
        _watcher = new FileSystemWatcher();
        _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite;
        _watcher.Changed += OnFileSystemChanged;
        _watcher.Created += OnFileSystemChanged;
        _watcher.Deleted += OnFileSystemChanged;
        _watcher.Renamed += OnFileSystemChanged;
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        // Queue refresh on main thread
        MainThread.Queue(() =>
        {
            if (!string.IsNullOrEmpty(_currentFolder) && Directory.Exists(_currentFolder))
            {
                _gridCanvas?.LoadFolder(_currentFolder);
            }
        });
    }

    public override void OnDestroyed()
    {
        base.OnDestroyed();
        _watcher?.Dispose();
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

        _upBtn = _toolbar.Layout.Add(new IconButton("arrow_upward"));
        _upBtn.ToolTip = "Go to Parent Folder";
        _upBtn.Background = Color.Transparent;
        _upBtn.OnClick = GoUp;
        _upBtn.Enabled = false;

        _pathLabel = _toolbar.Layout.Add(new Label("Select a folder...", this));
        _pathLabel.SetStyles("color: #888; font-size: 11px;");

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

        _gridCanvas = new IconGridCanvas(_scrollArea);
        _gridCanvas.OnFolderNavigate = ShowFolder;
        _scrollArea.Canvas = _gridCanvas;
    }

    /// <summary>
    /// Show local folder contents
    /// </summary>
    public void ShowFolder(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            return;

        _currentFolder = Path.GetFullPath(folderPath);
        _isShowingCloud = false;

        // Update file watcher to monitor this folder
        try
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Path = _currentFolder;
            _watcher.EnableRaisingEvents = true;
        }
        catch { /* Ignore watcher errors */ }

        // Update path label
        var displayPath = _currentFolder;
        var assetsPath = Project.Current?.GetAssetsPath();
        var codePath = Project.Current?.GetCodePath();
        var corePath = global::Editor.FileSystem.Root?.GetFullPath("/core/");
        var citizenPath = global::Editor.FileSystem.Root?.GetFullPath("/addons/citizen/assets/");

        if (!string.IsNullOrEmpty(assetsPath) && displayPath.StartsWith(assetsPath, StringComparison.OrdinalIgnoreCase))
            displayPath = "Assets" + displayPath.Substring(assetsPath.Length);
        else if (!string.IsNullOrEmpty(codePath) && displayPath.StartsWith(codePath, StringComparison.OrdinalIgnoreCase))
            displayPath = "Code" + displayPath.Substring(codePath.Length);
        else if (!string.IsNullOrEmpty(corePath) && displayPath.StartsWith(corePath, StringComparison.OrdinalIgnoreCase))
            displayPath = "Core" + displayPath.Substring(corePath.Length);
        else if (!string.IsNullOrEmpty(citizenPath) && displayPath.StartsWith(citizenPath, StringComparison.OrdinalIgnoreCase))
            displayPath = "Citizen" + displayPath.Substring(citizenPath.Length);

        _pathLabel.Text = displayPath.Replace("\\", "/");

        // Enable up button - allow navigation up unless we're at a root folder
        var parentDir = Directory.GetParent(_currentFolder);
        bool isAtRoot = false;

        if (!string.IsNullOrEmpty(assetsPath) && _currentFolder.Equals(assetsPath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            isAtRoot = true;
        else if (!string.IsNullOrEmpty(codePath) && _currentFolder.Equals(codePath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            isAtRoot = true;
        else if (!string.IsNullOrEmpty(corePath) && _currentFolder.Equals(corePath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            isAtRoot = true;
        else if (!string.IsNullOrEmpty(citizenPath) && _currentFolder.Equals(citizenPath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            isAtRoot = true;

        _upBtn.Enabled = parentDir != null && !isAtRoot;

        _gridCanvas.LoadFolder(_currentFolder);
    }

    /// <summary>
    /// Show cloud packages
    /// </summary>
    public void ShowCloudPackages(List<Package> packages, string title = "Cloud Assets")
    {
        _currentFolder = null;
        _isShowingCloud = true;
        _upBtn.Enabled = false;
        _pathLabel.Text = $"Cloud: {title} ({packages.Count})";

        // Disable file watcher for cloud view
        _watcher.EnableRaisingEvents = false;

        _gridCanvas.LoadCloudPackages(packages);
    }

    private void GoUp()
    {
        if (_isShowingCloud || string.IsNullOrEmpty(_currentFolder)) return;
        var parentDir = Directory.GetParent(_currentFolder);
        if (parentDir != null)
            ShowFolder(parentDir.FullName);
    }
}

/// <summary>
/// Canvas that draws all icons itself - supports both local files and cloud packages
/// </summary>
internal class IconGridCanvas : Widget
{
    private List<GridItem> _items = new();
    private int _hoveredIndex = -1;
    private int _selectedIndex = -1;

    public Action<string> OnFolderNavigate;

    private const float ItemWidth = 80;
    private const float ItemHeight = 100;
    private const float IconSize = 64;
    private const float Spacing = 8;
    private const float Padding = 8;

    public IconGridCanvas(Widget parent) : base(parent)
    {
        MinimumHeight = 100;
        AcceptDrops = true;
    }

    /// <summary>
    /// Load local folder contents
    /// </summary>
    public void LoadFolder(string folderPath)
    {
        _currentFolder = folderPath;
        _items.Clear();
        _hoveredIndex = -1;
        _selectedIndex = -1;

        try
        {
            // Folders
            foreach (var dir in Directory.GetDirectories(folderPath).OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
            {
                var dirInfo = new DirectoryInfo(dir);
                if (dirInfo.Attributes.HasFlag(FileAttributes.Hidden)) continue;
                if (dirInfo.Name.Equals("obj", StringComparison.OrdinalIgnoreCase)) continue;
                if (dirInfo.Name.StartsWith(".")) continue;

                _items.Add(new GridItem
                {
                    Path = dir,
                    Name = dirInfo.Name,
                    IsFolder = true,
                    IsCloud = false
                });
            }

            // Files
            foreach (var file in Directory.GetFiles(folderPath).OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.Attributes.HasFlag(FileAttributes.Hidden)) continue;
                if (fileInfo.Name.Contains(".generated", StringComparison.OrdinalIgnoreCase)) continue;
                if (fileInfo.Name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                if (fileInfo.Name.StartsWith(".")) continue;
                if (fileInfo.Name.EndsWith("_c") && File.Exists(file[..^2])) continue;

                var asset = AssetSystem.FindByPath(file);
                Pixmap thumb = null;
                try { thumb = asset?.GetAssetThumb(); } catch { }

                _items.Add(new GridItem
                {
                    Path = file,
                    Name = fileInfo.Name,
                    IsFolder = false,
                    IsCloud = false,
                    Asset = asset,
                    Thumbnail = thumb,
                    Extension = fileInfo.Extension.ToLowerInvariant()
                });
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"Error loading folder: {ex.Message}");
        }

        UpdateSize();
        Update();
    }

    /// <summary>
    /// Load cloud packages
    /// </summary>
    public void LoadCloudPackages(List<Package> packages)
    {
        _items.Clear();
        _hoveredIndex = -1;
        _selectedIndex = -1;

        foreach (var pkg in packages)
        {
            _items.Add(new GridItem
            {
                Name = pkg.Title ?? pkg.FullIdent,
                IsFolder = false,
                IsCloud = true,
                Package = pkg,
                Author = pkg.Org?.Title ?? "Unknown",
                CloudThumb = pkg.Thumb
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
            Paint.SetPen(Theme.Text.WithAlpha(0.3f));
            Paint.SetDefaultFont(10, 400);
            Paint.DrawText(LocalRect, "Select a folder or cloud category", TextFlag.Center);
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

        if (item.IsCloud)
        {
            // Cloud item - draw thumbnail if available, otherwise icon
            if (!string.IsNullOrEmpty(item.CloudThumb) && item.CloudThumb.StartsWith("http"))
            {
                Paint.Draw(iconRect, item.CloudThumb);
            }
            else
            {
                Paint.SetBrush(Theme.WidgetBackground);
                Paint.DrawRect(iconRect, 4);

                var icon = GetIconForPackageType(item.Package?.TypeName ?? "model");
                var color = GetColorForPackageType(item.Package?.TypeName ?? "model");
                Paint.SetPen(color);
                Paint.DrawIcon(iconRect, icon, 48, TextFlag.Center);
            }
        }
        else if (item.Thumbnail != null)
        {
            // Local file with thumbnail
            Paint.Draw(iconRect, item.Thumbnail);
        }
        else
        {
            // Local file/folder without thumbnail
            var icon = item.IsFolder ? "folder" : GetIconForExtension(item.Extension);
            var color = item.IsFolder ? Theme.Yellow : GetColorForExtension(item.Extension, item.Asset);
            Paint.SetPen(color);
            Paint.DrawIcon(iconRect, icon, IconSize, TextFlag.Center);
        }

        // Name
        var textRect = new Rect(rect.Left + 2, iconRect.Bottom + 2, rect.Width - 4, 16);
        Paint.SetPen(Theme.Text);
        Paint.SetDefaultFont(8, 400);

        var name = item.Name;
        if (name.Length > 12) name = name.Substring(0, 10) + "..";
        Paint.DrawText(textRect, name, TextFlag.Center);

        // Author for cloud items
        if (item.IsCloud && !string.IsNullOrEmpty(item.Author))
        {
            var authorRect = new Rect(rect.Left + 2, textRect.Bottom, rect.Width - 4, 12);
            Paint.SetPen(Theme.Text.WithAlpha(0.5f));
            Paint.SetDefaultFont(7, 400);

            var author = item.Author;
            if (author.Length > 14) author = author.Substring(0, 12) + "..";
            Paint.DrawText(authorRect, author, TextFlag.Center);
        }
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

        // Check if actually inside item bounds
        float x = Padding + col * (ItemWidth + Spacing);
        float y = Padding + row * (ItemHeight + Spacing);
        var itemRect = new Rect(x, y, ItemWidth, ItemHeight);
        if (!itemRect.IsInside(pos)) return -1;

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
                var item = _items[index];

                if (!item.IsCloud && !item.IsFolder && item.Asset != null)
                    EditorUtility.InspectorObject = item.Asset;

                Update();
            }
        }
    }

    private void StartDrag(int index)
    {
        if (index < 0 || index >= _items.Count) return;
        var item = _items[index];

        var drag = new Drag(this);

        if (item.IsCloud)
        {
            // Cloud item - set package FullIdent for scene drop
            drag.Data.Text = item.Package?.FullIdent ?? "";

            // Set thumbnail URL if available
            var thumb = item.Package?.Thumb;
            if (!string.IsNullOrEmpty(thumb))
            {
                drag.Data.Url = new Uri(thumb);
            }
        }
        else if (item.IsFolder)
        {
            drag.Data.Url = new Uri("file:///" + item.Path);
        }
        else if (item.Asset != null)
        {
            drag.Data.Text = item.Asset.RelativePath;
            drag.Data.Url = new Uri("file:///" + item.Asset.AbsolutePath);
        }
        else
        {
            drag.Data.Text = item.Path;
            drag.Data.Url = new Uri("file:///" + item.Path);
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

                if (item.IsCloud)
                {
                    // Open cloud package in browser
                    var url = $"https://sbox.game/{item.Package?.FullIdent}";
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                else if (item.IsFolder)
                {
                    OnFolderNavigate?.Invoke(item.Path);
                }
                else if (item.Asset != null)
                {
                    item.Asset.OpenInEditor();
                }
            }
        }
    }

    /// <summary>
    /// Handle drag hover - show appropriate drop cursor
    /// </summary>
    public override void OnDragHover(DragEvent ev)
    {
        base.OnDragHover(ev);

        ev.Action = DropAction.Ignore;

        // Only accept drops if we have a current folder to drop into
        if (string.IsNullOrEmpty(_currentFolder) || !Directory.Exists(_currentFolder))
            return;

        // Check if dragging over a folder item
        int index = GetItemAtPosition(ev.LocalPosition);
        if (index >= 0 && _items[index].IsFolder)
        {
            // Dropping onto a folder
            ev.Action = ev.HasCtrl ? DropAction.Copy : DropAction.Move;
            if (_hoveredIndex != index)
            {
                _hoveredIndex = index;
                Update();
            }
        }
        else if (ev.Data.HasFileOrFolder)
        {
            // Dropping into current folder
            ev.Action = ev.HasCtrl ? DropAction.Copy : DropAction.Move;
        }
    }

    /// <summary>
    /// Handle dropping files from Windows Explorer or other sources
    /// </summary>
    public override void OnDragDrop(DragEvent ev)
    {
        base.OnDragDrop(ev);

        if (!ev.Data.HasFileOrFolder)
            return;

        if (string.IsNullOrEmpty(_currentFolder) || !Directory.Exists(_currentFolder))
            return;

        // Check if dropping onto a folder item
        int index = GetItemAtPosition(ev.LocalPosition);
        string targetFolder = _currentFolder;

        if (index >= 0 && _items[index].IsFolder)
        {
            targetFolder = _items[index].Path;
        }

        // Process the drop
        bool isCopy = ev.HasCtrl;
        ev.Action = isCopy ? DropAction.Copy : DropAction.Move;

        foreach (var file in ev.Data.Files)
        {
            if (string.IsNullOrEmpty(file))
                continue;

            // Don't drop onto itself
            if (file.Equals(targetFolder, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var fileName = Path.GetFileName(file);
                var destPath = Path.Combine(targetFolder, fileName);

                // Don't overwrite if destination is same as source
                if (Path.GetFullPath(file).Equals(Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
                    continue;

                if (Directory.Exists(file))
                {
                    // It's a directory
                    if (isCopy)
                        CopyDirectory(file, destPath);
                    else
                        EditorUtility.RenameDirectory(file, destPath);
                }
                else if (File.Exists(file))
                {
                    // Check if it's a registered asset
                    var asset = AssetSystem.FindByPath(file);

                    if (asset != null && !asset.IsDeleted)
                    {
                        // Use EditorUtility for proper asset handling
                        if (isCopy)
                            EditorUtility.CopyAssetToDirectory(asset, targetFolder);
                        else
                            EditorUtility.MoveAssetToDirectory(asset, targetFolder);
                    }
                    else
                    {
                        // Regular file, not an asset
                        if (isCopy)
                            File.Copy(file, destPath, overwrite: false);
                        else
                            File.Move(file, destPath, overwrite: false);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to {(isCopy ? "copy" : "move")} '{file}': {ex.Message}");
            }
        }

        // Refresh the view
        LoadFolder(_currentFolder);
    }

    /// <summary>
    /// Recursively copy a directory
    /// </summary>
    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)));
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        }
    }

    protected override void OnContextMenu(ContextMenuEvent e)
    {
        int index = GetItemAtPosition(e.LocalPosition);
        var menu = new ContextMenu(this);

        // If clicking on empty space, show folder-level context menu
        if (index < 0)
        {
            if (!string.IsNullOrEmpty(_currentFolder))
            {
                var createMenu = menu.AddMenu("Create", "add");
                AssetCreator.AddOptions(createMenu, _currentFolder);

                menu.AddSeparator();
                menu.AddOption("Open in Explorer", "folder_open", () => EditorUtility.OpenFolder(_currentFolder));
                menu.AddOption("Refresh", "refresh", () => LoadFolder(_currentFolder));
            }
            menu.OpenAtCursor();
            e.Accepted = true;
            return;
        }

        var item = _items[index];

        if (item.IsCloud)
        {
            menu.AddOption("Open in Browser", "open_in_new", () =>
            {
                var url = $"https://sbox.game/{item.Package?.FullIdent}";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            });

            menu.AddOption("Copy Identifier", "content_copy", () =>
            {
                EditorUtility.Clipboard.Copy(item.Package?.FullIdent ?? "");
            });
        }
        else if (item.IsFolder)
        {
            menu.AddOption("Open", "folder_open", () => OnFolderNavigate?.Invoke(item.Path));
            menu.AddOption("Open in Explorer", "launch", () => EditorUtility.OpenFolder(item.Path));

            menu.AddSeparator();

            menu.AddOption("Rename", "edit", () => StartRename(index));

            menu.AddSeparator();

            menu.AddOption("Copy Path", "content_copy", () => EditorUtility.Clipboard.Copy(item.Path));

            menu.AddSeparator();

            // Create submenu inside folder
            var createMenu = menu.AddMenu("Create", "add");
            AssetCreator.AddOptions(createMenu, item.Path);

            menu.AddSeparator();

            menu.AddOption("Delete", "delete", () => DeleteFolder(item.Path, item.Name));
        }
        else
        {
            // Open options
            if (item.Asset != null)
            {
                menu.AddOption("Open in Editor", "edit", () => item.Asset.OpenInEditor());
            }
            else
            {
                menu.AddOption("Open", "open_in_new", () => EditorUtility.OpenFolder(item.Path));
            }
            menu.AddOption("Show in Explorer", "folder_open", () => EditorUtility.OpenFileFolder(item.Path));

            menu.AddSeparator();

            // Copy options
            if (item.Asset != null)
            {
                menu.AddOption("Copy Relative Path", "content_paste_go", () => EditorUtility.Clipboard.Copy(item.Asset.RelativePath));
            }
            menu.AddOption("Copy Absolute Path", "content_paste", () => EditorUtility.Clipboard.Copy(item.Path));

            // Asset-type specific options (Create Material, Create Texture, etc.)
            AssetContextMenuHelper.AddAssetTypeOptions(menu, item.Asset);

            menu.AddSeparator();

            // Edit options
            menu.AddOption("Rename", "edit", () => StartRename(_items.IndexOf(item)));
            menu.AddOption("Duplicate", "file_copy", () => DuplicateFile(item.Path));

            menu.AddSeparator();

            // Create submenu for quick asset creation in same folder
            var parentFolder = Path.GetDirectoryName(item.Path);
            if (!string.IsNullOrEmpty(parentFolder))
            {
                var createMenu = menu.AddMenu("Create", "add");
                AssetCreator.AddOptions(createMenu, parentFolder);
                menu.AddSeparator();
            }

            menu.AddOption("Delete", "delete", () => DeleteFile(item.Path, item.Name));
        }

        menu.OpenAtCursor();
        e.Accepted = true;
    }

    private string _currentFolder;

    private void StartRename(int index)
    {
        // For now, use a simple input dialog approach
        // In a full implementation, you'd want inline renaming
        if (index < 0 || index >= _items.Count) return;
        var item = _items[index];

        var dialog = new LineEditDialog("Rename", item.Name);
        dialog.OnConfirm = (newName) =>
        {
            if (string.IsNullOrWhiteSpace(newName) || newName == item.Name)
                return;

            try
            {
                var directory = Path.GetDirectoryName(item.Path);
                var newPath = Path.Combine(directory, newName);

                if (item.IsFolder)
                {
                    Directory.Move(item.Path, newPath);
                }
                else
                {
                    // Preserve extension if not provided
                    if (!Path.HasExtension(newName))
                    {
                        newName += item.Extension;
                        newPath = Path.Combine(directory, newName);
                    }
                    File.Move(item.Path, newPath);
                }

                LoadFolder(_currentFolder);
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to rename: {ex.Message}");
            }
        };
        dialog.Show();
    }


    private void DuplicateFile(string filePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            var nameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
            var extension = Path.GetExtension(filePath);

            var newName = $"{nameWithoutExt}_copy{extension}";
            var newPath = Path.Combine(directory, newName);

            var counter = 1;
            while (File.Exists(newPath))
            {
                newName = $"{nameWithoutExt}_copy{counter++}{extension}";
                newPath = Path.Combine(directory, newName);
            }

            File.Copy(filePath, newPath);
            LoadFolder(_currentFolder);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to duplicate file: {ex.Message}");
        }
    }

    private void DeleteFile(string filePath, string fileName)
    {
        var confirm = new PopupWindow(
            "Delete File",
            $"Are you sure you want to delete '{fileName}'?",
            "Cancel",
            new Dictionary<string, Action>()
            {
                { "Delete", () =>
                    {
                        try
                        {
                            File.Delete(filePath);
                            LoadFolder(_currentFolder);
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"Failed to delete file: {ex.Message}");
                        }
                    }
                }
            }
        );
        confirm.Show();
    }

    private void DeleteFolder(string folderPath, string folderName)
    {
        var confirm = new PopupWindow(
            "Delete Folder",
            $"Are you sure you want to delete '{folderName}'?\nAll contents will be deleted.",
            "Cancel",
            new Dictionary<string, Action>()
            {
                { "Delete", () =>
                    {
                        try
                        {
                            Directory.Delete(folderPath, recursive: true);
                            LoadFolder(_currentFolder);
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"Failed to delete folder: {ex.Message}");
                        }
                    }
                }
            }
        );
        confirm.Show();
    }

    private static string GetIconForExtension(string ext)
    {
        return ext switch
        {
            ".vmdl" or ".fbx" or ".obj" or ".gltf" or ".glb" => "view_in_ar",
            ".png" or ".jpg" or ".jpeg" or ".tga" or ".vtex" or ".psd" => "image",
            ".vmat" => "texture",
            ".wav" or ".mp3" or ".ogg" or ".vsnd" => "audiotrack",
            ".cs" or ".razor" => "code",
            ".scss" or ".css" => "style",
            ".shader" or ".shdrgrph" => "gradient",
            ".prefab" => "inventory_2",
            ".scene" => "landscape",
            ".json" or ".xml" => "data_object",
            ".txt" or ".md" => "description",
            ".item" => "category",
            _ => "description"
        };
    }

    private static Color GetColorForExtension(string ext, Asset asset)
    {
        if (asset?.AssetType?.Color != null && asset.AssetType.Color != default)
            return asset.AssetType.Color;

        return ext switch
        {
            ".cs" => new Color(0.4f, 0.7f, 1.0f),
            ".razor" => new Color(0.6f, 0.4f, 0.9f),
            ".shader" => new Color(0.3f, 0.9f, 0.5f),
            ".vmdl" => new Color(0.9f, 0.6f, 0.3f),
            ".vmat" => new Color(0.9f, 0.4f, 0.6f),
            ".prefab" => new Color(0.3f, 0.8f, 0.9f),
            ".scene" => new Color(0.9f, 0.9f, 0.3f),
            _ => Theme.Text.WithAlpha(0.7f)
        };
    }

    private static string GetIconForPackageType(string typeName)
    {
        return typeName switch
        {
            "model" => "view_in_ar",
            "material" => "texture",
            "sound" => "audiotrack",
            "map" => "landscape",
            _ => "cloud_download"
        };
    }

    private static Color GetColorForPackageType(string typeName)
    {
        return typeName switch
        {
            "model" => new Color(0.9f, 0.6f, 0.3f),
            "material" => new Color(0.9f, 0.4f, 0.6f),
            "sound" => new Color(0.4f, 0.7f, 1.0f),
            "map" => new Color(0.9f, 0.9f, 0.3f),
            _ => Theme.Text.WithAlpha(0.7f)
        };
    }

    private class GridItem
    {
        public string Path;
        public string Name;
        public bool IsFolder;
        public bool IsCloud;
        public Asset Asset;
        public Pixmap Thumbnail;
        public string Extension;
        public Package Package;
        public string Author;
        public string CloudThumb; // URL for cloud package thumbnail
    }
}

/// <summary>
/// Simple popup for entering text (used for renaming)
/// </summary>
internal class LineEditDialog : PopupWidget
{
    private LineEdit _lineEdit;
    public Action<string> OnConfirm;

    public LineEditDialog(string title, string initialText) : base(null)
    {
        Layout = Layout.Row();
        Layout.Margin = 4;
        Layout.Spacing = 4;

        _lineEdit = Layout.Add(new LineEdit(this));
        _lineEdit.Text = initialText;
        _lineEdit.MinimumWidth = 200;
        _lineEdit.SelectAll();
        _lineEdit.ReturnPressed += () =>
        {
            OnConfirm?.Invoke(_lineEdit.Text);
            Close();
        };

        var okBtn = Layout.Add(new Button("OK", this));
        okBtn.Clicked = () =>
        {
            OnConfirm?.Invoke(_lineEdit.Text);
            Close();
        };

        _lineEdit.Focus();
    }

    public new void Show()
    {
        OpenAtCursor();
        _lineEdit.Focus();
    }
}
