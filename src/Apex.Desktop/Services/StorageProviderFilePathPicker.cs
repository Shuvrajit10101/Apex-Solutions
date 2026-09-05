using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Apex.Desktop.Services;

/// <summary>
/// The real <see cref="IFilePathPicker"/>: the operating system's own file and folder dialogs, reached through
/// Avalonia's <see cref="IStorageProvider"/>.
///
/// <para><b>This class is the substituted seam</b> — it is the one piece of the chooser feature that a headless
/// test cannot drive, because there is no OS dialog to open. Everything decided <i>around</i> it (which screen
/// asks for which shape of path, where the answer lands, that cancel changes nothing, that the affordance is
/// reachable by chord and by button) is pinned by <c>FilePathPickerReachabilityTests</c> against the real window.
/// So this file is kept deliberately thin: translate the request, call the provider, hand back a local path.</para>
///
/// <para><b>Failure is silent by design.</b> A platform with no storage provider, or a chosen item with no local
/// path (a cloud location on some backends), yields <c>null</c> — which every caller already treats as "the
/// operator cancelled", i.e. leave the typed path exactly as it was. The typed path stays the fallback on every
/// screen, so the chooser can never become the only way in.</para>
/// </summary>
public sealed class StorageProviderFilePathPicker : IFilePathPicker
{
    private readonly TopLevel _owner;

    public StorageProviderFilePathPicker(TopLevel owner) =>
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    public async Task<string?> PickAsync(FilePathPickRequest request)
    {
        if (request is null) return null;

        var provider = _owner.StorageProvider;
        if (provider is null) return null;

        var start = await StartLocationAsync(provider, request);

        switch (request.Kind)
        {
            case FilePathPickKind.Folder:
            {
                if (!provider.CanPickFolder) return null;
                var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = request.Title,
                    AllowMultiple = false,
                    SuggestedStartLocation = start,
                });
                return LocalPath(folders.Count > 0 ? folders[0] : null);
            }

            case FilePathPickKind.OpenFile:
            {
                if (!provider.CanOpen) return null;
                var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = request.Title,
                    AllowMultiple = false,
                    SuggestedStartLocation = start,
                    FileTypeFilter = Translate(request.FileTypes),
                });
                return LocalPath(files.Count > 0 ? files[0] : null);
            }

            case FilePathPickKind.SaveFile:
            {
                if (!provider.CanSave) return null;
                var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = request.Title,
                    SuggestedFileName = request.SuggestedFileName,
                    SuggestedStartLocation = start,
                    FileTypeChoices = Translate(request.FileTypes),
                });
                return LocalPath(file);
            }

            default:
                return null;
        }
    }

    /// <summary>Opens the dialog where the operator already is, when that folder still exists.</summary>
    private static async Task<IStorageFolder?> StartLocationAsync(IStorageProvider provider, FilePathPickRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.StartFolder)) return null;
        try { return await provider.TryGetFolderFromPathAsync(r.StartFolder); }
        catch (Exception) { return null; }   // an unreadable or malformed path is not a reason to refuse the dialog
    }

    private static List<FilePickerFileType>? Translate(IReadOnlyList<FilePathFileType> types)
    {
        if (types is null || types.Count == 0) return null;
        return types
            .Select(t => new FilePickerFileType(t.Name) { Patterns = t.Patterns.ToArray() })
            .ToList();
    }

    private static string? LocalPath(IStorageItem? item)
    {
        if (item is null) return null;
        var local = item.TryGetLocalPath();
        return string.IsNullOrWhiteSpace(local) ? null : local;
    }
}
