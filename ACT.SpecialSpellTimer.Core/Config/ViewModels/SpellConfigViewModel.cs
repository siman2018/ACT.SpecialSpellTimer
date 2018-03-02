using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using ACT.SpecialSpellTimer.Config.Models;
using ACT.SpecialSpellTimer.Config.Views;
using ACT.SpecialSpellTimer.Models;
using FFXIV.Framework.FFXIVHelper;
using Prism.Commands;
using Prism.Mvvm;

namespace ACT.SpecialSpellTimer.Config.ViewModels
{
    public partial class SpellConfigViewModel :
        BindableBase
    {
        public SpellConfigViewModel() : this(new Spell())
        {
        }

        public SpellConfigViewModel(
            Spell model)
        {
            this.Model = model;
            this.SetupPanelSource();
        }

        private Spell model;

        public Spell Model
        {
            get => this.model;
            set
            {
                if (this.SetProperty(ref this.model, value))
                {
                    try
                    {
                        this.isInitialize = true;

                        // ジョブ・ゾーン・前提条件のセレクタを初期化する
                        this.SetJobSelectors();
                        this.SetZoneSelectors();
                        PreconditionSelectors.Instance.SetModel(this.model);

                        // Designモード？（Visualタブがアクティブか？）
                        this.model.IsDesignMode = this.IsActiveVisualTab;
                        Task.Run(() => TableCompiler.Instance.CompileSpells());
                        this.SwitchDesignGrid();

                        // タグを初期化する
                        this.SetupTagsSource();
                    }
                    finally
                    {
                        this.isInitialize = false;
                    }

                    this.RaisePropertyChanged(nameof(this.IsJobFiltered));
                    this.RaisePropertyChanged(nameof(this.IsZoneFiltered));
                    this.RaisePropertyChanged(nameof(this.PreconditionSelectors));
                    this.RaisePropertyChanged(nameof(this.Model.MatchAdvancedConfig));
                    this.RaisePropertyChanged(nameof(this.Model.BeforeAdvancedConfig));
                    this.RaisePropertyChanged(nameof(this.Model.OverAdvancedConfig));
                    this.RaisePropertyChanged(nameof(this.Model.TimeupAdvancedConfig));
                }
            }
        }

        private bool isInitialize = false;

        private ICommand simulateMatchCommand;

        public ICommand SimulateMatchCommand =>
            this.simulateMatchCommand ?? (this.simulateMatchCommand = new DelegateCommand(() =>
            {
                this.Model.SimulateMatch();
            }));

        private bool isActiveVisualTab;

        public bool IsActiveVisualTab
        {
            get => this.isActiveVisualTab;
            set
            {
                if (this.SetProperty(ref this.isActiveVisualTab, value))
                {
                    this.Model.IsDesignMode = this.isActiveVisualTab;
                    Task.Run(() => TableCompiler.Instance.CompileSpells());
                    this.SwitchDesignGrid();
                }
            }
        }

        private CollectionViewSource panelSource = new CollectionViewSource()
        {
            Source = SpellPanelTable.Instance.Table,
            IsLiveFilteringRequested = true,
            IsLiveSortingRequested = true,
        };

        public ICollectionView Panels => this.panelSource.View;

        private void SetupPanelSource()
        {
            this.panelSource.SortDescriptions.AddRange(new[]
            {
                new SortDescription()
                {
                    PropertyName = nameof(SpellPanel.SortPriority),
                    Direction = ListSortDirection.Ascending,
                },
                new SortDescription()
                {
                    PropertyName = nameof(SpellPanel.PanelName),
                    Direction = ListSortDirection.Ascending,
                }
            });
        }

        private void SwitchDesignGrid()
        {
            var showGrid =
                TableCompiler.Instance.SpellList.Any(x => x.IsDesignMode) ||
                TableCompiler.Instance.TickerList.Any(x => x.IsDesignMode);

            Settings.Default.VisibleDesignGrid = showGrid;
        }

        #region Job filter

        public bool IsJobFiltered => !string.IsNullOrEmpty(this.Model?.JobFilter);

        private static List<JobSelector> jobSelectors;

        public List<JobSelector> JobSelectors => jobSelectors;

        private void SetJobSelectors()
        {
            if (jobSelectors == null)
            {
                jobSelectors = new List<JobSelector>();

                foreach (var job in
                    from x in Jobs.List
                    where
                    x.ID != JobIDs.Unknown &&
                    x.ID != JobIDs.ADV
                    orderby
                    x.Role.ToSortOrder(),
                    x.ID
                    select
                    x)
                {
                    jobSelectors.Add(new JobSelector(job));
                }
            }

            var jobFilters = this.Model.JobFilter?.Split(',');
            foreach (var selector in this.JobSelectors)
            {
                if (jobFilters != null)
                {
                    selector.IsSelected = jobFilters.Contains(((int)selector.Job.ID).ToString());
                }

                selector.SelectedChangedDelegate = this.JobFilterChanged;
            }

            this.RaisePropertyChanged(nameof(this.JobSelectors));
        }

        private void JobFilterChanged()
        {
            if (!this.isInitialize)
            {
                this.Model.JobFilter = string.Join(",",
                    this.JobSelectors
                        .Where(x => x.IsSelected)
                        .Select(x => ((int)x.Job.ID).ToString())
                        .ToArray());

                this.RaisePropertyChanged(nameof(this.IsJobFiltered));
                Task.Run(() => TableCompiler.Instance.CompileSpells());
            }
        }

        private ICommand clearJobFilterCommand;

        public ICommand ClearJobFilterCommand =>
            this.clearJobFilterCommand ?? (this.clearJobFilterCommand = new DelegateCommand(() =>
            {
                try
                {
                    this.isInitialize = true;
                    foreach (var selector in this.JobSelectors)
                    {
                        selector.IsSelected = false;
                    }

                    this.Model.JobFilter = string.Empty;
                    this.RaisePropertyChanged(nameof(this.IsJobFiltered));
                    Task.Run(() => TableCompiler.Instance.CompileSpells());
                }
                finally
                {
                    this.isInitialize = false;
                }
            }));

        #endregion Job filter

        #region Zone filter

        public bool IsZoneFiltered => !string.IsNullOrEmpty(this.Model?.ZoneFilter);

        private static List<ZoneSelector> zoneSelectors;

        public List<ZoneSelector> ZoneSelectors => zoneSelectors;

        private void SetZoneSelectors()
        {
            if (zoneSelectors == null ||
                zoneSelectors.Count <= 0)
            {
                zoneSelectors = new List<ZoneSelector>();

                foreach (var zone in
                    from x in FFXIVPlugin.Instance?.ZoneList
                    orderby
                    x.IsAddedByUser ? 0 : 1,
                    x.Rank,
                    x.ID descending
                    select
                    x)
                {
                    var selector = new ZoneSelector(
                        zone.ID.ToString(),
                        zone.Name);

                    zoneSelectors.Add(selector);
                }
            }

            var zoneFilters = this.Model.ZoneFilter?.Split(',');
            foreach (var selector in this.ZoneSelectors)
            {
                if (zoneFilters != null)
                {
                    selector.IsSelected = zoneFilters.Contains(selector.ID);
                }

                selector.SelectedChangedDelegate = this.ZoneFilterChanged;
            }

            this.RaisePropertyChanged(nameof(this.ZoneSelectors));
        }

        private void ZoneFilterChanged()
        {
            if (!this.isInitialize)
            {
                this.Model.ZoneFilter = string.Join(",",
                    this.ZoneSelectors
                        .Where(x => x.IsSelected)
                        .Select(x => x.ID)
                        .ToArray());

                this.RaisePropertyChanged(nameof(this.IsZoneFiltered));
                Task.Run(() => TableCompiler.Instance.CompileSpells());
            }
        }

        private ICommand clearZoneFilterCommand;

        public ICommand ClearZoneFilterCommand =>
            this.clearZoneFilterCommand ?? (this.clearZoneFilterCommand = new DelegateCommand(() =>
            {
                try
                {
                    this.isInitialize = true;
                    foreach (var selector in this.ZoneSelectors)
                    {
                        selector.IsSelected = false;
                    }

                    this.Model.ZoneFilter = string.Empty;
                    this.RaisePropertyChanged(nameof(this.IsZoneFiltered));
                    Task.Run(() => TableCompiler.Instance.CompileSpells());
                }
                finally
                {
                    this.isInitialize = false;
                }
            }));

        #endregion Zone filter

        #region Precondition selector

        public PreconditionSelectors PreconditionSelectors => PreconditionSelectors.Instance;

        private ICommand clearPreconditionsCommand;

        public ICommand ClearPreconditionsCommand =>
            this.clearPreconditionsCommand ?? (this.clearPreconditionsCommand = new DelegateCommand(() =>
            {
                PreconditionSelectors.Instance.ClearSelect();
            }));

        #endregion Precondition selector

        #region Tags

        private ICommand addTagsCommand;

        public ICommand AddTagsCommand =>
            this.addTagsCommand ?? (this.addTagsCommand = new DelegateCommand<Guid?>(targetItemID =>
            {
                if (!targetItemID.HasValue)
                {
                    return;
                }

                new TagView()
                {
                    TargetItemID = targetItemID.Value,
                }.Show();
            }));

        public ICollectionView Tags => this.TagsSource?.View;

        private CollectionViewSource TagsSource;

        private void SetupTagsSource()
        {
            this.TagsSource = new CollectionViewSource()
            {
                Source = TagTable.Instance.ItemTags,
                IsLiveFilteringRequested = true,
                IsLiveSortingRequested = true,
            };

            this.TagsSource.Filter += (x, y) =>
                y.Accepted =
                    (y.Item as ItemTags).ItemID == this.Model.Guid;

            this.TagsSource.SortDescriptions.AddRange(new[]
            {
                new SortDescription()
                {
                    PropertyName = "Tag.SortPriority",
                    Direction = ListSortDirection.Ascending
                },
                new SortDescription()
                {
                    PropertyName = "Tag.Name",
                    Direction = ListSortDirection.Ascending
                },
            });

            this.RaisePropertyChanged(nameof(this.Tags));
        }

        #endregion Tags
    }
}
