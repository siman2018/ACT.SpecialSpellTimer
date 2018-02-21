using System;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using FFXIV.Framework.Common;
using FFXIV.Framework.Extensions;

namespace ACT.SpecialSpellTimer.RaidTimeline
{
    [Serializable]
    [XmlType(TypeName = "a")]
    public class TimelineActivityModel :
        TimelineBase
    {
        [XmlIgnore]
        public override TimelineElementTypes TimelineType => TimelineElementTypes.Activity;

        private TimeSpan time = TimeSpan.Zero;

        [XmlIgnore]
        public TimeSpan Time
        {
            get => this.time;
            set
            {
                if (this.SetProperty(ref this.time, value))
                {
                    this.RefreshProgress();
                }
            }
        }

        [XmlAttribute(AttributeName = "time")]
        public string TimeText
        {
            get => this.time.ToTLString();
            set => this.SetProperty(ref this.time, TimeSpanExtensions.FromTLString(value));
        }

        private string text = null;

        [XmlAttribute(AttributeName = "text")]
        public string Text
        {
            get => this.text;
            set => this.SetProperty(ref this.text, value);
        }

        private string syncKeyword = null;

        [XmlAttribute(AttributeName = "sync")]
        public string SyncKeyword
        {
            get => this.syncKeyword;
            set
            {
                if (this.SetProperty(ref this.syncKeyword, value))
                {
                    if (string.IsNullOrEmpty(this.syncKeyword))
                    {
                        this.SyncKeyword = null;
                    }
                    else
                    {
                        this.SynqRegex = new Regex(
                            this.syncKeyword,
                            RegexOptions.Compiled |
                            RegexOptions.ExplicitCapture |
                            RegexOptions.IgnoreCase);
                    }
                }
            }
        }

        private Regex syncRegex = null;

        [XmlIgnore]
        public Regex SynqRegex
        {
            get => this.syncRegex;
            private set => this.SetProperty(ref this.syncRegex, value);
        }

        private double? syncOffsetStart = null;
        private double? syncOffsetEnd = null;

        [XmlIgnore]
        public double? SyncOffsetStart
        {
            get => this.syncOffsetStart;
            set => this.SetProperty(ref this.syncOffsetStart, value);
        }

        [XmlAttribute(AttributeName = "sync-s")]
        public string SyncOffsetStartXML
        {
            get => this.SyncOffsetStart?.ToString();
            set => this.SyncOffsetStart = double.TryParse(value, out var v) ? v : (double?)null;
        }

        [XmlIgnore]
        public double? SyncOffsetEnd
        {
            get => this.syncOffsetEnd;
            set => this.SetProperty(ref this.syncOffsetEnd, value);
        }

        [XmlAttribute(AttributeName = "sync-e")]
        public string SyncOffsetEndXML
        {
            get => this.syncOffsetEnd?.ToString();
            set => this.syncOffsetEnd = double.TryParse(value, out var v) ? v : (double?)null;
        }

        private string gotoDestination = null;

        [XmlAttribute(AttributeName = "goto")]
        public string GoToDestination
        {
            get => this.gotoDestination;
            set
            {
                if (this.SetProperty(ref this.gotoDestination, value))
                {
                    this.RaisePropertyChanged(nameof(this.JumpDestination));
                }
            }
        }

        private string callTarget = null;

        [XmlAttribute(AttributeName = "call")]
        public string CallTarget
        {
            get => this.callTarget;
            set
            {
                if (this.SetProperty(ref this.callTarget, value))
                {
                    this.RaisePropertyChanged(nameof(this.JumpDestination));
                }
            }
        }

        private string notice = null;

        [XmlAttribute(AttributeName = "notice")]
        public string Notice
        {
            get => this.notice;
            set => this.SetProperty(ref this.notice, value);
        }

        private NoticeDevices? noticeDevice = null;

        [XmlIgnore]
        public NoticeDevices? NoticeDevice
        {
            get => this.noticeDevice;
            set => this.SetProperty(ref this.noticeDevice, value);
        }

        [XmlAttribute(AttributeName = "notice-d")]
        public string NoticeDeviceXML
        {
            get => this.NoticeDevice?.ToString();
            set => this.NoticeDevice = Enum.TryParse<NoticeDevices>(value, out var v) ? v : (NoticeDevices?)null;
        }

        private double? noticeOffset = null;

        [XmlIgnore]
        public double? NoticeOffset
        {
            get => this.noticeOffset;
            set => this.SetProperty(ref this.noticeOffset, value);
        }

        [XmlAttribute(AttributeName = "notice-o")]
        public string NoticeOffsetXML
        {
            get => this.NoticeOffset?.ToString();
            set => this.NoticeOffset = double.TryParse(value, out var v) ? v : (double?)null;
        }

        private string style = null;

        [XmlAttribute(AttributeName = "style")]
        public string Style
        {
            get => this.style;
            set => this.SetProperty(ref this.style, value);
        }

        public TimelineActivityModel Clone()
        {
            var clone = this.MemberwiseClone() as TimelineActivityModel;
            clone.id = Guid.NewGuid();
            return clone;
        }

        public override string ToString()
            => $"{this.TimeText} {this.Text}";

        #region 動作を制御するためのフィールド

        private static TimeSpan currentTime = TimeSpan.Zero;

        [XmlIgnore]
        public static TimeSpan CurrentTime
        {
            get => currentTime;
            set
            {
                if (currentTime != value)
                {
                    currentTime = value;
                }
            }
        }

        public void RefreshProgress()
        {
            var progressStartTime =
                WPFHelper.IsDesignMode ?
                15 :
                TimelineSettings.Instance.ShowProgressBarTime;

            var remain = (this.time - CurrentTime).TotalSeconds;
            if (remain < 0)
            {
                remain = 0;
            }

            this.RemainTime = remain;

            var progress = 0d;

            var before = this.time - CurrentTime;
            if (before.TotalSeconds <= progressStartTime)
            {
                progress = (progressStartTime - before.TotalSeconds) / progressStartTime;
                if (progress > 1)
                {
                    progress = 1;
                }
            }

            this.Progress = Math.Round(progress, 3);
        }

        private double remainTime = 0;

        [XmlIgnore]
        public double RemainTime
        {
            get => this.remainTime;
            set
            {
                if (this.SetProperty(ref this.remainTime, value))
                {
                    this.RemainTimeText = Math.Ceiling(this.remainTime).ToString("N0");
                }
            }
        }

        private string remainTimeText = string.Empty;

        [XmlIgnore]
        public string RemainTimeText
        {
            get => this.remainTimeText;
            set => this.SetProperty(ref this.remainTimeText, value);
        }

        private double progress = 0;

        [XmlIgnore]
        public double Progress
        {
            get => this.progress;
            set => this.SetProperty(ref this.progress, value);
        }

        private int seq = 0;

        [XmlIgnore]
        public int Seq
        {
            get => this.seq;
            set => this.SetProperty(ref this.seq, value);
        }

        private bool isActive = false;

        [XmlIgnore]
        public bool IsActive
        {
            get => this.isActive;
            set => this.SetProperty(ref this.isActive, value);
        }

        private bool isDone = false;

        [XmlIgnore]
        public bool IsDone
        {
            get => this.isDone;
            set => this.SetProperty(ref this.isDone, value);
        }

        private bool isNotified = false;

        [XmlIgnore]
        public bool IsNotified
        {
            get => this.isNotified;
            set => this.SetProperty(ref this.isNotified, value);
        }

        private bool isSynced = false;

        [XmlIgnore]
        public bool IsSynced
        {
            get => this.isSynced;
            set => this.SetProperty(ref this.isSynced, value);
        }

        private TimelineStyle styleModel = null;

        [XmlIgnore]
        public TimelineStyle StyleModel
        {
            get => this.styleModel;
            set => this.SetProperty(ref this.styleModel, value);
        }

        private double opacity = 1.0d;

        [XmlIgnore]
        public double Opacity
        {
            get => this.opacity;
            set => this.SetProperty(ref this.opacity, value);
        }

        private double scale = 1.0d;

        [XmlIgnore]
        public double Scale
        {
            get => this.scale;
            set => this.SetProperty(ref this.scale, value);
        }

        private bool isVisible = false;

        [XmlIgnore]
        public bool IsVisible
        {
            get => this.isVisible;
            set => this.SetProperty(ref this.isVisible, value);
        }

        [XmlIgnore]
        public string JumpDestination => (
            !string.IsNullOrEmpty(this.CallTarget) ?
            this.CallTarget :
            this.GoToDestination) ?? string.Empty;

        public void Init(
            int? seq = null)
        {
            if (seq.HasValue)
            {
                this.Seq = seq.Value;
            }

            this.IsActive = false;
            this.IsDone = false;
            this.IsNotified = false;
            this.IsSynced = false;
        }

        #endregion 動作を制御するためのフィールド
    }
}
