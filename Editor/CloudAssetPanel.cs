using Editor;
using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GeneralGame.Editor;

/// <summary>
/// Panel for browsing cloud assets from the S&box community as a tree view.
/// Shows categories with auto-loaded items and supports per-category search.
/// </summary>
public class CloudAssetPanel : Widget, IBrowserPanel
{
    private Widget _toolbar;
    private LineEdit _searchEdit;
    private IconButton _closeBtn;
    private IconButton _searchBtn;
    private IconButton _clearSearchBtn;
    private IconButton _refreshBtn;
    private IconButton _moveLeftBtn;
    private IconButton _moveRightBtn;
    private TreeView _treeView;
    private Label _statusLabel;

    private const int InitialItemsPerCategory = 10;
    private const int SearchItemsPerCategory = 20;
    private bool _isSearchMode = false;
    private string _currentSearchQuery = "";

    public Action OnCloseRequested { get; set; }
    public Action OnMoveLeftRequested { get; set; }
    public Action OnMoveRightRequested { get; set; }

    /// <summary>
    /// Called when a cloud category/folder is clicked (for syncing with preview panels)
    /// </summary>
    public Action<List<Package>> OnCloudAssetsLoaded;

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

    public CloudAssetPanel(Widget parent) : base(parent)
    {
        SetSizeMode(SizeMode.CanGrow, SizeMode.CanGrow);
        CreateUI();
        BuildCategoryTree();

        // Auto-load initial items for all categories
        _ = LoadAllCategoriesAsync();
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

        // Search input
        _searchEdit = _toolbar.Layout.Add(new LineEdit(this));
        _searchEdit.PlaceholderText = "Search cloud...";
        _searchEdit.ReturnPressed += DoSearch;

        // Search button
        _searchBtn = _toolbar.Layout.Add(new IconButton("search"));
        _searchBtn.ToolTip = "Search";
        _searchBtn.Background = Color.Transparent;
        _searchBtn.OnClick = DoSearch;

        // Clear search button
        _clearSearchBtn = _toolbar.Layout.Add(new IconButton("close"));
        _clearSearchBtn.ToolTip = "Clear Search";
        _clearSearchBtn.Background = Color.Transparent;
        _clearSearchBtn.OnClick = ClearSearch;
        _clearSearchBtn.Visible = false;

        // Refresh button
        _refreshBtn = _toolbar.Layout.Add(new IconButton("refresh"));
        _refreshBtn.ToolTip = "Refresh All";
        _refreshBtn.Background = Color.Transparent;
        _refreshBtn.OnClick = RefreshAll;

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

        // Close button
        _closeBtn = _toolbar.Layout.Add(new IconButton("close"));
        _closeBtn.ToolTip = "Close Panel";
        _closeBtn.Background = Color.Transparent;
        _closeBtn.OnClick = () => OnCloseRequested?.Invoke();
        _closeBtn.Visible = false;

        // Tree view
        _treeView = Layout.Add(new TreeView(this));
        _treeView.SetSizeMode(SizeMode.CanGrow, SizeMode.CanGrow);
        _treeView.MultiSelect = false;
        _treeView.ItemSpacing = 1;
        _treeView.IndentWidth = 16;

        _treeView.ItemActivated += OnItemActivated;
        _treeView.OnSelectionChanged += OnSelectionChanged;

        // Status label at bottom
        _statusLabel = Layout.Add(new Label("Loading...", this));
        _statusLabel.SetStyles("color: #888; font-size: 10px; padding: 2px;");
        _statusLabel.FixedHeight = 18;
    }

    private List<CloudCategoryNode> _categoryNodes = new();

    private void BuildCategoryTree()
    {
        _treeView.Clear();
        _categoryNodes.Clear();

        // Add category folders - filter format: "type:model", "type:material", etc.
        var models = new CloudCategoryNode("model", "Models", "view_in_ar", this);
        var materials = new CloudCategoryNode("material", "Materials", "texture", this);
        var sounds = new CloudCategoryNode("sound", "Sounds", "audiotrack", this);
        var maps = new CloudCategoryNode("map", "Maps", "landscape", this);

        _categoryNodes.Add(models);
        _categoryNodes.Add(materials);
        _categoryNodes.Add(sounds);
        _categoryNodes.Add(maps);

        foreach (var node in _categoryNodes)
        {
            _treeView.AddItem(node);
        }

        _treeView.Update();
    }

    /// <summary>
    /// Load initial items for all categories on startup
    /// </summary>
    private async Task LoadAllCategoriesAsync()
    {
        _statusLabel.Text = "Loading categories...";
        int totalLoaded = 0;

        foreach (var categoryNode in _categoryNodes)
        {
            try
            {
                // Query format: "type:model", "type:material", etc.
                var query = $"type:{categoryNode.TypeFilter}";
                var result = await Package.FindAsync(query, InitialItemsPerCategory, 0);

                if (result?.Packages != null)
                {
                    categoryNode.SetPackagesAndRefresh(result.Packages.ToList());
                    totalLoaded += categoryNode.Packages.Count;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[CloudAssetPanel] Failed to load {categoryNode.DisplayName}: {ex.Message}");
            }
        }

        _treeView.Update();
        _statusLabel.Text = $"Loaded {totalLoaded} cloud assets";
    }

    private async void DoSearch()
    {
        var query = _searchEdit.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(query))
        {
            ClearSearch();
            return;
        }

        _currentSearchQuery = query;
        _isSearchMode = true;
        _searchBtn.Enabled = false;
        _clearSearchBtn.Visible = true;
        _statusLabel.Text = $"Searching \"{query}\"...";

        try
        {
            int totalFound = 0;

            // Search in each category
            foreach (var categoryNode in _categoryNodes)
            {
                try
                {
                    // Combine search query with type filter
                    var searchQuery = $"{query} type:{categoryNode.TypeFilter}";
                    var result = await Package.FindAsync(searchQuery, SearchItemsPerCategory, 0);

                    if (result?.Packages != null && result.Packages.Any())
                    {
                        categoryNode.SetPackagesAndRefresh(result.Packages.ToList());
                        totalFound += result.Packages.Count();
                        // Auto-expand categories that have search results
                        _treeView.Open(categoryNode);
                    }
                    else
                    {
                        categoryNode.SetPackagesAndRefresh(new List<Package>());
                        // Collapse empty categories
                        _treeView.Close(categoryNode);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"Search error in {categoryNode.DisplayName}: {ex.Message}");
                }
            }

            _treeView.Update();
            _statusLabel.Text = $"Found {totalFound} results for \"{query}\"";

            // Notify with all found packages
            var allPackages = _categoryNodes.SelectMany(c => c.Packages).ToList();
            if (allPackages.Any())
            {
                OnCloudAssetsLoaded?.Invoke(allPackages);
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Search error: {ex.Message}";
            Log.Warning($"Cloud search error: {ex.Message}");
        }
        finally
        {
            _searchBtn.Enabled = true;
        }
    }

    private void ClearSearch()
    {
        _searchEdit.Text = "";
        _currentSearchQuery = "";
        _isSearchMode = false;
        _clearSearchBtn.Visible = false;

        // Reload all categories with default items
        foreach (var node in _categoryNodes)
        {
            node.ClearPackages();
        }
        _ = LoadAllCategoriesAsync();
    }

    private async void RefreshAll()
    {
        foreach (var node in _categoryNodes)
        {
            node.ClearPackages();
        }

        if (_isSearchMode && !string.IsNullOrEmpty(_currentSearchQuery))
        {
            DoSearch();
        }
        else
        {
            await LoadAllCategoriesAsync();
        }
    }

    private void OnItemActivated(object item)
    {
        if (item is CloudCategoryNode categoryNode)
        {
            _treeView.Toggle(categoryNode);
        }
        else if (item is CloudPackageNode packageNode)
        {
            // Open package page in browser
            var url = $"https://sbox.game/{packageNode.FullIdent}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        else if (item is CloudLoadMoreNode loadMoreNode)
        {
            _ = LoadMoreForCategory(loadMoreNode.Category);
        }
    }

    private async Task LoadMoreForCategory(CloudCategoryNode categoryNode)
    {
        if (categoryNode.IsLoadingMore)
            return;

        categoryNode.IsLoadingMore = true;
        var currentCount = categoryNode.Packages.Count;
        _statusLabel.Text = $"Loading more {categoryNode.DisplayName.ToLower()}...";

        try
        {
            string query;
            if (_isSearchMode && !string.IsNullOrEmpty(_currentSearchQuery))
            {
                query = $"{_currentSearchQuery} type:{categoryNode.TypeFilter}";
            }
            else
            {
                query = $"type:{categoryNode.TypeFilter}";
            }

            var result = await Package.FindAsync(query, 20, currentCount);

            if (result?.Packages != null && result.Packages.Any())
            {
                categoryNode.AppendPackagesAndRefresh(result.Packages.ToList());
                _treeView.Update();
                _statusLabel.Text = $"Loaded {categoryNode.Packages.Count} {categoryNode.DisplayName.ToLower()}";
                OnCloudAssetsLoaded?.Invoke(categoryNode.Packages);
            }
            else
            {
                _statusLabel.Text = $"No more {categoryNode.DisplayName.ToLower()} found";
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Error: {ex.Message}";
            Log.Warning($"Load more error: {ex.Message}");
        }
        finally
        {
            categoryNode.IsLoadingMore = false;
        }
    }

    private void OnSelectionChanged(object[] items)
    {
        var item = items.FirstOrDefault();

        if (item is CloudCategoryNode categoryNode)
        {
            if (categoryNode.Packages.Count > 0)
            {
                _statusLabel.Text = $"{categoryNode.Packages.Count} {categoryNode.DisplayName.ToLower()}";
                OnCloudAssetsLoaded?.Invoke(categoryNode.Packages);
            }
        }
        else if (item is CloudPackageNode packageNode)
        {
            _statusLabel.Text = $"{packageNode.Title} by {packageNode.Author}";
        }
    }
}

/// <summary>
/// Tree node representing a cloud asset category (Models, Materials, etc.)
/// </summary>
internal class CloudCategoryNode : TreeNode
{
    public string TypeFilter { get; }
    public string DisplayName { get; }
    public string IconName { get; }
    public bool IsLoadingMore { get; set; }
    public List<Package> Packages { get; private set; } = new();

    private CloudAssetPanel _panel;

    // Always show expand arrow - categories always have potential children
    public override bool HasChildren => true;
    public override string Name => DisplayName;

    public CloudCategoryNode(string typeFilter, string displayName, string icon, CloudAssetPanel panel)
    {
        TypeFilter = typeFilter;
        DisplayName = displayName;
        IconName = icon;
        _panel = panel;
        Value = this;
    }

    public void ClearPackages()
    {
        Packages.Clear();
        Clear();
        Dirty();
    }

    public void SetPackagesAndRefresh(List<Package> packages)
    {
        Packages = packages;

        // Clear and rebuild children
        Clear();

        foreach (var pkg in Packages)
        {
            AddItem(new CloudPackageNode(pkg));
        }

        // Always add "Load More" node
        AddItem(new CloudLoadMoreNode(this));

        Dirty();
    }

    public void AppendPackagesAndRefresh(List<Package> newPackages)
    {
        Packages.AddRange(newPackages);

        // Clear and rebuild children
        Clear();

        foreach (var pkg in Packages)
        {
            AddItem(new CloudPackageNode(pkg));
        }

        // Add "Load More" node
        AddItem(new CloudLoadMoreNode(this));

        Dirty();
    }

    protected override void BuildChildren()
    {
        // Children are built via SetPackagesAndRefresh/AppendPackagesAndRefresh
        // This is called by TreeView when expanding - we already have children built
    }

    public override void OnPaint(VirtualWidget item)
    {
        PaintSelection(item);

        var rect = item.Rect;

        // Draw folder icon
        var iconColor = Theme.Yellow;
        if (IsLoadingMore)
            iconColor = Theme.Primary;

        Paint.SetPen(iconColor);
        Paint.DrawIcon(rect, IconName, 16, TextFlag.LeftCenter);

        rect.Left += 22;

        // Draw name
        Paint.SetPen(Theme.Text);
        Paint.SetDefaultFont(9, item.Selected ? 600 : 400);
        Paint.DrawText(rect, DisplayName, TextFlag.LeftCenter);

        // Draw count
        var countText = $"({Packages.Count})";
        Paint.SetPen(Theme.Text.WithAlpha(0.5f));
        Paint.SetDefaultFont(8, 400);
        var countRect = new Rect(item.Rect.Right - 50, item.Rect.Top, 46, item.Rect.Height);
        Paint.DrawText(countRect, countText, TextFlag.RightCenter);
    }
}

/// <summary>
/// Tree node for "Load More" action
/// </summary>
internal class CloudLoadMoreNode : TreeNode
{
    public CloudCategoryNode Category { get; }

    public override bool HasChildren => false;
    public override string Name => "Load More...";

    public CloudLoadMoreNode(CloudCategoryNode category)
    {
        Category = category;
        Value = this;
    }

    public override void OnPaint(VirtualWidget item)
    {
        var rect = item.Rect;

        // Draw load more icon
        Paint.SetPen(Theme.Primary.WithAlpha(0.7f));
        Paint.DrawIcon(rect, "add_circle_outline", 14, TextFlag.LeftCenter);

        rect.Left += 20;

        // Draw text
        Paint.SetPen(Theme.Primary);
        Paint.SetDefaultFont(8, 400);
        Paint.DrawText(rect, "Load More...", TextFlag.LeftCenter);
    }
}

/// <summary>
/// Tree node representing a single cloud package/asset
/// </summary>
internal class CloudPackageNode : TreeNode
{
    public Package Package { get; }
    public string FullIdent { get; }
    public string Title { get; }
    public string Author { get; }
    public string Thumb { get; }

    public override bool HasChildren => false;
    public override string Name => Title;

    public CloudPackageNode(Package package)
    {
        Package = package;
        FullIdent = package.FullIdent;
        Title = package.Title ?? package.FullIdent;
        Author = package.Org?.Title ?? "Unknown";
        Thumb = package.Thumb;
        Value = this;
    }

    public override void OnPaint(VirtualWidget item)
    {
        PaintSelection(item);

        var rect = item.Rect;

        // Draw package thumbnail if available, otherwise use type icon
        if (!string.IsNullOrEmpty(Thumb) && Thumb.StartsWith("http"))
        {
            var iconRect = rect.Shrink(0, 2);
            iconRect.Width = 16;
            iconRect.Height = 16;

            Paint.Draw(iconRect, Thumb);
            rect.Left += 20;
        }
        else
        {
            // Fallback to type icon
            var icon = GetIconForType(Package.PackageType);
            var iconColor = GetColorForType(Package.PackageType);

            Paint.SetPen(iconColor);
            Paint.DrawIcon(rect, icon, 14, TextFlag.LeftCenter);
            rect.Left += 20;
        }

        // Draw title
        Paint.SetPen(Theme.Text);
        Paint.SetDefaultFont(9, item.Selected ? 600 : 400);

        var title = Title;
        if (title.Length > 30)
            title = title.Substring(0, 28) + "..";
        Paint.DrawText(rect, title, TextFlag.LeftCenter);
    }

    public override bool OnDragStart()
    {
        var drag = new Drag(TreeView);
        drag.Data.Text = FullIdent;

        // Set preview image if available
        if (!string.IsNullOrEmpty(Thumb))
        {
            drag.Data.Url = new Uri(Thumb);
        }

        drag.Execute();
        return true;
    }

    public override bool OnContextMenu()
    {
        var menu = new ContextMenu(null);

        menu.AddOption("Open in Browser", "open_in_new", () =>
        {
            var url = $"https://sbox.game/{FullIdent}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        });

        menu.AddOption("Copy Identifier", "content_copy", () =>
        {
            EditorUtility.Clipboard.Copy(FullIdent);
        });

        menu.OpenAtCursor();
        return true;
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
}
