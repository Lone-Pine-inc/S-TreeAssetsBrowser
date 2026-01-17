using Editor;
using Sandbox;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GeneralGame.Editor;

/// <summary>
/// Tree node representing a file/asset in the asset browser
/// </summary>
public class AssetFileNode : TreeNode
{
    public string FullPath { get; }
    public string FileName { get; }
    public string Extension { get; }
    public Asset Asset { get; private set; }

    public override bool HasChildren => false;
    public override string Name => Path.GetFileNameWithoutExtension(FileName);
    public override bool CanEdit => true;

    private static readonly Dictionary<string, string> ExtensionIcons = new()
    {
        // Models
        { ".vmdl", "view_in_ar" },
        { ".fbx", "view_in_ar" },
        { ".obj", "view_in_ar" },
        { ".gltf", "view_in_ar" },
        { ".glb", "view_in_ar" },
        
        // Textures
        { ".png", "image" },
        { ".jpg", "image" },
        { ".jpeg", "image" },
        { ".tga", "image" },
        { ".vtex", "image" },
        { ".psd", "image" },

        // Materials
        { ".vmat", "texture" },

        // Sounds
        { ".wav", "audiotrack" },
        { ".mp3", "audiotrack" },
        { ".ogg", "audiotrack" },
        { ".vsnd", "audiotrack" },

        // Code
        { ".cs", "code" },
        { ".razor", "code" },
        { ".scss", "style" },
        { ".css", "style" },
        { ".shader", "gradient" },
        { ".shdrgrph", "gradient" },

        // Prefabs & Scenes
        { ".prefab", "inventory_2" },
        { ".scene", "landscape" },

        // Data
        { ".json", "data_object" },
        { ".xml", "data_object" },
        { ".txt", "description" },
        { ".md", "description" },

        // Resources
        { ".item", "category" },
        { ".clothing", "checkroom" },
        { ".weapon", "sports_martial_arts" },

        // Other
        { ".particle", "blur_on" },
        { ".vanmgrph", "animation" },
        { ".vpost", "auto_fix_high" }
    };

    private static readonly Dictionary<string, Color> ExtensionColors = new()
    {
        { ".cs", new Color(0.4f, 0.7f, 1.0f) },      // Blue for C#
        { ".razor", new Color(0.6f, 0.4f, 0.9f) },  // Purple for Razor
        { ".shader", new Color(0.3f, 0.9f, 0.5f) }, // Green for shaders
        { ".vmdl", new Color(0.9f, 0.6f, 0.3f) },   // Orange for models
        { ".vmat", new Color(0.9f, 0.4f, 0.6f) },   // Pink for materials
        { ".prefab", new Color(0.3f, 0.8f, 0.9f) }, // Cyan for prefabs
        { ".scene", new Color(0.9f, 0.9f, 0.3f) },  // Yellow for scenes
    };

    public AssetFileNode(string path) : base()
    {
        FullPath = Path.GetFullPath(path);
        FileName = Path.GetFileName(path);
        Extension = Path.GetExtension(path).ToLowerInvariant();
        Value = this;

        // Try to find associated asset
        Asset = AssetSystem.FindByPath(path);
    }

    public override void OnPaint(VirtualWidget item)
    {
        PaintSelection(item);

        var rect = item.Rect;

        // Get icon
        var icon = GetIcon();
        var iconColor = GetIconColor();

        // Draw icon
        Paint.SetPen(iconColor);
        Paint.DrawIcon(rect, icon, 14, TextFlag.LeftCenter);

        rect.Left += 20;

        // Draw filename
        Paint.SetPen(Theme.Text);
        Paint.SetDefaultFont(8, 400);

        var nameWithoutExt = Path.GetFileNameWithoutExtension(FileName);
        var nameRect = Paint.MeasureText(rect, nameWithoutExt, TextFlag.LeftCenter);
        Paint.DrawText(rect, nameWithoutExt, TextFlag.LeftCenter);

        // Draw extension in dimmer color
        rect.Left += nameRect.Width + 1;
        Paint.SetPen(Theme.Text.WithAlpha(0.4f));
        Paint.SetDefaultFont(7, 400);
        Paint.DrawText(rect, Extension, TextFlag.LeftCenter);
    }

    private string GetIcon()
    {
        // Use extension-based icon
        if (ExtensionIcons.TryGetValue(Extension, out var icon))
        {
            return icon;
        }

        return "description"; // Default file icon
    }

    private Color GetIconColor()
    {
        // Use asset type color if available
        if (Asset?.AssetType?.Color != null && Asset.AssetType.Color != default)
        {
            return Asset.AssetType.Color;
        }

        // Use extension-based color
        if (ExtensionColors.TryGetValue(Extension, out var color))
        {
            return color;
        }

        return Theme.Text.WithAlpha(0.7f);
    }

    public override bool OnContextMenu()
    {
        var menu = new ContextMenu(null);

        // Open options
        if (Asset != null)
        {
            menu.AddOption("Open in Editor", "edit", () => Asset.OpenInEditor());
        }
        else
        {
            menu.AddOption("Open", "open_in_new", () => EditorUtility.OpenFolder(FullPath));
        }
        menu.AddOption("Show in Explorer", "folder_open", () => EditorUtility.OpenFileFolder(FullPath));

        menu.AddSeparator();

        // Copy options
        if (Asset != null)
        {
            menu.AddOption("Copy Relative Path", "content_paste_go", () => EditorUtility.Clipboard.Copy(Asset.RelativePath));
        }
        menu.AddOption("Copy Absolute Path", "content_paste", () => EditorUtility.Clipboard.Copy(FullPath));

        // Asset-type specific options (Create Material, Create Texture, etc.)
        AssetContextMenuHelper.AddAssetTypeOptions(menu, Asset);

        menu.AddSeparator();

        // Edit options
        menu.AddOption("Rename", "edit", () => ShowRenameDialog());
        menu.AddOption("Duplicate", "file_copy", () => DuplicateFile());

        menu.AddSeparator();

        // Create submenu for quick asset creation in same folder
        var parentFolder = Path.GetDirectoryName(FullPath);
        if (!string.IsNullOrEmpty(parentFolder))
        {
            var createMenu = menu.AddMenu("Create", "add");
            AssetCreator.AddOptions(createMenu, parentFolder);
            menu.AddSeparator();
        }

        menu.AddOption("Delete", "delete", () =>
        {
            var confirm = new PopupWindow(
                "Delete File",
                $"Are you sure you want to delete '{FileName}'?",
                "Cancel",
                new Dictionary<string, Action>()
                {
                    { "Delete", () =>
                        {
                            try
                            {
                                DeleteFileWithCompiled();
                                Parent?.Dirty();
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
        });

        menu.OpenAtCursor();
        return true;
    }

    private void ShowRenameDialog()
    {
        var dialog = new RenameDialog("Rename File", Path.GetFileNameWithoutExtension(FileName));
        dialog.OnConfirm = (newName) =>
        {
            if (string.IsNullOrWhiteSpace(newName))
                return;

            // Preserve extension if not provided
            if (!Path.HasExtension(newName))
            {
                newName += Extension;
            }

            if (newName == FileName)
                return;

            var newPath = Path.Combine(Path.GetDirectoryName(FullPath), newName);
            try
            {
                File.Move(FullPath, newPath);
                Parent?.Dirty();
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to rename file: {ex.Message}");
            }
        };
        dialog.Show();
    }

    private void DuplicateFile()
    {
        try
        {
            var directory = Path.GetDirectoryName(FullPath);
            var nameWithoutExt = Path.GetFileNameWithoutExtension(FileName);
            var extension = Path.GetExtension(FileName);

            var newName = $"{nameWithoutExt}_copy{extension}";
            var newPath = Path.Combine(directory, newName);

            var counter = 1;
            while (File.Exists(newPath))
            {
                newName = $"{nameWithoutExt}_copy{counter++}{extension}";
                newPath = Path.Combine(directory, newName);
            }

            File.Copy(FullPath, newPath);
            Parent?.Dirty();
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to duplicate file: {ex.Message}");
        }
    }

    /// <summary>
    /// Delete the file and its compiled _c version if it exists
    /// </summary>
    private void DeleteFileWithCompiled()
    {
        // If we have an Asset, use its Delete method which handles compiled files
        if (Asset != null)
        {
            Asset.Delete();
            return;
        }

        // Otherwise manually delete the file and any compiled version
        File.Delete(FullPath);

        // Check for compiled _c version and delete it too
        var compiledPath = FullPath + "_c";
        if (File.Exists(compiledPath))
        {
            File.Delete(compiledPath);
        }
    }

    public override void OnRename(VirtualWidget item, string text, List<TreeNode> selection = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        // Preserve extension if not provided
        var newName = text;
        if (!Path.HasExtension(newName))
        {
            newName += Extension;
        }

        if (newName == FileName)
            return;

        var newPath = Path.Combine(Path.GetDirectoryName(FullPath), newName);

        try
        {
            if (Asset != null)
            {
                EditorUtility.MoveAssetToDirectory(Asset, Path.GetDirectoryName(newPath));
            }
            else
            {
                File.Move(FullPath, newPath);
            }
            Parent?.Dirty();
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to rename file: {ex.Message}");
        }
    }

    public override bool OnDragStart()
    {
        var drag = new Drag(TreeView);

        if (Asset != null)
        {
            drag.Data.Object = Asset;
        }

        drag.Data.Text = FullPath;
        drag.Data.Url = new Uri("file:///" + FullPath);
        drag.Execute();
        return true;
    }

    public override void OnActivated()
    {
        if (Asset != null)
        {
            Asset.OpenInEditor();
        }
        else
        {
            EditorUtility.OpenFolder(FullPath);
        }
    }

    /// <summary>
    /// Check if this file matches the search filter
    /// </summary>
    public bool MatchesFilter(string filter)
    {
        if (string.IsNullOrEmpty(filter))
            return true;

        // Check filename
        if (FileName.ToLowerInvariant().Contains(filter))
            return true;

        // Check asset type
        if (Asset?.AssetType?.FriendlyName?.ToLowerInvariant().Contains(filter) == true)
            return true;

        return false;
    }

    public override string GetTooltip()
    {
        var tip = FullPath;

        if (Asset != null)
        {
            tip += $"\nType: {Asset.AssetType?.FriendlyName ?? "Unknown"}";
        }

        var fileInfo = new FileInfo(FullPath);
        if (fileInfo.Exists)
        {
            tip += $"\nSize: {FormatFileSize(fileInfo.Length)}";
            tip += $"\nModified: {fileInfo.LastWriteTime:g}";
        }

        return tip;
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
}
