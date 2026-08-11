using LbpArchiveToolkit.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;

namespace LbpArchiveToolkit.ViewModels
{
    public class TextureItem
    {
        public object? ImageSource { get; set; }
        public string Hash { get; set; } = "";
        public string Dimensions { get; set; } = "";
    }

    public class TextureViewerDialogViewModel : ViewModelBase
    {
        private readonly IViewService _viewService;
        private readonly string _backupPath;

        public ObservableCollection<TextureItem> Textures { get; } = new();

        public string Title { get; set => SetProperty(ref field, value); }
        public string StatusText { get; set => SetProperty(ref field, value); } = "Loading archive...";

        public ICommand ExportCommand { get; }
        public ICommand ExportAllCommand { get; }
        public ICommand CloseCommand { get; }

        public Action? RequestClose { get; set; }

        public TextureViewerDialogViewModel(IViewService viewService, string backupPath, string levelName) : base(viewService)
        {
            _viewService = viewService;
            _backupPath = backupPath;
            Title = $"TEXTURES: {levelName.ToUpper()}";

            ExportCommand = new RelayCommand(ExecuteExport);
            ExportAllCommand = new RelayCommand(ExecuteExportAll);
            CloseCommand = new RelayCommand(_ => RequestClose?.Invoke());

            LoadTexturesAsync();
        }

        private async void LoadTexturesAsync()
        {
            try
            {
                StatusText = "Reading local archive...";
                var archiveData = await Task.Run(() => Far4Archive.ReadSaveArchive(_backupPath));
                
                // Filter non-textures immediately to free unused archive memory
                var textureCandidates = new List<KeyValuePair<string, byte[]>>();
                foreach (var kvp in archiveData.hashes)
                {
                    if (kvp.Value.Length > 16)
                    {
                        uint magic = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(kvp.Value.AsSpan(0, 4));
                        string typeStr = System.Text.Encoding.ASCII.GetString(kvp.Value, 0, 3);

                        if (magic == 0x89504E47 || (magic & 0xFFFF0000) == 0xFFD80000 || magic == 0x44445320 || typeStr == "TEX" || typeStr == "GTF")
                        {
                            textureCandidates.Add(kvp);
                        }
                    }
                }

                archiveData.hashes.Clear();

                int loaded = 0;
                foreach (var kvp in textureCandidates)
                {
                    var result = await Task.Run(() => _viewService.DecodeImage(kvp.Value, -1, false));
                    if (result.Image != null)
                    {
                        Textures.Add(new TextureItem 
                        { 
                            ImageSource = result.Image, 
                            Hash = kvp.Key,
                            Dimensions = $"{result.Width}x{result.Height}"
                        });
                        loaded++;
                    }
                }
                StatusText = $"Found {loaded} images in archive.";
            }
            catch (Exception ex)
            {
                StatusText = "Failed to load textures.";
                _viewService.Alert(ex.Message, "Error");
            }
        }

        private void ExecuteExport(object? parameter)
        {
            if (parameter is TextureItem item)
            {
                string? fileName = _viewService.ShowSaveFileDialog("PNG Image|*.png", "Export Texture", item.Hash + ".png");

                if (fileName != null)
                {
                    try
                    {
                        if (item.ImageSource != null)
                        {
                            _viewService.SaveImageToFile(item.ImageSource, fileName);
                            _viewService.Alert("Texture exported successfully!", "Success");
                        }
                    }
                    catch (Exception ex)
                    {
                        _viewService.Alert($"Failed to export:\n{ex.Message}", "Error");
                    }
                }
            }
        }

        private async void ExecuteExportAll(object? parameter)
        {
            if (Textures.Count == 0)
            {
                _viewService.Alert("No textures available to export.", "Notice");
                return;
            }

            string? targetDir = _viewService.ShowOpenFolderDialog("Select Folder for Exported Textures");

            if (targetDir != null)
            {
                int exportedCount = 0;
                int failedCount = 0;

                StatusText = "Exporting all textures...";

                await Task.Run(() =>
                {
                    foreach (var item in Textures)
                    {
                        try
                        {
                            if (item.ImageSource != null)
                            {
                                string filePath = Path.Combine(targetDir, item.Hash + ".png");
                                _viewService.SaveImageToFile(item.ImageSource, filePath);
                                exportedCount++;
                            }
                        }
                        catch
                        {
                            failedCount++;
                        }
                    }
                });

                StatusText = $"Exported {exportedCount} texture(s) to folder.";
                if (failedCount > 0)
                {
                    _viewService.Alert($"Exported {exportedCount} texture(s) successfully.\nFailed to export {failedCount} texture(s).", "Export Finished");
                }
                else
                {
                    _viewService.Alert($"Successfully exported all {exportedCount} texture(s)!", "Success");
                }
            }
        }
    }
}