using Editor;
using Sandbox;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GeneralGame.Editor;

/// <summary>
/// Individual asset browser panel with its own toolbar and tree view.
/// Multiple panels can be displayed side by side in TreeAssetBrowser.
/// </summary>
public class AssetBrowserPanel : Widget, IBrowserPanel
{
    private AssetTreeView _treeView;
    private LineEdit _searchEdit;
    private IconButton _searchBtn;
    private IconButton _clearSearchBtn;
    private string _searchFilter = "";
    private Widget _toolbar;
    private IconButton _closeBtn;
    private IconButton _moveLeftBtn;
    private IconButton _moveRightBtn;
    private IconButton _expandAllBtn;
    private IconButton _collapseAllBtn;

    private string _assetsPath;
    private string _codePath;

    // Unique panel ID for this panel instance
    private string _panelId;

    // Store expanded folder paths to restore state after refresh/search
    private HashSet<string> _expandedPaths = new();
    private HashSet<string> _preSearchExpandedPaths = new();
    private bool _isSearchActive = false;

    // Cache of paths that match the current search filter (computed once)
    private HashSet<string> _matchingPaths = new();

    // Cookie key for saving expanded state between sessions (unique per panel)
    private string ExpandedPathsCookieKey => $"TreeAssetBrowser.{_panelId}.ExpandedPaths";

    /// <summary>
    /// Callback when user requests to close this panel
    /// </summary>
    public Action OnCloseRequested { get; set; }

    /// <summary>
    /// Callback when user requests to move this panel left
    /// </summary>
    public Action OnMoveLeftRequested { get; set; }

    /// <summary>
    /// Callback when user requests to move this panel right
    /// </summary>
    public Action OnMoveRightRequested { get; set; }

    /// <summary>
    /// Selected asset callback
    /// </summary>
    public Action<Asset> OnAssetSelected;
    public Action<string> OnFileSelected;
    public Action<string> OnFolderSelected;

    /// <summary>
    /// Called when a folder is clicked (for syncing with IconGridPanel)
    /// </summary>
    public Action<string> OnFolderClicked;

    /// <summary>
    /// Controls visibility of close button (hide if this is the only panel)
    /// </summary>
    public bool ShowCloseButton
    {
        get => _closeBtn?.Visible ?? false;
        set
        {
            if (_closeBtn != null)
                _closeBtn.Visible = value;
        }
    }

    /// <summary>
    /// Controls visibility of move left button
    /// </summary>
    public bool ShowMoveLeftButton
    {
        get => _moveLeftBtn?.Visible ?? false;
        set
        {
            if (_moveLeftBtn != null)
                _moveLeftBtn.Visible = value;
        }
    }

    /// <summary>
    /// Controls visibility of move right button
    /// </summary>
    public bool ShowMoveRightButton
    {
        get => _moveRightBtn?.Visible ?? false;
        set
        {
            if (_moveRightBtn != null)
                _moveRightBtn.Visible = value;
        }
    }

    public AssetBrowserPanel(Widget parent, string panelId = null) : base(parent)
    {
        // Use provided panel ID or generate a default one
        _panelId = panelId ?? $"Default_{Guid.NewGuid():N}";

        SetSizeMode(SizeMode.CanGrow, SizeMode.CanGrow);

        _assetsPath = Project.Current?.GetAssetsPath();
        _codePath = Project.Current?.GetCodePath();

        // Load saved expanded state from previous session
        LoadExpandedState();
        _lastSavedExpandedPaths = new HashSet<string>(_expandedPaths);

        // Subscribe to folder state changes
        AssetFolderNode.OnExpandedStateChanged += OnFolderExpandedStateChanged;

        CreateUI();
        RefreshTree();
    }

    public override void OnDestroyed()
    {
        base.OnDestroyed();
        AssetFolderNode.OnExpandedStateChanged -= OnFolderExpandedStateChanged;
        AssetFolderNode.PathsToAutoExpandByPanel.Remove(this);
    }

    private void OnFolderExpandedStateChanged(object panelKey, string fullPath, bool isExpanded)
    {
        // Only handle events from this panel's tree view
        if (panelKey != this)
            return;

        if (isExpanded)
        {
            _expandedPaths.Add(fullPath);
        }
        else
        {
            _expandedPaths.Remove(fullPath);
        }
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

        // Search input
        _searchEdit = _toolbar.Layout.Add(new LineEdit(this));
        _searchEdit.PlaceholderText = "Search...";
        _searchEdit.TextEdited += OnSearchTextChanged;
        _searchEdit.ReturnPressed += () => DoSearch();

        // Search button
        _searchBtn = _toolbar.Layout.Add(new IconButton("search"));
        _searchBtn.ToolTip = "Search";
        _searchBtn.Background = Color.Transparent;
        _searchBtn.OnClick = () => DoSearch();

        // Clear search button
        _clearSearchBtn = _toolbar.Layout.Add(new IconButton("close"));
        _clearSearchBtn.ToolTip = "Clear Search";
        _clearSearchBtn.Background = Color.Transparent;
        _clearSearchBtn.OnClick = ClearSearch;
        _clearSearchBtn.Visible = false;

        // Refresh button
        var refreshBtn = _toolbar.Layout.Add(new IconButton("refresh"));
        refreshBtn.ToolTip = "Refresh";
        refreshBtn.Background = Color.Transparent;
        refreshBtn.OnClick = RefreshTree;

        // Expand all button (only visible during search)
        _expandAllBtn = _toolbar.Layout.Add(new IconButton("unfold_more"));
        _expandAllBtn.ToolTip = "Expand All";
        _expandAllBtn.Background = Color.Transparent;
        _expandAllBtn.OnClick = ExpandAll;
        _expandAllBtn.Visible = false;

        // Collapse all button (only visible during search)
        _collapseAllBtn = _toolbar.Layout.Add(new IconButton("unfold_less"));
        _collapseAllBtn.ToolTip = "Collapse All";
        _collapseAllBtn.Background = Color.Transparent;
        _collapseAllBtn.OnClick = CollapseAll;
        _collapseAllBtn.Visible = false;

        // Move left button
        _moveLeftBtn = _toolbar.Layout.Add(new IconButton("chevron_left"));
        _moveLeftBtn.ToolTip = "Move Panel Left";
        _moveLeftBtn.Background = Color.Transparent;
        _moveLeftBtn.OnClick = () => OnMoveLeftRequested?.Invoke();
        _moveLeftBtn.Visible = false;

        // Move right button
        _moveRightBtn = _toolbar.Layout.Add(new IconButton("chevron_right"));
        _moveRightBtn.ToolTip = "Move Panel Right";
        _moveRightBtn.Background = Color.Transparent;
        _moveRightBtn.OnClick = () => OnMoveRightRequested?.Invoke();
        _moveRightBtn.Visible = false;

        // Close button
        _closeBtn = _toolbar.Layout.Add(new IconButton("close"));
        _closeBtn.ToolTip = "Close Panel";
        _closeBtn.Background = Color.Transparent;
        _closeBtn.OnClick = () => OnCloseRequested?.Invoke();
        _closeBtn.Visible = false; // Hidden by default, shown when multiple panels exist

        // Tree view with drag-drop support
        _treeView = Layout.Add(new AssetTreeView(this));
        _treeView.SetSizeMode(SizeMode.CanGrow, SizeMode.CanGrow);
        _treeView.MultiSelect = false;
        _treeView.ItemSpacing = 1;
        _treeView.IndentWidth = 16;

        _treeView.ItemActivated += OnItemActivated;
        _treeView.OnSelectionChanged += OnSelectionChanged;
    }

    private void OnSearchTextChanged(string text)
    {
        // Save expanded state when user starts typing (before any search)
        if (!string.IsNullOrEmpty(text) && !_isSearchActive)
        {
            _preSearchExpandedPaths = GetExpandedPaths();
            _isSearchActive = true;
        }
    }

    private void DoSearch()
    {
        var text = _searchEdit.Text;
        _searchFilter = text?.ToLowerInvariant() ?? "";

        if (string.IsNullOrEmpty(_searchFilter))
        {
            ClearSearch();
            return;
        }

        // Ensure expanded state is saved (in case search was triggered without typing)
        if (!_isSearchActive)
        {
            _preSearchExpandedPaths = GetExpandedPaths();
            _isSearchActive = true;
        }

        _clearSearchBtn.Visible = true;
        _expandAllBtn.Visible = true;
        _collapseAllBtn.Visible = true;
        ApplySearchFilter();
    }

    private void ApplySearchFilter()
    {
        // Pre-compute matching paths by scanning filesystem (done ONCE)
        _matchingPaths.Clear();

        // Scan Assets folder
        if (!string.IsNullOrEmpty(_assetsPath) && Directory.Exists(_assetsPath))
        {
            ScanDirectoryForMatches(_assetsPath, _searchFilter);
        }

        // Scan Code folder
        if (!string.IsNullOrEmpty(_codePath) && Directory.Exists(_codePath))
        {
            ScanDirectoryForMatches(_codePath, _searchFilter);
        }

        // Scan Core folder
        var corePath = global::Editor.FileSystem.Root.GetFullPath("/core/");
        if (!string.IsNullOrEmpty(corePath) && Directory.Exists(corePath))
        {
            ScanDirectoryForMatches(corePath, _searchFilter);
        }

        // Scan Citizen folder
        var citizenPath = global::Editor.FileSystem.Root.GetFullPath("/addons/citizen/assets/");
        if (!string.IsNullOrEmpty(citizenPath) && Directory.Exists(citizenPath))
        {
            ScanDirectoryForMatches(citizenPath, _searchFilter);
        }

        // Set filter to check the pre-computed cache (fast O(1) lookup)
        _treeView.ShouldDisplayChild = (obj) =>
        {
            if (obj is AssetFolderNode folder)
                return _matchingPaths.Contains(folder.FullPath);
            if (obj is AssetFileNode file)
                return _matchingPaths.Contains(file.FullPath);
            // Headers and other nodes - always show
            return true;
        };

        // Clear and rebuild tree
        _treeView.Clear();
        BuildTreeItems();
        _treeView.Update();
    }

    /// <summary>
    /// Scan directory and add all matching file/folder paths to cache.
    /// Also adds parent folder paths so they remain visible.
    /// </summary>
    private void ScanDirectoryForMatches(string path, string filter, int maxDepth = 15)
    {
        if (maxDepth <= 0 || !Directory.Exists(path))
            return;

        try
        {
            bool hasMatchingChild = false;

            // Check files
            foreach (var file in Directory.EnumerateFiles(path))
            {
                var fileName = Path.GetFileName(file);

                // Skip hidden/generated files
                if (fileName.StartsWith(".") ||
                    fileName.Contains(".generated", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip compiled assets only if source exists
                if (fileName.EndsWith("_c"))
                {
                    var sourcePath = file[..^2]; // Remove _c
                    if (File.Exists(sourcePath))
                        continue;
                }

                if (fileName.ToLowerInvariant().Contains(filter))
                {
                    _matchingPaths.Add(Path.GetFullPath(file));
                    hasMatchingChild = true;
                }
            }

            // Check subdirectories
            foreach (var dir in Directory.EnumerateDirectories(path))
            {
                var dirName = Path.GetFileName(dir);

                // Skip hidden/system folders
                if (dirName.StartsWith(".") || dirName.Equals("obj", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Check if folder name matches
                if (dirName.ToLowerInvariant().Contains(filter))
                {
                    _matchingPaths.Add(Path.GetFullPath(dir));
                    hasMatchingChild = true;
                }

                // Recursively scan subdirectory
                int countBefore = _matchingPaths.Count;
                ScanDirectoryForMatches(dir, filter, maxDepth - 1);

                // If subdirectory had matches, add it to visible paths
                if (_matchingPaths.Count > countBefore)
                {
                    _matchingPaths.Add(Path.GetFullPath(dir));
                    hasMatchingChild = true;
                }
            }

            // If this folder has matching children, make sure it's visible
            if (hasMatchingChild)
            {
                _matchingPaths.Add(Path.GetFullPath(path));
            }
        }
        catch
        {
            // Ignore filesystem errors
        }
    }

    private void ClearSearch()
    {
        _searchEdit.Text = "";
        _searchFilter = "";
        _treeView.ShouldDisplayChild = null;
        _matchingPaths.Clear();
        _clearSearchBtn.Visible = false;
        _expandAllBtn.Visible = false;
        _collapseAllBtn.Visible = false;

        // Restore expanded state from before search
        if (_isSearchActive)
        {
            _isSearchActive = false;

            // Rebuild tree without filter, then restore expanded state
            _treeView.Clear();
            BuildTreeItems();
            RestoreExpandedPaths(_preSearchExpandedPaths);
        }

        _treeView.Update();
    }

    private HashSet<string> GetExpandedPaths()
    {
        // Return copy of current tracked expanded paths
        return new HashSet<string>(_expandedPaths);
    }

    private void SaveExpandedState()
    {
        // Save expanded paths to ProjectCookie for persistence between sessions
        var pathsList = _expandedPaths.ToList();
        ProjectCookie.Set(ExpandedPathsCookieKey, pathsList);
    }

    private void LoadExpandedState()
    {
        // Load expanded paths from ProjectCookie
        var pathsList = ProjectCookie.Get<List<string>>(ExpandedPathsCookieKey, new List<string>());
        _expandedPaths = new HashSet<string>(pathsList);
    }

    private void RestoreExpandedPaths(HashSet<string> paths)
    {
        _expandedPaths = new HashSet<string>(paths);

        // Create panel-specific auto-expand set (keyed by this panel instance)
        if (!AssetFolderNode.PathsToAutoExpandByPanel.ContainsKey(this))
        {
            AssetFolderNode.PathsToAutoExpandByPanel[this] = new HashSet<string>();
        }
        else
        {
            AssetFolderNode.PathsToAutoExpandByPanel[this].Clear();
        }

        RestoreExpandedPathsRecursive(_treeView.Items, paths, 0);
    }

    private void RestoreExpandedPathsRecursive(IEnumerable<object> items, HashSet<string> paths, int depth)
    {
        foreach (var item in items)
        {
            if (item is AssetFolderNode folder)
            {
                bool shouldExpand = paths.Contains(folder.FullPath);
                bool hasChildToExpand = !shouldExpand && paths.Any(p =>
                    p.StartsWith(folder.FullPath + Path.DirectorySeparatorChar) ||
                    p.StartsWith(folder.FullPath + "/"));

                if (shouldExpand || hasChildToExpand)
                {
                    // Add to panel-specific auto-expand set
                    if (AssetFolderNode.PathsToAutoExpandByPanel.TryGetValue(this, out var panelPaths))
                    {
                        panelPaths.Add(folder.FullPath);
                    }
                    folder.EnsureChildrenBuilt();
                    if (folder.Children != null)
                    {
                        RestoreExpandedPathsRecursive(folder.Children, paths, depth + 1);
                    }
                }
            }
            else if (item is PackageFolderNode packageFolder)
            {
                var packagePath = $"__package__{packageFolder.Package.FullIdent}";
                if (paths.Contains(packagePath))
                {
                    _treeView.Open(packageFolder);
                    if (packageFolder.Children != null)
                    {
                        RestoreExpandedPathsRecursive(packageFolder.Children, paths, depth + 1);
                    }
                }
            }
            else if (item is PackageSubFolderNode packageSubFolder)
            {
                var subPath = $"__packagesub__{packageSubFolder.Package.FullIdent}__{packageSubFolder.FolderName}";
                if (paths.Contains(subPath))
                {
                    _treeView.Open(packageSubFolder);
                    if (packageSubFolder.Children != null)
                    {
                        RestoreExpandedPathsRecursive(packageSubFolder.Children, paths, depth + 1);
                    }
                }
            }
            else if (item is TreeNode node)
            {
                var headerPath = $"__header__{node.Name}";
                if (paths.Contains(headerPath))
                {
                    _treeView.Open(node);
                }
                // Always check children of headers (they contain folders)
                if (node.Children != null)
                {
                    RestoreExpandedPathsRecursive(node.Children, paths, depth + 1);
                }
            }
        }
    }

    public void RefreshTree()
    {
        // Save expanded state before refresh
        var savedPaths = GetExpandedPaths();
        bool hadSavedPaths = savedPaths.Count > 0;

        _treeView.Clear();
        BuildTreeItems(hadSavedPaths);

        // Restore expanded state - folders will auto-expand on first paint
        if (hadSavedPaths)
        {
            RestoreExpandedPaths(savedPaths);
        }

        _treeView.Update();
    }

    private void BuildTreeItems(bool skipDefaultOpen = true)
    {
        // Add Assets folder
        if (!string.IsNullOrEmpty(_assetsPath) && Directory.Exists(_assetsPath))
        {
            var assetsNode = new AssetFolderNode(_assetsPath, "Assets", "folder_special");
            _treeView.AddItem(assetsNode);

            // Default open Assets on first load
            if (!skipDefaultOpen)
            {
                _treeView.Open(assetsNode);
                _expandedPaths.Add(assetsNode.FullPath);
            }
        }

        // Add Code folder
        if (!string.IsNullOrEmpty(_codePath) && Directory.Exists(_codePath))
        {
            var codeNode = new AssetFolderNode(_codePath, "Code", "code");
            _treeView.AddItem(codeNode);
        }

        // Add Parent Package if this is an addon with a parent (e.g. sandbox gamemode)
        var parentPackageIdent = Project.Current?.Config?.GetMetaOrDefault("ParentPackage", "");
        if (Project.Current?.Config?.Type == "addon" &&
            !string.IsNullOrWhiteSpace(parentPackageIdent) &&
            Package.TryGetCached(parentPackageIdent, out Package parentPackage))
        {
            var parentHeader = new TreeNode.SmallHeader("cloud", "Parent");
            _treeView.AddItem(parentHeader);

            var packageNode = new PackageFolderNode(parentPackage, "supervisor_account");
            parentHeader.AddItem(packageNode);

            // Default open on first load
            if (!skipDefaultOpen)
            {
                _treeView.Open(parentHeader);
                _expandedPaths.Add("__header__Parent");
            }
        }

        // Add s&box section header with Core engine assets
        var sboxHeader = new TreeNode.SmallHeader("dns", "s&box");
        _treeView.AddItem(sboxHeader);

        // Add Core engine assets folder
        var corePath = global::Editor.FileSystem.Root.GetFullPath("/core/");
        if (!string.IsNullOrEmpty(corePath) && Directory.Exists(corePath))
        {
            var coreNode = new AssetFolderNode(corePath, "Core", "folder");
            sboxHeader.AddItem(coreNode);
        }

        // Add Citizen assets folder (optional DLC content)
        var citizenPath = global::Editor.FileSystem.Root.GetFullPath("/addons/citizen/assets/");
        if (!string.IsNullOrEmpty(citizenPath) && Directory.Exists(citizenPath))
        {
            var citizenNode = new AssetFolderNode(citizenPath, "Citizen", "accessibility_new");
            sboxHeader.AddItem(citizenNode);
        }

        // Default open s&box on first load
        if (!skipDefaultOpen)
        {
            _treeView.Open(sboxHeader);
            _expandedPaths.Add("__header__s&box");
        }
    }

    private void ExpandAll()
    {
        foreach (var item in _treeView.Items)
        {
            if (item is TreeNode node)
                ExpandNodeRecursive(node);
        }
        _treeView.Update();
    }

    private void ExpandNodeRecursive(TreeNode node)
    {
        // Handle different node types
        if (node is AssetFolderNode folder)
        {
            folder.EnsureChildrenBuilt();
            _treeView.Open(folder);

            foreach (var child in folder.Children)
            {
                if (child is TreeNode childNode)
                    ExpandNodeRecursive(childNode);
            }
        }
        else if (node is PackageFolderNode packageFolder)
        {
            _treeView.Open(packageFolder);

            if (packageFolder.Children != null)
            {
                foreach (var child in packageFolder.Children)
                {
                    if (child is TreeNode childNode)
                        ExpandNodeRecursive(childNode);
                }
            }
        }
        else if (node is PackageSubFolderNode packageSubFolder)
        {
            _treeView.Open(packageSubFolder);

            if (packageSubFolder.Children != null)
            {
                foreach (var child in packageSubFolder.Children)
                {
                    if (child is TreeNode childNode)
                        ExpandNodeRecursive(childNode);
                }
            }
        }
        else if (node.Children != null)
        {
            // For headers and other generic nodes with children
            _treeView.Open(node);

            foreach (var child in node.Children)
            {
                if (child is TreeNode childNode)
                    ExpandNodeRecursive(childNode);
            }
        }
    }

    private void CollapseAll()
    {
        foreach (var item in _treeView.Items)
        {
            if (item is TreeNode node)
                CollapseNodeRecursive(node);
        }
        _treeView.Update();
    }

    private void CollapseNodeRecursive(TreeNode node)
    {
        // Collapse children first (depth-first)
        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                if (child is TreeNode childNode)
                    CollapseNodeRecursive(childNode);
            }
        }

        // Then collapse this node
        _treeView.Close(node);
    }

    private void OnItemActivated(object item)
    {
        if (item is AssetFileNode fileNode)
        {
            var asset = AssetSystem.FindByPath(fileNode.FullPath);
            if (asset != null)
            {
                OnAssetSelected?.Invoke(asset);
                asset.OpenInEditor();
            }
            else
            {
                OnFileSelected?.Invoke(fileNode.FullPath);
                EditorUtility.OpenFolder(fileNode.FullPath);
            }
        }
        // Folder and header toggle is handled automatically by TreeView
        // State tracking is done via OnFrame periodic sync
    }

    private void OnSelectionChanged(object[] items)
    {
        var item = items.FirstOrDefault();

        if (item is AssetFileNode fileNode)
        {
            var asset = AssetSystem.FindByPath(fileNode.FullPath);
            if (asset != null)
            {
                EditorUtility.InspectorObject = asset;
            }
        }
        else if (item is AssetFolderNode folderNode)
        {
            OnFolderSelected?.Invoke(folderNode.FullPath);
            OnFolderClicked?.Invoke(folderNode.FullPath);
        }
    }

    private HashSet<string> _lastSavedExpandedPaths = new();
    private float _saveTimer = 0;

    [EditorEvent.Frame]
    public void OnFrame()
    {
        // Periodically save if state changed
        _saveTimer += Time.Delta;
        if (_saveTimer >= 2.0f)
        {
            _saveTimer = 0;

            if (!_expandedPaths.SetEquals(_lastSavedExpandedPaths))
            {
                SaveExpandedState();
                _lastSavedExpandedPaths = new HashSet<string>(_expandedPaths);
            }
        }
    }


    /// <summary>
    /// Navigate to and select a specific file in the tree
    /// </summary>
    public void NavigateToFile(string path)
    {
        var normalizedPath = Path.GetFullPath(path);

        foreach (var rootItem in _treeView.Items)
        {
            if (rootItem is AssetFolderNode rootFolder)
            {
                var node = rootFolder.FindNode(normalizedPath);
                if (node != null)
                {
                    _treeView.ExpandPathTo(node);
                    _treeView.SelectItem(node);
                    _treeView.ScrollTo(node);
                    break;
                }
            }
        }
    }
}

/// <summary>
/// Custom TreeView with drag-drop support for asset folders
/// </summary>
internal class AssetTreeView : TreeView
{
    public AssetTreeView(Widget parent) : base(parent)
    {
        AcceptDrops = true;
    }
}
