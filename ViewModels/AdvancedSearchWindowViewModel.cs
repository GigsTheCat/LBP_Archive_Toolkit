using LbpArchiveToolkit.Models;
using LbpArchiveToolkit.Utils;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace LbpArchiveToolkit.ViewModels
{
    public class SelectableTagViewModel : ViewModelBase
    {
        public string DisplayName { get; set; } = "";
        public string InternalTag { get; set; } = "";
        public double TiltAngle { get; set; }

        public bool IsSelected { get; set => SetProperty(ref field, value); }
    }

    public class AdvancedSearchWindowViewModel : ViewModelBase
    {
        public string MinHearts { get; set => SetProperty(ref field, value); } = "0";
        public string MinPlays { get; set => SetProperty(ref field, value); } = "0";
        public string MinHeartPercentage { get; set => SetProperty(ref field, value); } = "0";
        public string MinYayPercentage { get; set => SetProperty(ref field, value); } = "0";
        public string MinClearPercentage { get; set => SetProperty(ref field, value); } = "0";
        public string MaxClearPercentage { get; set => SetProperty(ref field, value); } = "100";
        public bool IsTeamPick { get; set => SetProperty(ref field, value); }

        public bool RequireLocked 
        { 
            get; 
            set
            {
                if (!_hasExtendedSlotProperties && value) { ShowDatabaseOutdatedPrompt("extended level properties (Locked, Sub-level, Copyable)"); OnPropertyChanged(nameof(RequireLocked)); return; }
                SetProperty(ref field, value);
            } 
        }

        public bool RequireSubLevel 
        { 
            get; 
            set
            {
                if (!_hasExtendedSlotProperties && value) { ShowDatabaseOutdatedPrompt("extended level properties (Locked, Sub-level, Copyable)"); OnPropertyChanged(nameof(RequireSubLevel)); return; }
                SetProperty(ref field, value);
            } 
        }

        public bool RequireShareable 
        { 
            get; 
            set
            {
                if (!_hasExtendedSlotProperties && value) { ShowDatabaseOutdatedPrompt("extended level properties (Locked, Sub-level, Copyable)"); OnPropertyChanged(nameof(RequireShareable)); return; }
                SetProperty(ref field, value);
            } 
        }

        public string MaxHearts { get; set => SetProperty(ref field, value); } = "0";
        public string MaxPlays { get; set => SetProperty(ref field, value); } = "0";
        public string ExcludedCreators { get; set => SetProperty(ref field, value); } = "";
        public string ExcludedContributors { get; set => SetProperty(ref field, value); } = "";
        public string ExcludedObjectContributors { get; set => SetProperty(ref field, value); } = "";
        public string PublishedBeforeYear { get; set => SetProperty(ref field, value); } = "Any";
        public string PublishedBeforeMonth { get; set => SetProperty(ref field, value); } = "Any";
        public string PublishedAfterYear { get; set => SetProperty(ref field, value); } = "Any";
        public string PublishedAfterMonth { get; set => SetProperty(ref field, value); } = "Any";

        public ObservableCollection<string> AvailableYears { get; } = new();
        public ObservableCollection<string> AvailableMonths { get; } = new();

        public bool ExcludeTeamPick { get; set => SetProperty(ref field, value); }

        public bool ExcludeLocked 
        { 
            get; 
            set
            {
                if (!_hasExtendedSlotProperties && value) { ShowDatabaseOutdatedPrompt("extended level properties (Locked, Sub-level, Copyable)"); OnPropertyChanged(nameof(ExcludeLocked)); return; }
                SetProperty(ref field, value);
            } 
        }

        public bool ExcludeSubLevels 
        { 
            get; 
            set
            {
                if (!_hasExtendedSlotProperties && value) { ShowDatabaseOutdatedPrompt("extended level properties (Locked, Sub-level, Copyable)"); OnPropertyChanged(nameof(ExcludeSubLevels)); return; }
                SetProperty(ref field, value);
            } 
        }

        public bool ExcludeShareable 
        { 
            get; 
            set
            {
                if (!_hasExtendedSlotProperties && value) { ShowDatabaseOutdatedPrompt("extended level properties (Locked, Sub-level, Copyable)"); OnPropertyChanged(nameof(ExcludeShareable)); return; }
                SetProperty(ref field, value);
            } 
        }

        public int LabelMatchMode
        {
            get;
            set
            {
                if (!_hasCommunityLabels && value != 1)
                {
                    ShowDatabaseOutdatedPrompt("community labels data");
                    OnPropertyChanged(nameof(LabelMatchMode));
                    return;
                }
                SetProperty(ref field, value);
            }
        }

        private void ShowDatabaseOutdatedPrompt(string missingFeature)
        {
            bool download = _viewService.Confirm(
                $"Your database is out of date and does not contain {missingFeature}.\n\n" +
                "To use this feature, you will need to download the updated database.\n\n" +
                "Would you like to open the download link now?", 
                "Database Out of Date");
            
            if (download)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://archive.org/download/fullfastdry") { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    LogManager.Log("AdvancedSearchWindowViewModel.ShowDatabaseOutdatedPrompt (Open Link)", ex);
                }
            }
        }

        // Categorized Collections for Data Binding
        public ObservableCollection<SelectableTagViewModel> Lbp2ExperienceLabels { get; } = new();
        public ObservableCollection<SelectableTagViewModel> Lbp2TypeLabels { get; } = new();
        public ObservableCollection<SelectableTagViewModel> Lbp2ContentLabels { get; } = new();
        
        public ObservableCollection<SelectableTagViewModel> Lbp3ExperienceLabels { get; } = new();
        public ObservableCollection<SelectableTagViewModel> Lbp3TypeLabels { get; } = new();
        public ObservableCollection<SelectableTagViewModel> Lbp3ContentLabels { get; } = new();
        public ObservableCollection<SelectableTagViewModel> Lbp3CharacterLabels { get; } = new();
        
        public ObservableCollection<SelectableTagViewModel> Lbp1Tags { get; } = new();

        // Excluded Categorized Collections
        public ObservableCollection<SelectableTagViewModel> ExcludedLbp2ExperienceLabels { get; } = new();
        public ObservableCollection<SelectableTagViewModel> ExcludedLbp2TypeLabels { get; } = new();
        public ObservableCollection<SelectableTagViewModel> ExcludedLbp2ContentLabels { get; } = new();
        
        public ObservableCollection<SelectableTagViewModel> ExcludedLbp3ExperienceLabels { get; } = new();
        public ObservableCollection<SelectableTagViewModel> ExcludedLbp3TypeLabels { get; } = new();
        public ObservableCollection<SelectableTagViewModel> ExcludedLbp3ContentLabels { get; } = new();
        public ObservableCollection<SelectableTagViewModel> ExcludedLbp3CharacterLabels { get; } = new();
        
        public ObservableCollection<SelectableTagViewModel> ExcludedLbp1Tags { get; } = new();

        public ICommand ClearCommand { get; }
        public ICommand SavePresetCommand { get; }
        public ICommand LoadPresetCommand { get; }
        public ICommand ApplyCommand { get; }
        public ICommand ApplyAndSearchCommand { get; }

        // Action to pass the updated criteria back to the Window
        public Action<AdvancedSearchCriteria, bool>? RequestClose { get; set; }

        private readonly bool _hasCommunityLabels;
        private readonly bool _hasExtendedSlotProperties;
        private readonly IViewService _viewService;

        public AdvancedSearchWindowViewModel(AdvancedSearchCriteria existingCriteria, bool hasCommunityLabels, bool hasExtendedSlotProperties, IViewService viewService)
        {
            _hasCommunityLabels = hasCommunityLabels;
            _hasExtendedSlotProperties = hasExtendedSlotProperties;
            _viewService = viewService;

            MinHearts = existingCriteria.MinHearts.ToString();
            MinPlays = existingCriteria.MinPlays.ToString();
            MinHeartPercentage = existingCriteria.MinHeartPercentage.ToString();
            MinYayPercentage = existingCriteria.MinYayPercentage.ToString();
            MinClearPercentage = existingCriteria.MinClearPercentage.ToString();
            MaxClearPercentage = existingCriteria.MaxClearPercentage.ToString();
            IsTeamPick = existingCriteria.IsTeamPick;
            RequireLocked = existingCriteria.RequireLocked;
            RequireSubLevel = existingCriteria.RequireSubLevel;
            RequireShareable = existingCriteria.RequireShareable;
            MaxHearts = existingCriteria.MaxHearts.ToString();
            MaxPlays = existingCriteria.MaxPlays.ToString();
            ExcludedCreators = existingCriteria.ExcludedCreators;
            ExcludedContributors = existingCriteria.ExcludedContributors;
            ExcludedObjectContributors = existingCriteria.ExcludedObjectContributors;
            ExcludeTeamPick = existingCriteria.ExcludeTeamPick;
            ExcludeLocked = existingCriteria.ExcludeLocked;
            ExcludeSubLevels = existingCriteria.ExcludeSubLevels;
            ExcludeShareable = existingCriteria.ExcludeShareable;

            AvailableYears.Add("Any");
            for (int y = 2008; y <= DateTime.Now.Year; y++) AvailableYears.Add(y.ToString());
            
            AvailableMonths.Add("Any");
            for (int m = 1; m <= 12; m++) AvailableMonths.Add(m.ToString("D2"));

            PublishedAfterYear = existingCriteria.PublishedAfter.HasValue ? existingCriteria.PublishedAfter.Value.Year.ToString() : "Any";
            PublishedAfterMonth = existingCriteria.PublishedAfter.HasValue ? existingCriteria.PublishedAfter.Value.Month.ToString("D2") : "Any";
            PublishedBeforeYear = existingCriteria.PublishedBefore.HasValue ? existingCriteria.PublishedBefore.Value.Year.ToString() : "Any";
            PublishedBeforeMonth = existingCriteria.PublishedBefore.HasValue ? existingCriteria.PublishedBefore.Value.Month.ToString("D2") : "Any";
            
            // Revert back to Author Only if community labels column isn't present
            LabelMatchMode = !_hasCommunityLabels ? 1 : existingCriteria.LabelMatchMode;

            PopulateTags(existingCriteria);

            ClearCommand = new RelayCommand(_ => ExecuteClear());
            ApplyCommand = new RelayCommand(_ => ExecuteApply(false));
            ApplyAndSearchCommand = new RelayCommand(_ => ExecuteApply(true));
            SavePresetCommand = new RelayCommand(_ => ExecuteSavePreset());
            LoadPresetCommand = new RelayCommand(_ => ExecuteLoadPreset());
        }

        private void PopulateTags(AdvancedSearchCriteria criteria)
        {
            var allTags = LabelParser.GetTags();
            var allFriendly = LabelParser.GetFriendlyNames();

            // Populate LBP2 & LBP3 Labels
            for (int i = 0; i < allTags.Count; i++)
            {
                string tag = allTags[i];
                string friendly = allFriendly[i];
                string category = LabelParser.GetLabelCategory(tag);
                bool isLbp2 = LabelParser.IsLbp2LabelByTag(tag);
                bool isChecked = criteria.RequiredLabels.Contains(tag);

                var vm = new SelectableTagViewModel
                {
                    DisplayName = friendly,
                    InternalTag = tag,
                    IsSelected = isChecked,
                    TiltAngle = GetDeterministicTilt(tag)
                };

                bool isExcludedChecked = criteria.ExcludedLabels.Contains(tag);
                var vmExcluded = new SelectableTagViewModel
                {
                    DisplayName = friendly,
                    InternalTag = tag,
                    IsSelected = isExcludedChecked,
                    TiltAngle = GetDeterministicTilt(tag)
                };

                if (isLbp2)
                {
                    if (category == "Experience") { Lbp2ExperienceLabels.Add(vm); ExcludedLbp2ExperienceLabels.Add(vmExcluded); }
                    else if (category == "Type") { Lbp2TypeLabels.Add(vm); ExcludedLbp2TypeLabels.Add(vmExcluded); }
                    else { Lbp2ContentLabels.Add(vm); ExcludedLbp2ContentLabels.Add(vmExcluded); }
                }
                else
                {
                    if (category == "Character") { Lbp3CharacterLabels.Add(vm); ExcludedLbp3CharacterLabels.Add(vmExcluded); }
                    else if (category == "Experience") { Lbp3ExperienceLabels.Add(vm); ExcludedLbp3ExperienceLabels.Add(vmExcluded); }
                    else if (category == "Type") { Lbp3TypeLabels.Add(vm); ExcludedLbp3TypeLabels.Add(vmExcluded); }
                    else { Lbp3ContentLabels.Add(vm); ExcludedLbp3ContentLabels.Add(vmExcluded); }
                }
            }

            // Populate LBP1 Tags
            foreach (var tagName in TagParser.GetNames())
            {
                Lbp1Tags.Add(new SelectableTagViewModel
                {
                    DisplayName = tagName,
                    InternalTag = tagName,
                    IsSelected = criteria.RequiredTags.Contains(tagName),
                    TiltAngle = GetDeterministicTilt(tagName)
                });
                
                ExcludedLbp1Tags.Add(new SelectableTagViewModel
                {
                    DisplayName = tagName,
                    InternalTag = tagName,
                    IsSelected = criteria.ExcludedTags.Contains(tagName),
                    TiltAngle = GetDeterministicTilt(tagName)
                });
            }
        }

        private void ExecuteClear()
        {
            MinHearts = "0";
            MinPlays = "0";
            MinHeartPercentage = "0";
            MinYayPercentage = "0";
            MinClearPercentage = "0";
            MaxClearPercentage = "100";
            IsTeamPick = false;
            RequireLocked = false;
            RequireSubLevel = false;
            RequireShareable = false;
            MaxHearts = "0";
            MaxPlays = "0";
            ExcludedCreators = "";
            ExcludedContributors = "";
            ExcludedObjectContributors = "";
            PublishedBeforeYear = "Any";
            PublishedBeforeMonth = "Any";
            PublishedAfterYear = "Any";
            PublishedAfterMonth = "Any";
            ExcludeTeamPick = false;
            ExcludeLocked = false;
            ExcludeSubLevels = false;
            ExcludeShareable = false;
            LabelMatchMode = !_hasCommunityLabels ? 1 : 0;

            var allCollections = new[] {  
                Lbp2ExperienceLabels, Lbp2TypeLabels, Lbp2ContentLabels, 
                Lbp3ExperienceLabels, Lbp3TypeLabels, Lbp3ContentLabels, Lbp3CharacterLabels, Lbp1Tags,
                ExcludedLbp2ExperienceLabels, ExcludedLbp2TypeLabels, ExcludedLbp2ContentLabels, 
                ExcludedLbp3ExperienceLabels, ExcludedLbp3TypeLabels, ExcludedLbp3ContentLabels, ExcludedLbp3CharacterLabels, ExcludedLbp1Tags
            };

            foreach (var collection in allCollections)
            {
                foreach (var item in collection) item.IsSelected = false;
            }
        }

        private AdvancedSearchCriteria BuildCriteria()
        {
            int.TryParse(MinHearts, out int hearts);
            int.TryParse(MinPlays, out int plays);
            int.TryParse(MinHeartPercentage, out int heartPct);
            int.TryParse(MinYayPercentage, out int yayPct);
            int.TryParse(MinClearPercentage, out int minClearPct);
            int.TryParse(MaxClearPercentage, out int maxClearPct);
            int.TryParse(MaxHearts, out int maxHearts);
            int.TryParse(MaxPlays, out int maxPlays);

            DateTime? after = null;
            if (PublishedAfterYear != "Any")
            {
                int y = int.Parse(PublishedAfterYear);
                int m = PublishedAfterMonth == "Any" ? 1 : int.Parse(PublishedAfterMonth);
                after = new DateTime(y, m, 1);
            }

            DateTime? before = null;
            if (PublishedBeforeYear != "Any")
            {
                int y = int.Parse(PublishedBeforeYear);
                int m = PublishedBeforeMonth == "Any" ? 12 : int.Parse(PublishedBeforeMonth);
                int d = DateTime.DaysInMonth(y, m);
                before = new DateTime(y, m, d, 23, 59, 59); // End of the month
            }

            var criteria = new AdvancedSearchCriteria
            {
                MinHearts = hearts,
                MinPlays = plays,
                MinHeartPercentage = heartPct,
                MinYayPercentage = yayPct,
                MinClearPercentage = minClearPct,
                MaxClearPercentage = maxClearPct,
                IsTeamPick = IsTeamPick,
                RequireLocked = RequireLocked,
                RequireSubLevel = RequireSubLevel,
                RequireShareable = RequireShareable,
                MaxHearts = maxHearts,
                MaxPlays = maxPlays,
                ExcludedCreators = ExcludedCreators,
                ExcludedContributors = ExcludedContributors,
                ExcludedObjectContributors = ExcludedObjectContributors,
                PublishedBefore = before,
                PublishedAfter = after,
                ExcludeTeamPick = ExcludeTeamPick,
                ExcludeLocked = ExcludeLocked,
                ExcludeSubLevels = ExcludeSubLevels,
                ExcludeShareable = ExcludeShareable,
                LabelMatchMode = LabelMatchMode
            };

            var allLabels = new[] { 
                Lbp2ExperienceLabels, Lbp2TypeLabels, Lbp2ContentLabels, 
                Lbp3ExperienceLabels, Lbp3TypeLabels, Lbp3ContentLabels, Lbp3CharacterLabels 
            };

            foreach (var collection in allLabels)
            {
                criteria.RequiredLabels.AddRange(collection.Where(x => x.IsSelected).Select(x => x.InternalTag));
            }

            criteria.RequiredTags.AddRange(Lbp1Tags.Where(x => x.IsSelected).Select(x => x.InternalTag));

            var allExcludedLabels = new[] { 
                ExcludedLbp2ExperienceLabels, ExcludedLbp2TypeLabels, ExcludedLbp2ContentLabels, 
                ExcludedLbp3ExperienceLabels, ExcludedLbp3TypeLabels, ExcludedLbp3ContentLabels, ExcludedLbp3CharacterLabels 
            };

            foreach (var collection in allExcludedLabels)
            {
                criteria.ExcludedLabels.AddRange(collection.Where(x => x.IsSelected).Select(x => x.InternalTag));
            }

            criteria.ExcludedTags.AddRange(ExcludedLbp1Tags.Where(x => x.IsSelected).Select(x => x.InternalTag));

            return criteria;
        }

        private void ExecuteApply(bool shouldSearch)
        {
            RequestClose?.Invoke(BuildCriteria(), shouldSearch);
        }

        private void ExecuteSavePreset()
        {
            string presetsDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LbpArchiveToolkit", "Presets");
            System.IO.Directory.CreateDirectory(presetsDir);

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "LBP Search Preset (*.lbppreset)|*.lbppreset",
                Title = "Save Search Preset",
                InitialDirectory = presetsDir
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var criteria = BuildCriteria();
                    var json = System.Text.Json.JsonSerializer.Serialize(criteria, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, TypeInfoResolver = LbpArchiveToolkit.Configuration.ConfigManager.ConfigJsonContext.Default });
                    System.IO.File.WriteAllText(dlg.FileName, json);
                    _viewService.Alert("Preset saved successfully.", "Success");
                }
                catch (Exception ex)
                {
                    _viewService.Alert($"Failed to save preset: {ex.Message}", "Error");
                }
            }
        }

        private void ExecuteLoadPreset()
        {
            string presetsDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LbpArchiveToolkit", "Presets");
            System.IO.Directory.CreateDirectory(presetsDir);

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "LBP Search Preset (*.lbppreset)|*.lbppreset",
                Title = "Load Search Preset",
                InitialDirectory = presetsDir
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var json = System.IO.File.ReadAllText(dlg.FileName);
                    var criteria = System.Text.Json.JsonSerializer.Deserialize<AdvancedSearchCriteria>(json, new System.Text.Json.JsonSerializerOptions { TypeInfoResolver = LbpArchiveToolkit.Configuration.ConfigManager.ConfigJsonContext.Default });
                    if (criteria != null)
                    {
                        ExecuteClear();

                        MinHearts = criteria.MinHearts.ToString();
                        MinPlays = criteria.MinPlays.ToString();
                        MinHeartPercentage = criteria.MinHeartPercentage.ToString();
                        MinYayPercentage = criteria.MinYayPercentage.ToString();
                        MinClearPercentage = criteria.MinClearPercentage.ToString();
                        MaxClearPercentage = criteria.MaxClearPercentage.ToString();
                        IsTeamPick = criteria.IsTeamPick;
                        RequireLocked = criteria.RequireLocked;
                        RequireSubLevel = criteria.RequireSubLevel;
                        RequireShareable = criteria.RequireShareable;
                        MaxHearts = criteria.MaxHearts.ToString();
                        MaxPlays = criteria.MaxPlays.ToString();
                        ExcludedCreators = criteria.ExcludedCreators;
                        ExcludedContributors = criteria.ExcludedContributors;
                        ExcludedObjectContributors = criteria.ExcludedObjectContributors;
                        PublishedAfterYear = criteria.PublishedAfter.HasValue ? criteria.PublishedAfter.Value.Year.ToString() : "Any";
                        PublishedAfterMonth = criteria.PublishedAfter.HasValue ? criteria.PublishedAfter.Value.Month.ToString("D2") : "Any";
                        PublishedBeforeYear = criteria.PublishedBefore.HasValue ? criteria.PublishedBefore.Value.Year.ToString() : "Any";
                        PublishedBeforeMonth = criteria.PublishedBefore.HasValue ? criteria.PublishedBefore.Value.Month.ToString("D2") : "Any";
                        ExcludeTeamPick = criteria.ExcludeTeamPick;
                        ExcludeLocked = criteria.ExcludeLocked;
                        ExcludeSubLevels = criteria.ExcludeSubLevels;
                        ExcludeShareable = criteria.ExcludeShareable;
                        LabelMatchMode = !_hasCommunityLabels ? 1 : criteria.LabelMatchMode;

                        PopulateTags(criteria);
                    }
                }
                catch (Exception ex)
                {
                    _viewService.Alert($"Failed to load preset: {ex.Message}", "Error");
                }
            }
        }

        /// <summary>
        /// Computes a stable, deterministic visual skew degree mapped between [-3.0, 3.0]
        /// using the FNV-1a non-cryptographic hashing algorithm.
        /// </summary>
        private double GetDeterministicTilt(string str)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in str)
                {
                    hash = (hash ^ c) * 16777619;
                }
                double val = (hash % 10000) / 10000.0;
                return val * 6.0 - 3.0; // Maps precisely to a [-3.0, 3.0] angle range
            }
        }
    }
}