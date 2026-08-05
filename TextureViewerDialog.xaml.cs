using System;
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
                var archiveData = await Task.Run(() => Far4Archive.ReadSaveArchive(path));
                int loaded = 0;

                foreach (var kvp in archiveData.hashes)
                {
                    if (kvp.Value.Length > 16)
                    {
                        uint magic = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(kvp.Value.AsSpan(0, 4));
                        string typeStr = System.Text.Encoding.ASCII.GetString(kvp.Value, 0, 3);

                        if (magic == 0x89504E47 || (magic & 0xFFFF0000) == 0xFFD80000 || magic == 0x44445320 || typeStr == "TEX" || typeStr == "GTF")
                        {
                            var bmp = await Task.Run(() => TextureDecoder.DecodeToBitmapSourceCentered(kvp.Value, -1, false));
                            if (bmp != null)
                            {
                                Textures.Add(new TextureItem { ImageSource = bmp, Hash = kvp.Key });
                                loaded++;
                            }
                        }
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
            if (sender is System.Windows.Controls.MenuItem mi && mi.CommandParameter is TextureItem item && item.ImageSource != null)
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
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(item.ImageSource));
                        using var fs = File.Create(dlg.FileName);
                        encoder.Save(fs);
                        CustomDialog.Show(this, "Texture exported successfully!", "Success");
                    }
                    catch (Exception ex)
                    {
                        CustomDialog.Show(this, $"Failed to export:\n{ex.Message}", "Error");
                    }
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