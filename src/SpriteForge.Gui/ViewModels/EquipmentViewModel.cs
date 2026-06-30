using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Microsoft.Win32;
using SpriteForge.Core.Models;
using SpriteForge.Gui.Mvvm;

namespace SpriteForge.Gui.ViewModels;

/// <summary>
/// View model for the equipment panel, allowing users to load, clear, and toggle equipment attachments.
/// </summary>
public sealed class EquipmentViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private bool _isEmpty = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="EquipmentViewModel"/> class.
    /// </summary>
    /// <param name="main">The root view model.</param>
    /// <exception cref="ArgumentNullException">Thrown if main is null.</exception>
    public EquipmentViewModel(MainViewModel main)
    {
        _main = main ?? throw new ArgumentNullException(nameof(main));
        LoadCommand = new RelayCommand(LoadEquipment);
        ClearCommand = new RelayCommand(ClearEquipment);
        Attachments.CollectionChanged += (s, e) => IsEmpty = Attachments.Count == 0;
    }

    /// <summary>
    /// Gets the command to load an equipment manifest JSON file.
    /// </summary>
    public RelayCommand LoadCommand { get; }

    /// <summary>
    /// Gets the command to clear all loaded equipment.
    /// </summary>
    public RelayCommand ClearCommand { get; }

    /// <summary>
    /// Gets the collection of equipment attachment items.
    /// </summary>
    public ObservableCollection<AttachmentItem> Attachments { get; } = new();

    /// <summary>
    /// Gets a value indicating whether no equipment is loaded.
    /// </summary>
    public bool IsEmpty
    {
        get => _isEmpty;
        private set => SetField(ref _isEmpty, value);
    }

    private void LoadEquipment()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select equipment manifest",
            Filter = "Equipment manifest (*.json)|*.json|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var manifest = EquipmentManifestLoader.Load(dialog.FileName);
                Attachments.Clear();
                foreach (var attachment in manifest.Attachments)
                {
                    Attachments.Add(new AttachmentItem(attachment, OnAttachmentChanged));
                }

                _main.EquipmentProvider = BuildManifest;
                _main.Reload();
            }
            catch (Exception ex)
            {
                _main.Status = $"Load equipment failed: {ex.Message}";
            }
        }
    }

    private void ClearEquipment()
    {
        Attachments.Clear();
        _main.EquipmentProvider = null;
        _main.Reload();
    }

    private void OnAttachmentChanged()
    {
        // note: toggling reloads the whole model — correct but heavier than a live swap
        _main.Reload();
    }

    private EquipmentManifest? BuildManifest()
    {
        var enabledAttachments = Attachments
            .Where(x => x.IsEnabled)
            .Select(x => x.Attachment)
            .ToList();

        if (enabledAttachments.Count == 0)
        {
            return null;
        }

        return new EquipmentManifest { Attachments = enabledAttachments };
    }
}

/// <summary>
/// Represents a single equipment attachment item in the UI.
/// </summary>
public sealed class AttachmentItem : ObservableObject
{
    private readonly Action _onChanged;
    private bool _isEnabled = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="AttachmentItem"/> class.
    /// </summary>
    /// <param name="attachment">The core attachment model.</param>
    /// <param name="onChanged">Callback executed when the enabled state changes.</param>
    /// <exception cref="ArgumentNullException">Thrown if attachment or onChanged is null.</exception>
    public AttachmentItem(Attachment attachment, Action onChanged)
    {
        Attachment = attachment ?? throw new ArgumentNullException(nameof(attachment));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        Name = string.IsNullOrWhiteSpace(attachment.Name) ? "Unnamed" : attachment.Name;
        SocketLabel = attachment.UseMasterPose ? "(master pose)" : (attachment.SocketBone ?? string.Empty);
    }

    /// <summary>
    /// Gets the underlying attachment model.
    /// </summary>
    public Attachment Attachment { get; }

    /// <summary>
    /// Gets the name of the attachment.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the socket bone label or "(master pose)".
    /// </summary>
    public string SocketLabel { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the attachment is enabled.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetField(ref _isEnabled, value))
            {
                _onChanged();
            }
        }
    }
}
