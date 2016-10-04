﻿namespace ACT.SpecialSpellTimer
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;

    using ACT.SpecialSpellTimer.Properties;
    using ACT.SpecialSpellTimer.Utility;
    using Advanced_Combat_Tracker;

    /// <summary>
    /// コンバットアナライザ
    /// </summary>
    public class CombatAnalyzer
    {
        private static readonly string[] CastKeywords = new string[] { "を唱えた。", "の構え。" };
        private static readonly string[] ActionKeywords = new string[] { "「", "」" };
        private static readonly string[] HPRateKeywords = new string[] { "HP at" };
        private static readonly string[] AddedKeywords = new string[] { "Added new combatant" };

        private static readonly Regex CastRegex = new Regex(
            @"\[.+?\] 00:2[89a]..:(?<actor>.+?)は「(?<skill>.+?)」(を唱えた。|の構え。)$",
            RegexOptions.Compiled | RegexOptions.ExplicitCapture);

        private static readonly Regex ActionRegex = new Regex(
            @"\[.+?\] 00:2[89a]..:(?<actor>.+?)の「(?<skill>.+?)」$",
            RegexOptions.Compiled | RegexOptions.ExplicitCapture);

        private static readonly Regex HPRateRegex = new Regex(
            @"\[.+?\] ..:(?<actor>.+?) HP at (?<hprate>\d+?)%",
            RegexOptions.Compiled | RegexOptions.ExplicitCapture);

        private static readonly Regex AddedRegex = new Regex(
            @"\[.+?\] 03:Added new combatant (?<actor>.+)\.  ",
            RegexOptions.Compiled | RegexOptions.ExplicitCapture);

        /// <summary>
        /// シングルトンInstance
        /// </summary>
        private static CombatAnalyzer instance;

        /// <summary>
        /// シングルトンInstance
        /// </summary>
        public static CombatAnalyzer Default
        {
            get
            {
                if (instance == null)
                {
                    instance = new CombatAnalyzer();
                }

                return instance;
            }
        }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public CombatAnalyzer()
        {
            this.CurrentCombatLogList = new List<CombatLog>();
            this.ActorHPRate = new Dictionary<string, decimal>();
        }

        /// <summary>
        /// 戦闘ログのリスト
        /// </summary>
        public List<CombatLog> CurrentCombatLogList { get; private set; }

        /// <summary>
        /// アクターのHP率
        /// </summary>
        private Dictionary<string, decimal> ActorHPRate { get; set; }

        /// <summary>
        /// ログのID
        /// </summary>
        private long id;

        /// <summary>
        /// ログ一時バッファ
        /// </summary>
        private readonly ConcurrentQueue<LogLineEventArgs> logInfoQueue = new ConcurrentQueue<LogLineEventArgs>();

        /// <summary>
        /// ログ格納スレッド
        /// </summary>
        private Thread storeLogThread;

        /// <summary>
        /// スレッド稼働中？
        /// </summary>
        private bool isRunning;

        /// <summary>
        /// 分析を開始する
        /// </summary>
        public void Initialize()
        {
            this.ClearLogBuffer();

            if (Settings.Default.CombatLogEnabled)
            {
                this.StartPoller();
            }

            ActGlobals.oFormActMain.OnLogLineRead += this.oFormActMain_OnLogLineRead;
            Logger.Write("start combat analyze.");
        }

        /// <summary>
        /// 分析を停止する
        /// </summary>
        public void Denitialize()
        {
            this.EndPoller();

            ActGlobals.oFormActMain.OnLogLineRead -= this.oFormActMain_OnLogLineRead;
            this.CurrentCombatLogList.Clear();
            Logger.Write("end combat analyze.");
        }

        /// <summary>
        /// ログのポーリングを開始する
        /// </summary>
        public void StartPoller()
        {
            this.ClearLogInfoQueue();

            this.storeLogThread = new Thread(() =>
            {
                Thread.Sleep(TimeSpan.FromSeconds(1));

                try
                {
                    Logger.Write("start log poll for analyze.");
                    this.StoreLogPoller();
                }
                catch (ThreadAbortException)
                {
                    this.isRunning = false;
                }
                catch (Exception ex)
                {
                    Logger.Write(
                        "Catch exception at Store log for analyze.\n" +
                        ex.ToString());
                }
                finally
                {
                    Logger.Write("end log poll for analyze.");
                }
            });

            this.isRunning = true;
            this.storeLogThread.Start();
        }

        /// <summary>
        /// ログのポーリングを終了する
        /// </summary>
        public void EndPoller()
        {
            if (this.storeLogThread != null)
            {
                if (this.storeLogThread.IsAlive)
                {
                    this.storeLogThread.Abort();
                }

                this.storeLogThread = null;
            }

            this.ClearLogInfoQueue();
        }

        /// <summary>
        /// ログバッファをクリアする
        /// </summary>
        public void ClearLogBuffer()
        {
            lock (this.CurrentCombatLogList)
            {
                this.ClearLogInfoQueue();
                this.CurrentCombatLogList.Clear();
                this.ActorHPRate.Clear();
            }
        }

        /// <summary>
        /// ログを分析する
        /// </summary>
        public void AnalyzeLog()
        {
            this.AnalyzeLog(this.CurrentCombatLogList);
        }

        /// <summary>
        /// ログを分析する
        /// </summary>
        /// <param name="logList">ログのリスト</param>
        public void AnalyzeLog(
            List<CombatLog> logList)
        {
            CombatLog[] logs;

            lock (this.CurrentCombatLogList)
            {
                if (logList == null ||
                    logList.Count < 1)
                {
                    return;
                }

                logs = logList.OrderBy(x => x.ID).ToArray();
            }

            var previouseAction = new Dictionary<string, DateTime>();

            var i = 0L;
            foreach (var log in logs)
            {
                // 10回に1回ちょっとだけスリープする
                if ((i % 10) == 0)
                {
                    Thread.Sleep(1);
                }

                if (log.LogType == CombatLogType.AnalyzeStart ||
                    log.LogType == CombatLogType.AnalyzeEnd ||
                    log.LogType == CombatLogType.HPRate)
                {
                    continue;
                }

                var key = log.LogType.ToString() + "-" + log.Actor + "-" + log.Action;

                // 直前の同じログを探す
                if (previouseAction.ContainsKey(key))
                {
                    log.Span = (log.TimeStamp - previouseAction[key]).TotalSeconds;
                }

                // 記録しておく
                previouseAction[key] = log.TimeStamp;

                i++;
            }
        }

        /// <summary>
        /// ログキューを消去する
        /// </summary>
        private void ClearLogInfoQueue()
        {
            while(!this.logInfoQueue.IsEmpty)
            {
                LogLineEventArgs l;
                this.logInfoQueue.TryDequeue(out l);
            }
        }

        /// <summary>
        /// ログを格納するスレッド
        /// </summary>
        private void StoreLogPoller()
        {
            while (this.isRunning)
            {
                // プレイヤ情報とパーティリストを取得する
                var player = FF14PluginHelper.GetPlayer();
                var ptlist = LogBuffer.PartyList;

                while (!this.logInfoQueue.IsEmpty)
                {
                    Thread.Sleep(0);

                    LogLineEventArgs log = null;
                    this.logInfoQueue.TryDequeue(out log);

                    if (log == null)
                    {
                        continue;
                    }

                    // ログにペットが含まれている？
                    if (log.logLine.Contains("・エギ") ||
                        log.logLine.Contains("フェアリー・") ||
                        log.logLine.Contains("カーバンクル・"))
                    {
                        continue;
                    }

                    if (player != null &&
                        ptlist != null)
                    {
                        // ログにプレイヤ名が含まれている？
                        if (log.logLine.Contains(player.Name))
                        {
                            continue;
                        }

                        // ログにパーティメンバ名が含まれている？
                        foreach (var name in ptlist)
                        {
                            if (log.logLine.Contains(name))
                            {
                                break;
                            }
                        }
                    }

                    // キャストのキーワードが含まれている？
                    foreach (var keyword in CastKeywords)
                    {
                        if (log.logLine.Contains(keyword))
                        {
                            this.StoreCastLog(log);
                            break;
                        }
                    }

                    // アクションのキーワードが含まれている？
                    foreach (var keyword in ActionKeywords)
                    {
                        if (log.logLine.Contains(keyword))
                        {
                            this.StoreActionLog(log);
                            break;
                        }
                    }

                    // 残HP率のキーワードが含まれている？
                    foreach (var keyword in HPRateKeywords)
                    {
                        if (log.logLine.Contains(keyword))
                        {
                            this.StoreHPRateLog(log);
                            break;
                        }
                    }

                    // Addedのキーワードが含まれている？
                    foreach (var keyword in AddedKeywords)
                    {
                        if (log.logLine.Contains(keyword))
                        {
                            this.StoreAddedLog(log);
                            break;
                        }
                    }
                }

                Thread.Sleep((int)Settings.Default.LogPollSleepInterval);
            }
        }

        /// <summary>
        /// ログを1行読取った
        /// </summary>
        /// <param name="isImport">Importか？</param>
        /// <param name="logInfo">ログ情報</param>
        private void oFormActMain_OnLogLineRead(
            bool isImport,
            LogLineEventArgs logInfo)
        {
            try
            {
                if (!Settings.Default.CombatLogEnabled)
                {
                    return;
                }

                // キューに貯める
                this.logInfoQueue.Enqueue(logInfo);
            }
            catch (Exception ex)
            {
                Logger.Write(
                    "catch exception at Combat Analyzer OnLogLineRead.\n" +
                    ex.ToString());
            }

#if false
            if (this.CurrentCombatLogList == null)
            {
                return;
            }

            // ログにペットが含まれている？
            if (logInfo.logLine.Contains("・エギ") ||
                logInfo.logLine.Contains("フェアリー・") ||
                logInfo.logLine.Contains("カーバンクル・"))
            {
                return;
            }

            // インポートログではない？
            if (!isImport)
            {
                // プレイヤ情報とパーティリストを取得する
                var player = FF14PluginHelper.GetPlayer();
                var ptlist = LogBuffer.PartyList;

                if (player == null ||
                    ptlist == null)
                {
                    return;
                }

                // ログにプレイヤ名が含まれている？
                if (logInfo.logLine.Contains(player.Name))
                {
                    return;
                }

                // ログにパーティメンバ名が含まれている？
                foreach (var name in ptlist)
                {
                    if (logInfo.logLine.Contains(name))
                    {
                        return;
                    }
                }
            }

            // キャストのキーワードが含まれている？
            foreach (var keyword in CastKeywords)
            {
                if (logInfo.logLine.Contains(keyword))
                {
                    this.StoreCastLog(logInfo);
                    return;
                }
            }

            // アクションのキーワードが含まれている？
            foreach (var keyword in ActionKeywords)
            {
                if (logInfo.logLine.Contains(keyword))
                {
                    this.StoreActionLog(logInfo);
                    return;
                }
            }

            // 残HP率のキーワードが含まれている？
            foreach (var keyword in HPRateKeywords)
            {
                if (logInfo.logLine.Contains(keyword))
                {
                    this.StoreHPRateLog(logInfo);
                    return;
                }
            }

            // Addedのキーワードが含まれている？
            foreach (var keyword in AddedKeywords)
            {
                if (logInfo.logLine.Contains(keyword))
                {
                    this.StoreAddedLog(logInfo);
                    return;
                }
            }
#endif
        }

        /// <summary>
        /// ログを格納する
        /// </summary>
        /// <param name="log">ログ</param>
        private void StoreLog(
            CombatLog log)
        {
            switch (log.LogType)
            {
                case CombatLogType.AnalyzeStart:
                    log.LogTypeName = "開始";
                    break;

                case CombatLogType.AnalyzeEnd:
                    log.LogTypeName = "終了";
                    break;

                case CombatLogType.CastStart:
                    log.LogTypeName = "準備動作";
                    break;

                case CombatLogType.Action:
                    log.LogTypeName = "アクション";
                    break;

                case CombatLogType.Added:
                    log.LogTypeName = "Added";
                    break;

                case CombatLogType.HPRate:
                    log.LogTypeName = "残HP率";
                    break;
            }

            lock (this.CurrentCombatLogList)
            {
                // IDを発番する
                log.ID = this.id;
                this.id++;

                // バッファサイズを超えた？
                if (this.CurrentCombatLogList.Count >
                    (Settings.Default.CombatLogBufferSize * 1.1m))
                {
                    // オーバー分を消去する
                    var over = (int)(this.CurrentCombatLogList.Count - Settings.Default.CombatLogBufferSize);
                    this.CurrentCombatLogList.RemoveRange(0, over);
                }

                // 経過秒を求める
                if (this.CurrentCombatLogList.Count > 0)
                {
                    log.TimeStampElapted =
                        (log.TimeStamp - this.CurrentCombatLogList.First().TimeStamp).TotalSeconds;
                }
                else
                {
                    log.TimeStampElapted = 0;
                }

                // アクター別の残HP率をセットする
                if (this.ActorHPRate.ContainsKey(log.Actor))
                {
                    log.HPRate = this.ActorHPRate[log.Actor];
                }

                this.CurrentCombatLogList.Add(log);
            }
        }

        /// <summary>
        /// キャストログを格納する
        /// </summary>
        /// <param name="logInfo">ログ情報</param>
        private void StoreCastLog(
            LogLineEventArgs logInfo)
        {
            var match = CastRegex.Match(logInfo.logLine);
            if (!match.Success)
            {
                return;
            }

            var log = new CombatLog()
            {
                TimeStamp = logInfo.detectedTime,
                Raw = logInfo.logLine,
                Actor = match.Groups["actor"].ToString(),
                Action = match.Groups["skill"].ToString() + " の準備動作",
                LogType = CombatLogType.CastStart
            };

            this.StoreLog(log);
        }

        /// <summary>
        /// アクションログを格納する
        /// </summary>
        /// <param name="logInfo">ログ情報</param>
        private void StoreActionLog(
            LogLineEventArgs logInfo)
        {
            var match = ActionRegex.Match(logInfo.logLine);
            if (!match.Success)
            {
                return;
            }

            var log = new CombatLog()
            {
                TimeStamp = logInfo.detectedTime,
                Raw = logInfo.logLine,
                Actor = match.Groups["actor"].ToString(),
                Action = match.Groups["skill"].ToString() + " の発動",
                LogType = CombatLogType.Action
            };

            this.StoreLog(log);
        }

        /// <summary>
        /// HP率のログを格納する
        /// </summary>
        /// <param name="logInfo">ログ情報</param>
        private void StoreHPRateLog(
            LogLineEventArgs logInfo)
        {
            var match = HPRateRegex.Match(logInfo.logLine);
            if (!match.Success)
            {
                return;
            }

            var actor = match.Groups["actor"].ToString();

            if (!string.IsNullOrWhiteSpace(actor))
            {
                decimal hprate;
                if (!decimal.TryParse(match.Groups["hprate"].ToString(), out hprate))
                {
                    hprate = 0m;
                }

                this.ActorHPRate[actor] = hprate;
            }
        }

        /// <summary>
        /// Addedのログを格納する
        /// </summary>
        /// <param name="logInfo">ログ情報</param>
        private void StoreAddedLog(
            LogLineEventArgs logInfo)
        {
            var match = AddedRegex.Match(logInfo.logLine);
            if (!match.Success)
            {
                return;
            }

            var log = new CombatLog()
            {
                TimeStamp = logInfo.detectedTime,
                Raw = logInfo.logLine,
                Actor = match.Groups["actor"].ToString(),
                Action = "Added",
                LogType = CombatLogType.Added
            };

            this.StoreLog(log);
        }
    }
}
