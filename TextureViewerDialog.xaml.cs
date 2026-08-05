using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using LbpArchiveToolkit.Utils;

namespace LbpArchiveToolkit
{
    public class TextureItem
    {
        public BitmapSource? ImageSource { get; set; }
        public string Hash { get; set; } = "";
        public byte[]? RawData { get; set; }
        public string Dimensions => ImageSource != null ? $"{ImageSource.PixelWidth}x{ImageSource.PixelHeight}" : "";
    }

    public partial class TextureViewerDialog : Window
    {
        public ObservableCollection<TextureItem> Textures { get; } = new();

        public TextureViewerDialog(string backupPath, string levelName)
        {
            InitializeComponent();
            txtTitle.Text = $"TEXTURES: {levelName.ToUpper()}";
            itemsControl.ItemsSource = Textures;
            LoadTexturesAsync(backupPath);
        }

        private async void LoadTexturesAsync(string path)
        {
            try
            {
                txtStatus.Text = "Reading local archive...";
                var archiveData = await Task.Run(() => Far4Archive.ReadSaveArchive(path));
                
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
                    var bmp = await Task.Run(() => TextureDecoder.DecodeToBitmapSourceCentered(kvp.Value, -1, false));
                    if (bmp != null)
                    {
                        Textures.Add(new TextureItem 
                        { 
                            ImageSource = bmp, 
                            Hash = kvp.Key,
                            RawData = kvp.Value
                        });
                        loaded++;
                    }
                }
                txtStatus.Text = $"Found {loaded} images in archive.";
            }
            catch (Exception ex)
            {
                txtStatus.Text = "Failed to load textures.";
                CustomDialog.Show(this, ex.Message, "Error");
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem mi && mi.CommandParameter is TextureItem item)
            {
                var dlg = new SaveFileDialog
                {
                    Filter = "PNG Image|*.png",
                    Title = "Export Texture",
                    FileName = item.Hash + ".png"
                };

                if (dlg.ShowDialog() == true)
                {
                    try
                    {
                        BitmapSource? exportBmp = null;
                        if (item.RawData != null && item.RawData.Length > 0)
                        {
                            // Re-decode at full resolution for export
                            exportBmp = TextureDecoder.DecodeToBitmapSourceCentered(item.RawData, -1, false);
                        }
                        else if (item.ImageSource != null)
                        {
                            exportBmp = item.ImageSource;
                        }

                        if (exportBmp != null)
                        {
                            var encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(exportBmp));
                            using var fs = File.Create(dlg.FileName);
                            encoder.Save(fs);
                            CustomDialog.Show(this, "Texture exported successfully!", "Success");
                        }
                    }
                    catch (Exception ex)
                    {
                        CustomDialog.Show(this, $"Failed to export:\n{ex.Message}", "Error");
                    }
                }
            }
        }

        private async void ExportAll_Click(object sender, RoutedEventArgs e)
        {
            if (Textures.Count == 0)
            {
                CustomDialog.Show(this, "No textures available to export.", "Notice");
                return;
            }

            var dlg = new OpenFolderDialog
            {
                Title = "Select Folder for Exported Textures"
            };

            if (dlg.ShowDialog() == true)
            {
                string targetDir = dlg.FolderName;
                int exportedCount = 0;
                int failedCount = 0;

                txtStatus.Text = "Exporting all textures...";

                await Task.Run(() =>
                {
                    foreach (var item in Textures)
                    {
                        try
                        {
                            BitmapSource? exportBmp = null;
                            if (item.RawData != null && item.RawData.Length > 0)
                            {
                                exportBmp = TextureDecoder.DecodeToBitmapSourceCentered(item.RawData, -1, false);
                            }
                            else if (item.ImageSource != null)
                            {
                                exportBmp = item.ImageSource;
                            }

                            if (exportBmp != null)
                            {
                                string filePath = Path.Combine(targetDir, item.Hash + ".png");
                                var encoder = new PngBitmapEncoder();
                                encoder.Frames.Add(BitmapFrame.Create(exportBmp));
                                using var fs = File.Create(filePath);
                                encoder.Save(fs);
                                exportedCount++;
                            }
                        }
                        catch
                        {
                            failedCount++;
                        }
                    }
                });

                txtStatus.Text = $"Exported {exportedCount} texture(s) to folder.";
                if (failedCount > 0)
                {
                    CustomDialog.Show(this, $"Exported {exportedCount} texture(s) successfully.\nFailed to export {failedCount} texture(s).", "Export Finished");
                }
                else
                {
                    CustomDialog.Show(this, $"Successfully exported all {exportedCount} texture(s)!", "Success");
                }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
    }
}