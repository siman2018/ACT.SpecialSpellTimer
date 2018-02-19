using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Xml.Serialization;
using ACT.SpecialSpellTimer.Config.Views;
using FFXIV.Framework.Common;
using FFXIV.Framework.Globalization;
using Prism.Commands;
using Prism.Mvvm;

namespace ACT.SpecialSpellTimer.RaidTimeline
{
    [XmlType(TypeName = "timeline")]
    [XmlInclude(typeof(TimelineDefaultModel))]
    [XmlInclude(typeof(TimelineActivityModel))]
    [XmlInclude(typeof(TimelineTriggerModel))]
    [XmlInclude(typeof(TimelineSubroutineModel))]
    public partial class TimelineModel :
        BindableBase
    {
        private string name = string.Empty;

        [XmlElement(ElementName = "name")]
        public string Name
        {
            get => this.name;
            set => this.SetProperty(ref this.name, value);
        }

        private string zone = string.Empty;

        [XmlElement(ElementName = "zone")]
        public string Zone
        {
            get => this.zone;
            set => this.SetProperty(ref this.zone, value);
        }

        private Locales locale = Locales.JA;

        [XmlElement(ElementName = "locale")]
        public Locales Locale
        {
            get => this.locale;
            set => this.SetProperty(ref this.locale, value);
        }

        [XmlIgnore]
        public string LocaleText => this.Locale.ToText();

        private string file = string.Empty;

        [XmlIgnore]
        public string File
        {
            get => this.file;
            private set
            {
                if (this.SetProperty(ref this.file, value))
                {
                    this.RaisePropertyChanged(nameof(this.FileName));
                }
            }
        }

        private bool isActive = false;

        [XmlIgnore]
        public bool IsActive
        {
            get => this.isActive;
            set
            {
                if (this.SetProperty(ref this.isActive, value))
                {
                    Task.Run(() =>
                    {
                        if (this.isActive)
                        {
                            if (TimelineController.CurrentController != null)
                            {
                                TimelineController.CurrentController.Model.IsActive = false;
                            }

                            this.Controller.Load();
                        }
                        else
                        {
                            this.Controller.Unload();
                        }
                    });
                }
            }
        }

        [XmlIgnore]
        public string FileName => Path.GetFileName(this.File);

        private List<TimelineBase> elements = new List<TimelineBase>();

        [XmlIgnore]
        public IReadOnlyList<TimelineBase> Elements => this.elements;

        [XmlElement(ElementName = "default")]
        public TimelineDefaultModel[] Defaults
        {
            get => this.Elements.Where(x => x.TimelineType == TimelineElementTypes.Default).Cast<TimelineDefaultModel>().ToArray();
            set => this.AddRange(value);
        }

        [XmlElement(ElementName = "a")]
        public TimelineActivityModel[] Activities
        {
            get => this.Elements.Where(x => x.TimelineType == TimelineElementTypes.Activity).Cast<TimelineActivityModel>().ToArray();
            set => this.AddRange(value);
        }

        [XmlElement(ElementName = "t")]
        public TimelineTriggerModel[] Triggers
        {
            get => this.Elements.Where(x => x.TimelineType == TimelineElementTypes.Trigger).Cast<TimelineTriggerModel>().ToArray();
            set => this.AddRange(value);
        }

        [XmlElement(ElementName = "s")]
        public TimelineSubroutineModel[] Subroutines
        {
            get => this.Elements.Where(x => x.TimelineType == TimelineElementTypes.Subroutine).Cast<TimelineSubroutineModel>().ToArray();
            set => this.AddRange(value);
        }

        private TimelineController controller;

        /// <summary>
        /// タイムラインの実行を制御するオブジェクト
        /// </summary>
        [XmlIgnore]
        public TimelineController Controller =>
            this.controller = (this.controller ?? new TimelineController(this));

        #region Methods

        public void Add(TimelineBase timeline)
        {
            timeline.Parent = this;
            this.elements.Add(timeline);
        }

        public void AddRange(IEnumerable<TimelineBase> timelines)
        {
            if (timelines != null)
            {
                foreach (var tl in timelines)
                {
                    this.Add(tl);
                }
            }
        }

        public static TimelineModel Load(
            string file)
        {
            if (!System.IO.File.Exists(file))
            {
                return null;
            }

            var tl = default(TimelineModel);

            using (var sr = new StreamReader(file, new UTF8Encoding(false)))
            {
                if (sr.BaseStream.Length > 0)
                {
                    var xs = new XmlSerializer(typeof(TimelineModel));
                    var data = xs.Deserialize(sr) as TimelineModel;
                    if (data != null)
                    {
                        tl = data;
                        tl.File = file;
                    }
                }
            }
#if false
            if (tl == null)
            {
                return tl;
            }

            void setSubroutinesParent(
                TimelineSubroutineModel sub)
            {
                foreach (var state in sub.Statements
                    .Where(x =>
                        x.TimelineType == TimelineElementTypes.Default ||
                        x.TimelineType == TimelineElementTypes.Activity ||
                        x.TimelineType == TimelineElementTypes.Trigger))
                {
                    state.Parent = sub;
                }

                foreach (var subsub in sub.Statements
                    .Where(x =>
                        x.TimelineType == TimelineElementTypes.Subroutine))
                {
                    subsub.Parent = sub;
                    setSubroutinesParent(subsub as TimelineSubroutineModel);
                }
            }

            foreach (var el in tl.Elements)
            {
                switch (el.TimelineType)
                {
                    case TimelineElementTypes.Default:
                    case TimelineElementTypes.Activity:
                    case TimelineElementTypes.Trigger:
                        el.Parent = tl;
                        break;

                    case TimelineElementTypes.Subroutine:
                        el.Parent = tl;
                        setSubroutinesParent(el as TimelineSubroutineModel);
                        break;
                }
            }
#endif
            return tl;
        }

        public void Save(
            string file)
        {
            lock (this)
            {
                FileHelper.CreateDirectory(file);

                var ns = new XmlSerializerNamespaces();
                ns.Add(string.Empty, string.Empty);

                var sb = new StringBuilder();
                using (var sw = new StringWriter(sb))
                {
                    var xs = new XmlSerializer(this.GetType());
                    xs.Serialize(sw, this, ns);
                }

                sb.Replace("utf-16", "utf-8");

                System.IO.File.WriteAllText(
                    file,
                    sb.ToString() + Environment.NewLine,
                    new UTF8Encoding(false));
            }
        }

        #endregion Methods

        #region Commands

        private ICommand editCommand;

        public ICommand EditCommand =>
            this.editCommand ?? (this.editCommand = new DelegateCommand(() =>
            {
                if (System.IO.File.Exists(this.File))
                {
                    Process.Start(this.File);
                }
            }));

        private ICommand reloadCommand;

        public ICommand ReloadCommand =>
            this.reloadCommand ?? (this.reloadCommand = new DelegateCommand(async () =>
            {
                if (!System.IO.File.Exists(this.File))
                {
                    return;
                }

                try
                {
                    var isStanby = this.IsActive;

                    var tl = default(TimelineModel);
                    await Task.Run(() =>
                    {
                        if (isStanby)
                        {
                            this.Controller.Unload();
                        }

                        tl = TimelineModel.Load(this.File);
                    });

                    if (tl == null)
                    {
                        return;
                    }

                    this.Name = tl.Name;
                    this.Zone = tl.Zone;
                    this.Locale = tl.Locale;
                    this.File = tl.File;

                    this.elements.Clear();
                    this.AddRange(tl.Elements);

                    if (isStanby)
                    {
                        this.Controller.Load();
                    }

                    ModernMessageBox.ShowDialog(
                        "Timeline reloaded.",
                        "Timeline Manager");
                }
                catch (Exception ex)
                {
                    ModernMessageBox.ShowDialog(
                        "Timeline reload error !",
                        "Timeline Manager",
                        MessageBoxButton.OK,
                        ex);
                }
            }));

        #endregion Commands
    }
}