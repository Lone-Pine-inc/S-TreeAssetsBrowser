using Editor;
using Sandbox;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GeneralGame.Editor;

/// <summary>
/// Tree node representing a cloud package folder (parent project loaded from cloud)
/// </summary>
public class PackageFolderNode : TreeNode
{
    public Package Package { get; }
    public string DisplayName { get; }
    public string IconName { get; set; }

    /// <summary>
    /// Tracks whether this folder is currently expanded in the tree view.
    /// Updated during OnPaint.
    /// </summary>
    public bool IsExpanded { get; private set; }

    private List<string> _cachedFiles;

    public override bool HasChildren => GetPackageFiles().Any();
    public override string Name => DisplayName;
    public override bool CanEdit => false;

    public PackageFolderNode(Package package, string icon = "cloud") : base()
    {
        Package = package;
        DisplayName = package.Title;
        IconName = icon;
        Value = this;
    }

    private List<string> GetPackageFiles()
    {
        if (_cachedFiles == null)
        {
            try
            {
                _cachedFiles = AssetSystem.GetPackageFiles(Package).ToList();
            }
            catch
            {
                _cachedFiles = new List<string>();
            }
        }
        return _cachedFiles;
    }

    protected override void BuildChildren()
    {
        Clear();

        var files = GetPackageFiles();
        if (!files.Any())
            return;

        try
        {
            // Group files by directory structure
            var directories = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var rootFiles = new List<string>();

            foreach (var file in files)
            {
                var parts = file.Split('/', '\\');
                if (parts.Length > 1)
                {
                    var topDir = parts[0];
                    if (!directories.ContainsKey(topDir))
                        directories[topDir] = new List<string>();
                    directories[topDir].Add(file);
                }
                else
                {
                    rootFiles.Add(file);
                }
            }

            // Add subdirectories
            foreach (var dir in directories.OrderBy(d => d.Key, StringComparer.OrdinalIgnoreCase))
            {
                AddItem(new PackageSubFolderNode(Package, dir.Key, dir.Value));
            }

            // Add root files
            foreach (var file in rootFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var fullPath = global::Editor.FileSystem.Cloud.GetFullPath(file);
                if (!string.IsNullOrEmpty(fullPath))
                {
                    AddItem(new AssetFileNode(fullPath));
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"Error building package tree: {ex.Message}");
        }
    }

    public override void OnPaint(VirtualWidget item)
    {
        // Track expanded state for external access
        IsExpanded = item.IsOpen;

        PaintSelection(item);

        var rect = item.Rect;

        // Package icon
        Paint.SetPen(Theme.Blue);
        Paint.DrawIcon(rect, item.IsOpen ? "folder_open" : IconName, 16, TextFlag.LeftCenter);

        rect.Left += 22;

        // Package name
        Paint.SetPen(Theme.Text);
        Paint.SetDefaultFont();
        Paint.DrawText(rect, DisplayName, TextFlag.LeftCenter);
    }

    public override bool OnContextMenu()
    {
        var menu = new ContextMenu(null);

        menu.AddOption("Refresh", "refresh", () =>
        {
            _cachedFiles = null;
            Dirty();
        });

        menu.AddOption("View on asset.party", "open_in_new", () =>
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = $"https://asset.party/{Package.FullIdent}",
                UseShellExecute = true
            });
        });

        menu.OpenAtCursor();
        return true;
    }

    /// <summary>
    /// Check if this folder or any of its contents matches the search filter.
    /// </summary>
    public bool MatchesFilter(string filter)
    {
        if (string.IsNullOrEmpty(filter))
            return true;

        // Check package name
        if (DisplayName.ToLowerInvariant().Contains(filter))
            return true;

        // Check files
        foreach (var file in GetPackageFiles())
        {
            if (file.ToLowerInvariant().Contains(filter))
                return true;
        }

        return false;
    }
}

/// <summary>
/// Subfolder within a package
/// </summary>
public class PackageSubFolderNode : TreeNode
{
    public Package Package { get; }
    public string FolderName { get; }
    public List<string> Files { get; }

    /// <summary>
    /// Tracks whether this folder is currently expanded in the tree view.
    /// Updated during OnPaint.
    /// </summary>
    public bool IsExpanded { get; private set; }

    public override bool HasChildren => Files.Any();
    public override string Name => FolderName;
    public override bool CanEdit => false;

    public PackageSubFolderNode(Package package, string folderName, List<string> files) : base()
    {
        Package = package;
        FolderName = folderName;
        Files = files;
        Value = this;
    }

    protected override void BuildChildren()
    {
        Clear();

        // Group by next level directory
        var subDirs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var currentFiles = new List<string>();

        foreach (var file in Files)
        {
            // Remove the current folder prefix
            var relativePath = file;
            if (file.StartsWith(FolderName + "/", StringComparison.OrdinalIgnoreCase) ||
                file.StartsWith(FolderName + "\\", StringComparison.OrdinalIgnoreCase))
            {
                relativePath = file.Substring(FolderName.Length + 1);
            }

            var parts = relativePath.Split('/', '\\');
            if (parts.Length > 1)
            {
                var subDir = parts[0];
                if (!subDirs.ContainsKey(subDir))
                    subDirs[subDir] = new List<string>();
                subDirs[subDir].Add(file);
            }
            else
            {
                currentFiles.Add(file);
            }
        }

        // Add subdirectories
        foreach (var dir in subDirs.OrderBy(d => d.Key, StringComparer.OrdinalIgnoreCase))
        {
            AddItem(new PackageSubFolderNode(Package, FolderName + "/" + dir.Key, dir.Value));
        }

        // Add files
        foreach (var file in currentFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var fullPath = global::Editor.FileSystem.Cloud.GetFullPath(file);
            if (!string.IsNullOrEmpty(fullPath))
            {
                AddItem(new AssetFileNode(fullPath));
            }
        }
    }

    public override void OnPaint(VirtualWidget item)
    {
        // Track expanded state for external access
        IsExpanded = item.IsOpen;

        PaintSelection(item);

        var rect = item.Rect;

        Paint.SetPen(Theme.Yellow);
        Paint.DrawIcon(rect, item.IsOpen ? "folder_open" : "folder", 16, TextFlag.LeftCenter);

        rect.Left += 22;

        Paint.SetPen(Theme.Text);
        Paint.SetDefaultFont();

        // Show just the folder name, not full path
        var displayName = FolderName.Contains('/') ? FolderName.Split('/').Last() : FolderName;
        Paint.DrawText(rect, displayName, TextFlag.LeftCenter);
    }

    public bool MatchesFilter(string filter)
    {
        if (string.IsNullOrEmpty(filter))
            return true;

        if (FolderName.ToLowerInvariant().Contains(filter))
            return true;

        foreach (var file in Files)
        {
            if (file.ToLowerInvariant().Contains(filter))
                return true;
        }

        return false;
    }
}
