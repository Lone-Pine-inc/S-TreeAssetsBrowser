using Editor;
using Sandbox;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GeneralGame.Editor;

/// <summary>
/// Custom Asset Browser displayed as a tree view of files and folders.
/// Supports multiple side-by-side panels for comparing/browsing different locations.
/// Supports different panel types: Tree View and Icon Grid.
/// Automatically switches to tab mode when window is narrow.
/// </summary>
[Dock("Editor", "Tree Asset Browser", "account_tree")]
public class TreeAssetBrowser : Widget
{
    private List<Widget> _panels = new();
    private List<Splitter> _splitters = new();
    private Widget _panelsContainer;
    private Widget _mainToolbar;
    private Widget _tabsContainer;
    private Label _titleLabel;
    private IconButton _toggleModeBtn;
    private int _activeTabIndex = 0;
    private bool _isTabMode = false;

    // Unique instance ID for this browser window (assigned from saved state or generated)
    private string _instanceId;
    private static int _nextInstanceId = 0;
    private static HashSet<string> _usedInstanceIds = new();

    // Cookie keys
    private string BrowserStateCookieKey => $"TreeAssetBrowser.{_instanceId}.State";
    private const string AllBrowserIdsCookieKey = "TreeAssetBrowser.AllInstanceIds";

    // Selected asset callback (forwarded from panels)
    public Action<Asset> OnAssetSelected;
    public Action<string> OnFileSelected;
    public Action<string> OnFolderSelected;

    public TreeAssetBrowser(Widget parent) : base(parent)
    {
        // Try to claim an existing saved instance ID, or generate new one
        _instanceId = ClaimOrCreateInstanceId();
        _usedInstanceIds.Add(_instanceId);

        WindowTitle = "Tree Asset Browser";
        MinimumSize = new Vector2(250, 200);
        SetSizeMode(SizeMode.CanGrow, SizeMode.CanGrow);

        CreateUI();

        // Try to restore saved state, or create default panel
        if (!RestoreBrowserState())
        {
            AddTreePanel();
        }
    }

    public override void OnDestroyed()
    {
        base.OnDestroyed();
        SaveBrowserState();
        _usedInstanceIds.Remove(_instanceId);
    }

    /// <summary>
    /// Find an available saved instance ID or create a new one
    /// </summary>
    private string ClaimOrCreateInstanceId()
    {
        // Get list of saved instance IDs
        var savedIds = ProjectCookie.Get<List<string>>(AllBrowserIdsCookieKey, new List<string>());

        // Find first unused saved ID
        foreach (var id in savedIds)
        {
            if (!_usedInstanceIds.Contains(id))
            {
                return id;
            }
        }

        // Generate new unique ID
        string newId;
        do
        {
            newId = $"Browser_{_nextInstanceId++}";
        } while (_usedInstanceIds.Contains(newId) || savedIds.Contains(newId));

        return newId;
    }

    private void CreateUI()
    {
        Layout = Layout.Column();
        Layout.Spacing = 2;

        // Main toolbar with tabs, toggle and add buttons
        _mainToolbar = Layout.Add(new Widget(this));
        _mainToolbar.Layout = Layout.Row();
        _mainToolbar.Layout.Spacing = 4;
        _mainToolbar.Layout.Margin = 4;
        _mainToolbar.FixedHeight = 28;

        // Title label (hidden in tab mode)
        _titleLabel = _mainToolbar.Layout.Add(new Label("Panels:", this));
        _titleLabel.SetStyles("color: #888; font-size: 11px;");

        // Tabs container (hidden by default, shown in tab mode)
        _tabsContainer = _mainToolbar.Layout.Add(new Widget(this));
        _tabsContainer.Layout = Layout.Row();
        _tabsContainer.Layout.Spacing = 2;
        _tabsContainer.SetSizeMode(SizeMode.CanGrow, SizeMode.Default);
        _tabsContainer.Visible = false;

        _mainToolbar.Layout.AddStretchCell();

        // Toggle tab/side-by-side mode button
        _toggleModeBtn = _mainToolbar.Layout.Add(new IconButton("tab"));
        _toggleModeBtn.ToolTip = "Switch to Tab Mode";
        _toggleModeBtn.Background = Color.Transparent;
        _toggleModeBtn.OnClick = ToggleLayoutMode;

        // Add panel button (with dropdown menu)
        var addBtn = _mainToolbar.Layout.Add(new IconButton("add"));
        addBtn.ToolTip = "Add Browser Panel";
        addBtn.Background = Color.Transparent;
        addBtn.OnClick = ShowAddPanelMenu;

        // Panels container (horizontal layout with splitters)
        _panelsContainer = Layout.Add(new Widget(this));
        _panelsContainer.Layout = Layout.Row();
        _panelsContainer.SetSizeMode(SizeMode.CanGrow, SizeMode.CanGrow);
    }

    /// <summary>
    /// Save browser state (panels, tab mode, etc.) to cookie
    /// </summary>
    private void SaveBrowserState()
    {
        var state = new BrowserState
        {
            IsTabMode = _isTabMode,
            ActiveTabIndex = _activeTabIndex,
            PanelTypes = _panels.Select(GetPanelTypeName).ToList()
        };

        ProjectCookie.Set(BrowserStateCookieKey, state);

        // Update global list of instance IDs
        var savedIds = ProjectCookie.Get<List<string>>(AllBrowserIdsCookieKey, new List<string>());
        if (!savedIds.Contains(_instanceId))
        {
            savedIds.Add(_instanceId);
            ProjectCookie.Set(AllBrowserIdsCookieKey, savedIds);
        }
    }

    /// <summary>
    /// Restore browser state from cookie
    /// </summary>
    private bool RestoreBrowserState()
    {
        var state = ProjectCookie.Get<BrowserState>(BrowserStateCookieKey, null);
        if (state == null || state.PanelTypes == null || state.PanelTypes.Count == 0)
            return false;

        // Recreate panels
        foreach (var panelType in state.PanelTypes)
        {
            switch (panelType)
            {
                case "Tree":
                    AddTreePanel(saveState: false);
                    break;
                case "IconGrid":
                    AddIconGridPanel(saveState: false);
                    break;
                case "Cloud":
                    AddCloudAssetPanel(saveState: false);
                    break;
                case "CloudIconGrid":
                    AddCloudIconGridPanel(saveState: false);
                    break;
            }
        }

        // Restore tab mode
        _activeTabIndex = Math.Clamp(state.ActiveTabIndex, 0, Math.Max(0, _panels.Count - 1));

        if (state.IsTabMode && _panels.Count > 1)
        {
            SwitchToTabMode();
        }

        return _panels.Count > 0;
    }

    /// <summary>
    /// Get type name for saving
    /// </summary>
    private string GetPanelTypeName(Widget panel)
    {
        return panel switch
        {
            AssetBrowserPanel => "Tree",
            IconGridPanel => "IconGrid",
            CloudAssetPanel => "Cloud",
            CloudIconGridPanel => "CloudIconGrid",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// Show context menu to choose panel type
    /// </summary>
    private void ShowAddPanelMenu()
    {
        var menu = new ContextMenu(this);

        menu.AddOption("Tree View", "account_tree", () => AddTreePanel());
        menu.AddOption("Icon Grid", "grid_view", () => AddIconGridPanel());
        menu.AddSeparator();
        menu.AddOption("Cloud Assets", "cloud", () => AddCloudAssetPanel());

        menu.OpenAtCursor();
    }

    /// <summary>
    /// Add a new tree view panel
    /// </summary>
    public void AddTreePanel(bool saveState = true)
    {
        AddSplitterIfNeeded();

        // Generate unique panel ID within this browser
        var panelId = $"{_instanceId}_Panel{_panels.Count}";
        var panel = new AssetBrowserPanel(_panelsContainer, panelId);
        panel.OnCloseRequested = () => RemovePanel(panel);
        panel.OnMoveLeftRequested = () => MovePanelLeft(panel);
        panel.OnMoveRightRequested = () => MovePanelRight(panel);

        // Forward callbacks
        panel.OnAssetSelected = (asset) => OnAssetSelected?.Invoke(asset);
        panel.OnFileSelected = (path) => OnFileSelected?.Invoke(path);
        panel.OnFolderSelected = (path) => OnFolderSelected?.Invoke(path);

        // Sync folder selection with all IconGridPanels
        panel.OnFolderClicked = OnFolderSelectedInTree;

        _panelsContainer.Layout.Add(panel);
        _panels.Add(panel);

        _activeTabIndex = _panels.Count - 1;
        UpdatePanelButtons();

        if (_isTabMode)
        {
            SelectTab(_activeTabIndex);
        }

        if (saveState)
        {
            SaveBrowserState();
        }
    }

    /// <summary>
    /// Add a new icon grid panel
    /// </summary>
    public void AddIconGridPanel(bool saveState = true)
    {
        AddSplitterIfNeeded();

        var panel = new IconGridPanel(_panelsContainer);
        panel.OnCloseRequested = () => RemovePanel(panel);
        panel.OnMoveLeftRequested = () => MovePanelLeft(panel);
        panel.OnMoveRightRequested = () => MovePanelRight(panel);

        _panelsContainer.Layout.Add(panel);
        _panels.Add(panel);

        _activeTabIndex = _panels.Count - 1;
        UpdatePanelButtons();

        if (_isTabMode)
        {
            SelectTab(_activeTabIndex);
        }

        if (saveState)
        {
            SaveBrowserState();
        }
    }

    /// <summary>
    /// Add a new cloud asset search panel
    /// </summary>
    public void AddCloudAssetPanel(bool saveState = true)
    {
        AddSplitterIfNeeded();

        var panel = new CloudAssetPanel(_panelsContainer);
        panel.OnCloseRequested = () => RemovePanel(panel);
        panel.OnMoveLeftRequested = () => MovePanelLeft(panel);
        panel.OnMoveRightRequested = () => MovePanelRight(panel);

        // Sync cloud asset selection with all CloudIconGridPanels
        panel.OnCloudAssetsLoaded = OnCloudAssetsLoaded;

        _panelsContainer.Layout.Add(panel);
        _panels.Add(panel);

        _activeTabIndex = _panels.Count - 1;
        UpdatePanelButtons();

        if (_isTabMode)
        {
            SelectTab(_activeTabIndex);
        }

        if (saveState)
        {
            SaveBrowserState();
        }
    }

    /// <summary>
    /// Add a new cloud icon grid panel for previewing cloud assets
    /// </summary>
    public void AddCloudIconGridPanel(bool saveState = true)
    {
        AddSplitterIfNeeded();

        var panel = new CloudIconGridPanel(_panelsContainer);
        panel.OnCloseRequested = () => RemovePanel(panel);
        panel.OnMoveLeftRequested = () => MovePanelLeft(panel);
        panel.OnMoveRightRequested = () => MovePanelRight(panel);

        _panelsContainer.Layout.Add(panel);
        _panels.Add(panel);

        _activeTabIndex = _panels.Count - 1;
        UpdatePanelButtons();

        if (_isTabMode)
        {
            SelectTab(_activeTabIndex);
        }

        if (saveState)
        {
            SaveBrowserState();
        }
    }

    /// <summary>
    /// Add splitter before new panel if not the first one
    /// </summary>
    private void AddSplitterIfNeeded()
    {
        if (_panels.Count > 0)
        {
            var splitter = new Splitter(_panelsContainer);
            splitter.IsVertical = true;
            _panelsContainer.Layout.Add(splitter);
            _splitters.Add(splitter);
        }
    }

    /// <summary>
    /// Called when folder is selected in any tree panel - syncs all IconGridPanels
    /// </summary>
    private void OnFolderSelectedInTree(string folderPath)
    {
        foreach (var panel in _panels.OfType<IconGridPanel>())
        {
            panel.ShowFolder(folderPath);
        }
    }

    /// <summary>
    /// Called when cloud assets are loaded in any CloudAssetPanel - syncs all grid panels
    /// </summary>
    private void OnCloudAssetsLoaded(List<Package> packages)
    {
        // Update CloudIconGridPanels
        foreach (var panel in _panels.OfType<CloudIconGridPanel>())
        {
            panel.ShowPackages(packages);
        }

        // Update regular IconGridPanels
        foreach (var panel in _panels.OfType<IconGridPanel>())
        {
            panel.ShowCloudPackages(packages, "Cloud Assets");
        }
    }

    /// <summary>
    /// Remove a panel (minimum 1 panel must remain)
    /// </summary>
    public void RemovePanel(Widget panel)
    {
        if (_panels.Count <= 1)
            return; // Keep at least one panel

        var index = _panels.IndexOf(panel);
        if (index < 0)
            return;

        // Remove the panel from list first
        _panels.RemoveAt(index);

        // Destroy the panel widget
        panel.Destroy();

        // Remove associated splitter
        if (_splitters.Count > 0)
        {
            // If removing first panel, remove first splitter
            // If removing other panel, remove splitter before it (index - 1)
            int splitterIndex = index > 0 ? index - 1 : 0;
            if (splitterIndex < _splitters.Count)
            {
                _splitters[splitterIndex].Destroy();
                _splitters.RemoveAt(splitterIndex);
            }
        }

        // Adjust active tab index if needed
        if (_activeTabIndex >= _panels.Count)
        {
            _activeTabIndex = _panels.Count - 1;
        }

        // Update visibility in tab mode
        if (_isTabMode)
        {
            for (int i = 0; i < _panels.Count; i++)
            {
                _panels[i].Visible = (i == _activeTabIndex);
            }
            RebuildTabBar();

            // If only one panel left, switch back to normal mode
            if (_panels.Count <= 1)
            {
                SwitchToNormalMode();
            }
        }

        UpdatePanelButtons();
        SaveBrowserState();
    }

    /// <summary>
    /// Move a panel to the left (swap with previous panel)
    /// </summary>
    public void MovePanelLeft(Widget panel)
    {
        var index = _panels.IndexOf(panel);
        if (index <= 0)
            return; // Already first or not found

        SwapPanels(index - 1, index);
    }

    /// <summary>
    /// Move a panel to the right (swap with next panel)
    /// </summary>
    public void MovePanelRight(Widget panel)
    {
        var index = _panels.IndexOf(panel);
        if (index < 0 || index >= _panels.Count - 1)
            return; // Already last or not found

        SwapPanels(index, index + 1);
    }

    /// <summary>
    /// Swap two adjacent panels
    /// </summary>
    private void SwapPanels(int indexA, int indexB)
    {
        if (indexA < 0 || indexB >= _panels.Count || indexA >= indexB)
            return;

        // Swap in the list
        var temp = _panels[indexA];
        _panels[indexA] = _panels[indexB];
        _panels[indexB] = temp;

        // Rebuild the layout
        RebuildPanelsLayout();
        UpdatePanelButtons();

        if (_isTabMode)
        {
            RebuildTabBar();
        }

        SaveBrowserState();
    }

    /// <summary>
    /// Rebuild the panels container layout after reordering
    /// </summary>
    private void RebuildPanelsLayout()
    {
        // Clear current layout (but don't destroy widgets)
        _panelsContainer.Layout.Clear(false);

        // Clear old splitters
        foreach (var splitter in _splitters)
        {
            splitter.Destroy();
        }
        _splitters.Clear();

        // Re-add panels with splitters
        for (int i = 0; i < _panels.Count; i++)
        {
            if (i > 0)
            {
                var splitter = new Splitter(_panelsContainer);
                splitter.IsVertical = true;
                _panelsContainer.Layout.Add(splitter);
                _splitters.Add(splitter);
            }
            _panelsContainer.Layout.Add(_panels[i]);
        }
    }

    /// <summary>
    /// Update close button and move arrows visibility on all panels
    /// </summary>
    private void UpdatePanelButtons()
    {
        bool showClose = _panels.Count > 1;

        for (int i = 0; i < _panels.Count; i++)
        {
            if (_panels[i] is IBrowserPanel browserPanel)
            {
                // In tab mode, hide panel buttons (close is on tab)
                if (_isTabMode)
                {
                    browserPanel.ShowCloseButton = false;
                    browserPanel.ShowMoveLeftButton = false;
                    browserPanel.ShowMoveRightButton = false;
                }
                else
                {
                    browserPanel.ShowCloseButton = showClose;
                    browserPanel.ShowMoveLeftButton = showClose && i > 0;
                    browserPanel.ShowMoveRightButton = showClose && i < _panels.Count - 1;
                }
            }
        }
    }

    /// <summary>
    /// Navigate to and select a specific file in the first tree panel
    /// </summary>
    public void NavigateToFile(string path)
    {
        _panels.OfType<AssetBrowserPanel>().FirstOrDefault()?.NavigateToFile(path);
    }

    /// <summary>
    /// Refresh all tree panels
    /// </summary>
    public void RefreshAll()
    {
        foreach (var panel in _panels.OfType<AssetBrowserPanel>())
        {
            panel.RefreshTree();
        }
    }

    [EditorEvent.Frame]
    public void OnFrame()
    {
        // Update toggle button visibility (only show when multiple panels)
        if (_toggleModeBtn != null)
        {
            _toggleModeBtn.Visible = _panels.Count > 1;
        }
    }

    /// <summary>
    /// Toggle between tab mode and side-by-side mode
    /// </summary>
    private void ToggleLayoutMode()
    {
        if (_panels.Count <= 1)
            return;

        if (_isTabMode)
        {
            SwitchToNormalMode();
        }
        else
        {
            SwitchToTabMode();
        }

        SaveBrowserState();
    }

    /// <summary>
    /// Switch to tab mode - show tabs and only one panel at a time
    /// </summary>
    private void SwitchToTabMode()
    {
        _isTabMode = true;
        _titleLabel.Visible = false;
        _tabsContainer.Visible = true;

        // Update toggle button
        if (_toggleModeBtn != null)
        {
            _toggleModeBtn.Icon = "view_column";
            _toggleModeBtn.ToolTip = "Switch to Side-by-Side Mode";
        }

        // Hide splitters
        foreach (var splitter in _splitters)
        {
            splitter.Visible = false;
        }

        // Show only active panel
        for (int i = 0; i < _panels.Count; i++)
        {
            _panels[i].Visible = (i == _activeTabIndex);
        }

        RebuildTabBar();
        UpdatePanelButtons();
    }

    /// <summary>
    /// Switch to normal side-by-side mode
    /// </summary>
    private void SwitchToNormalMode()
    {
        _isTabMode = false;
        _titleLabel.Visible = true;
        _tabsContainer.Visible = false;

        // Update toggle button
        if (_toggleModeBtn != null)
        {
            _toggleModeBtn.Icon = "tab";
            _toggleModeBtn.ToolTip = "Switch to Tab Mode";
        }

        // Show splitters
        foreach (var splitter in _splitters)
        {
            splitter.Visible = true;
        }

        // Show all panels
        foreach (var panel in _panels)
        {
            panel.Visible = true;
        }

        UpdatePanelButtons();
    }

    /// <summary>
    /// Rebuild the tab bar with current panels
    /// </summary>
    private void RebuildTabBar()
    {
        _tabsContainer.Layout.Clear(true);

        for (int i = 0; i < _panels.Count; i++)
        {
            var index = i;
            var panel = _panels[i];
            var tabName = GetPanelTabName(panel);
            var tabIcon = GetPanelTabIcon(panel);

            var tab = new TabButton(_tabsContainer, tabIcon, tabName, index == _activeTabIndex);
            tab.OnClick = () => SelectTab(index);
            tab.OnCloseClick = () => RemovePanel(panel);
            tab.ShowClose = _panels.Count > 1;

            _tabsContainer.Layout.Add(tab);
        }
    }

    /// <summary>
    /// Get display name for panel tab
    /// </summary>
    private string GetPanelTabName(Widget panel)
    {
        return panel switch
        {
            AssetBrowserPanel => "Tree",
            IconGridPanel => "Grid",
            CloudAssetPanel => "Cloud",
            CloudIconGridPanel => "Cloud Grid",
            _ => "Panel"
        };
    }

    /// <summary>
    /// Get icon for panel tab
    /// </summary>
    private string GetPanelTabIcon(Widget panel)
    {
        return panel switch
        {
            AssetBrowserPanel => "account_tree",
            IconGridPanel => "grid_view",
            CloudAssetPanel => "cloud",
            CloudIconGridPanel => "cloud",
            _ => "tab"
        };
    }

    /// <summary>
    /// Select a tab by index
    /// </summary>
    private void SelectTab(int index)
    {
        if (index < 0 || index >= _panels.Count)
            return;

        _activeTabIndex = index;

        // Show only selected panel
        for (int i = 0; i < _panels.Count; i++)
        {
            _panels[i].Visible = (i == _activeTabIndex);
        }

        RebuildTabBar();
    }
}

/// <summary>
/// State data for saving/restoring browser configuration
/// </summary>
[Serializable]
internal class BrowserState
{
    public bool IsTabMode { get; set; }
    public int ActiveTabIndex { get; set; }
    public List<string> PanelTypes { get; set; } = new();
}

/// <summary>
/// Custom tab button widget for the tab bar
/// </summary>
internal class TabButton : Widget
{
    public Action OnClick;
    public Action OnCloseClick;
    public bool ShowClose { get; set; } = true;

    private string _icon;
    private string _text;
    private bool _isActive;
    private bool _isHovered;
    private bool _isCloseHovered;
    private Rect _closeRect;

    public TabButton(Widget parent, string icon, string text, bool isActive) : base(parent)
    {
        _icon = icon;
        _text = text;
        _isActive = isActive;
        MinimumWidth = 50;
        MaximumWidth = 150;
        Cursor = CursorShape.Finger;
        SetSizeMode(SizeMode.CanGrow, SizeMode.Default);
    }

    protected override void OnPaint()
    {
        var rect = LocalRect;

        // Background
        if (_isActive)
        {
            Paint.SetBrush(Theme.Primary.WithAlpha(0.3f));
            Paint.SetPen(Theme.Primary);
        }
        else if (_isHovered)
        {
            Paint.SetBrush(Color.White.WithAlpha(0.05f));
            Paint.SetPen(Color.White.WithAlpha(0.2f));
        }
        else
        {
            Paint.SetBrush(Color.White.WithAlpha(0.02f));
            Paint.SetPen(Color.White.WithAlpha(0.1f));
        }

        Paint.DrawRect(rect, 4);

        // Icon
        var iconRect = rect;
        iconRect.Left += 6;
        iconRect.Width = 16;
        Paint.SetPen(_isActive ? Theme.Primary : Color.White.WithAlpha(0.7f));
        Paint.DrawIcon(iconRect, _icon, 14, TextFlag.LeftCenter);

        // Text
        var textRect = rect;
        textRect.Left += 24;
        textRect.Right -= ShowClose ? 22 : 6;
        Paint.SetPen(_isActive ? Color.White : Color.White.WithAlpha(0.7f));
        Paint.SetDefaultFont(8, 400);
        Paint.DrawText(textRect, _text, TextFlag.LeftCenter);

        // Close button
        if (ShowClose)
        {
            _closeRect = rect;
            _closeRect.Left = rect.Right - 20;
            _closeRect.Width = 16;

            Paint.SetPen(_isCloseHovered ? Theme.Red : Color.White.WithAlpha(0.5f));
            Paint.DrawIcon(_closeRect, "close", 12, TextFlag.Center);
        }
    }

    protected override void OnMouseEnter()
    {
        _isHovered = true;
        Update();
    }

    protected override void OnMouseLeave()
    {
        _isHovered = false;
        _isCloseHovered = false;
        Update();
    }

    protected override void OnMouseMove(MouseEvent e)
    {
        var wasCloseHovered = _isCloseHovered;
        _isCloseHovered = ShowClose && _closeRect.IsInside(e.LocalPosition);

        if (wasCloseHovered != _isCloseHovered)
            Update();
    }

    protected override void OnMousePress(MouseEvent e)
    {
        if (e.LeftMouseButton)
        {
            if (ShowClose && _closeRect.IsInside(e.LocalPosition))
            {
                OnCloseClick?.Invoke();
            }
            else
            {
                OnClick?.Invoke();
            }
        }
    }
}
