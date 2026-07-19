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

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }

    public class AdvancedSearchWindowViewModel : ViewModelBase
    {
        private string _minHearts = "0";
        public string MinHearts { get => _minHearts; set => SetProperty(ref _minHearts, value); }

        private string _minPlays = "0";
        public string MinPlays { get => _minPlays; set => SetProperty(ref _minPlays, value); }

        private bool _isTeamPick;
        public bool IsTeamPick { get => _isTeamPick; set => SetProperty(ref _isTeamPick, value); }

        // Categorized Collections for Data Binding
        public ObservableCollection<SelectableTagViewModel> Lbp2ExperienceLabels { get; } = new();
        public ObservableCollection<SelectableTagViewModel> Lbp2TypeLabels { get; } = new();
        public ObservableCollection<SelectableTagViewModel> Lbp2ContentLabels { get; } = new();
        
        public ObservableCollection<SelectableTagViewModel> Lbp3ExperienceLabels { get; } = new();
        public ObservableCollection<SelectableTagViewModel> Lbp3TypeLabels { get; } = new();
        public ObservableCollection<SelectableTagViewModel> Lbp3ContentLabels { get; } = new();
        public ObservableCollection<SelectableTagViewModel> Lbp3CharacterLabels { get; } = new();
        
        public ObservableCollection<SelectableTagViewModel> Lbp1Tags { get; } = new();

        public ICommand ClearCommand { get; }
        public ICommand ApplyCommand { get; }
        public ICommand ApplyAndSearchCommand { get; }

        // Action to pass the updated criteria back to the Window
        public Action<AdvancedSearchCriteria, bool>? RequestClose { get; set; }

        public AdvancedSearchWindowViewModel(AdvancedSearchCriteria existingCriteria)
        {
            MinHearts = existingCriteria.MinHearts.ToString();
            MinPlays = existingCriteria.MinPlays.ToString();
            IsTeamPick = existingCriteria.IsTeamPick;

            PopulateTags(existingCriteria);

            ClearCommand = new RelayCommand(_ => ExecuteClear());
            ApplyCommand = new RelayCommand(_ => ExecuteApply(false));
            ApplyAndSearchCommand = new RelayCommand(_ => ExecuteApply(true));
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

                if (isLbp2)
                {
                    if (category == "Experience") Lbp2ExperienceLabels.Add(vm);
                    else if (category == "Type") Lbp2TypeLabels.Add(vm);
                    else Lbp2ContentLabels.Add(vm);
                }
                else
                {
                    if (category == "Character") Lbp3CharacterLabels.Add(vm);
                    else if (category == "Experience") Lbp3ExperienceLabels.Add(vm);
                    else if (category == "Type") Lbp3TypeLabels.Add(vm);
                    else Lbp3ContentLabels.Add(vm);
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
            }
        }

        private void ExecuteClear()
        {
            MinHearts = "0";
            MinPlays = "0";
            IsTeamPick = false;

            var allCollections = new[] { 
                Lbp2ExperienceLabels, Lbp2TypeLabels, Lbp2ContentLabels, 
                Lbp3ExperienceLabels, Lbp3TypeLabels, Lbp3ContentLabels, Lbp3CharacterLabels, Lbp1Tags 
            };

            foreach (var collection in allCollections)
            {
                foreach (var item in collection) item.IsSelected = false;
            }
        }

        private void ExecuteApply(bool shouldSearch)
        {
            int.TryParse(MinHearts, out int hearts);
            int.TryParse(MinPlays, out int plays);

            var criteria = new AdvancedSearchCriteria
            {
                MinHearts = hearts,
                MinPlays = plays,
                IsTeamPick = IsTeamPick
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

            RequestClose?.Invoke(criteria, shouldSearch);
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