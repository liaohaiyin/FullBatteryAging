using BatteryAging.Communication;
using BatteryAging.Core.Enums;
using BatteryAging.Core.Models;
using BatteryAging.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace BatteryAging.ViewModels
{
    public partial class TestExecutionViewModel : ObservableObject
    {
        private readonly ChannelManager _channelManager;
        private readonly IDataService _dataService;
        private readonly IDialogService _dialogService;
        private readonly IBatteryAnalyticsService _analytics;
        private readonly IAuthService _auth;
        //private readonly IMesService _mes;

        private readonly ConcurrentDictionary<int, List<DataPoint>> _pendingPoints = new();

        public ObservableCollection<ChannelViewModel> Channels { get; } = new();
        public ObservableCollection<TestRecipe> AvailableRecipes { get; } = new();

        [ObservableProperty] private ChannelViewModel _selectedChannel;
        [ObservableProperty] private TestRecipe _selectedRecipe;
        [ObservableProperty] private string _logText = "";
        [ObservableProperty] private string _barCode = "";

        public IAsyncRelayCommand LoadRecipesCommand { get; }
        public IAsyncRelayCommand StartChannelCommand { get; }
        public IRelayCommand StopChannelCommand { get; }
        public IRelayCommand PauseChannelCommand { get; }
        public IRelayCommand ResumeChannelCommand { get; }
        public IAsyncRelayCommand StartAllCommand { get; }
        public IAsyncRelayCommand SyncStartAllCommand { get; }
        public IRelayCommand StopAllCommand { get; }
        public IRelayCommand ClearLogCommand { get; }

        public TestExecutionViewModel(
            ChannelManager channelManager,
            IDataService dataService,
            IDialogService dialogService,
            IBatteryAnalyticsService analytics,
            IAuthService auth)
        {
            _channelManager = channelManager;
            _dataService = dataService;
            _dialogService = dialogService;
            _analytics = analytics;
            _auth = auth;

            foreach (var executor in _channelManager.GetAll())
            {
                // 查找该通道所属机柜的编号
                var cab = _channelManager.Cabinets.FirstOrDefault(c => c.Id == executor.CabinetId);
                var cabIdx = cab?.CabinetIndex ?? 1;

                var vm = new ChannelViewModel(executor, cabIdx);
                Channels.Add(vm);

                executor.DataSampled += OnDataSampled;
                executor.StatusChanged += OnChannelStatusChanged;
                executor.StepChanged += OnChannelStepChanged;
                executor.CheckpointReached += OnCheckpointReached;
                executor.CycleCompleted += OnCycleCompleted;
                executor.DcirMeasured += async (s, e) =>
                {
                    await _dataService.SaveDcirAsync(e.Result);
                    App.UIDispatch(() => AppendLog($"通道{e.ChannelIndex} DCIR={e.Result.Resistance * 1000:F2} mΩ @SOC{e.Result.Soc:F0}%"));
                };
            }

            _channelManager.CommunicationError += (s, msg) => AppendLog($"⚠ 通讯异常: {msg}");

            if (Channels.Count > 0) SelectedChannel = Channels[0];

            LoadRecipesCommand = new AsyncRelayCommand(LoadRecipesAsync);

            StartChannelCommand = new AsyncRelayCommand(StartChannelAsync,
                () => _auth.HasPermission(Permission.TestExecution_Start)
                   && (SelectedChannel?.Status == ChannelStatus.Idle
                    || SelectedChannel?.Status == ChannelStatus.Completed
                    || SelectedChannel?.Status == ChannelStatus.Stopped));
            StopChannelCommand = new RelayCommand(StopChannel,
                () => _auth.HasPermission(Permission.TestExecution_Stop)
                   && (SelectedChannel?.Status == ChannelStatus.Running
                    || SelectedChannel?.Status == ChannelStatus.Paused));
            PauseChannelCommand = new RelayCommand(PauseChannel,
                () => _auth.HasPermission(Permission.TestExecution_Pause)
                   && SelectedChannel?.Status == ChannelStatus.Running);
            ResumeChannelCommand = new RelayCommand(ResumeChannel,
                () => _auth.HasPermission(Permission.TestExecution_Resume)
                   && SelectedChannel?.Status == ChannelStatus.Paused);
            StartAllCommand = new AsyncRelayCommand(StartAllAsync,
                () => _auth.HasPermission(Permission.TestExecution_StartAll));
            SyncStartAllCommand = new AsyncRelayCommand(SyncStartAllAsync,
                () => _auth.HasPermission(Permission.TestExecution_SyncStart));
            StopAllCommand = new RelayCommand(StopAll,
                () => _auth.HasPermission(Permission.TestExecution_StopAll));
            ClearLogCommand = new RelayCommand(() => LogText = "");

            _ = LoadRecipesAsync();
            _ = CheckInterruptedRecordsAsync();

            StartBatchSaveTimer();            
        }

        partial void OnSelectedChannelChanged(ChannelViewModel value) => RefreshCommands();

        private void RefreshCommands()
        {
            ((AsyncRelayCommand)StartChannelCommand)?.NotifyCanExecuteChanged();
            ((RelayCommand)StopChannelCommand)?.NotifyCanExecuteChanged();
            ((RelayCommand)PauseChannelCommand)?.NotifyCanExecuteChanged();
            ((RelayCommand)ResumeChannelCommand)?.NotifyCanExecuteChanged();
            ((AsyncRelayCommand)StartAllCommand)?.NotifyCanExecuteChanged();
            ((AsyncRelayCommand)SyncStartAllCommand)?.NotifyCanExecuteChanged();
            ((RelayCommand)StopAllCommand)?.NotifyCanExecuteChanged();
        }

        private async Task LoadRecipesAsync()
        {
            try
            {
                var list = await _dataService.GetAllRecipesAsync();
                AvailableRecipes.Clear();
                foreach (var r in list) AvailableRecipes.Add(r);
                SelectedRecipe ??= AvailableRecipes.FirstOrDefault();
            }
            catch (Exception ex) { AppendLog($"加载方案失败: {ex.Message}"); }
        }

        /// <summary>启动时检查未完成的测试，询问是否恢复</summary>
        private async Task CheckInterruptedRecordsAsync()
        {
            try
            {
                var interrupted = await _dataService.GetInterruptedRecordsAsync();
                if (interrupted.Count == 0) return;

                AppendLog($"发现 {interrupted.Count} 条未完成的测试记录");

                foreach (var rec in interrupted)
                {
                    // 标记为"已停止"，避免反复弹窗
                    rec.Status = ChannelStatus.Stopped;
                    rec.EndTime = DateTime.Now;
                    rec.FailReason = "程序异常退出或断电";
                    await _dataService.UpdateRecordAsync(rec);
                }

                var first = interrupted.First();
                var resume = _dialogService.Confirm(
                    $"检测到 {interrupted.Count} 条上次未完成的测试任务\n" +
                    $"最近一条: 通道{first.ChannelIndex} [{first.BarCode}] {first.RecipeName}\n" +
                    $"断点: 工步 #{first.LastStepIndex + 1}, 循环 {first.LastLoopIndex}, 已运行 {TimeSpan.FromSeconds(first.LastTotalElapsed):hh\\:mm\\:ss}\n\n" +
                    "是否从断点恢复？(选择否将以新任务方式查看)");

                if (resume)
                {
                    foreach (var rec in interrupted)
                        await ResumeRecordAsync(rec);
                }
            }
            catch (Exception ex)
            {
                AppendLog($"检查断点失败: {ex.Message}");
            }
        }

        private async Task ResumeRecordAsync(TestRecord record)
        {
            try
            {
                var ch = Channels.FirstOrDefault(c => c.ChannelIndex == record.ChannelIndex);
                if (ch == null)
                {
                    AppendLog($"通道{record.ChannelIndex}不存在，跳过");
                    return;
                }

                var recipe = await _dataService.GetRecipeAsync(record.RecipeId);
                if (recipe == null)
                {
                    AppendLog($"方案 {record.RecipeName} 已删除，无法恢复");
                    return;
                }

                ch.Reset();
                ch.RecipeName = recipe.Name;
                ch.TotalSteps = recipe.Steps.Count;
                ch.BarCode = record.BarCode;
                ch.TestRecordId = record.Id;

                // 创建新的恢复记录
                record.Status = ChannelStatus.Running;
                record.EndTime = null;
                record.FailReason = null;
                await _dataService.UpdateRecordAsync(record);

                AppendLog($"通道{ch.ChannelIndex} 从断点恢复 [{record.BarCode}]");
                _ = ch.Executor.StartAsync(recipe, record, resumeFromCheckpoint: true);
                RefreshCommands();
            }
            catch (Exception ex)
            {
                AppendLog($"恢复失败: {ex.Message}");
            }
        }

        private async Task StartChannelAsync()
        {
            if (SelectedChannel == null || SelectedRecipe == null)
            {
                _dialogService.ShowWarning("请选择通道和测试方案");
                return;
            }
            await StartOneAsync(SelectedChannel, SelectedRecipe);
        }

        private async Task StartOneAsync(ChannelViewModel ch, TestRecipe recipe)
        {
            if (recipe?.Steps == null || recipe.Steps.Count == 0)
            {
                AppendLog($"通道{ch.ChannelIndex}: 方案为空，无法启动");
                return;
            }

            // ── MES 过站校验 ──
            //if (_mes.CheckInEnabled)
            //{
            //    var bar = string.IsNullOrWhiteSpace(BarCode) ? ch.BarCode : BarCode;
            //    var chk = await _mes.CheckInAsync(new MesCheckRequest
            //    {
            //        BarCode = bar,
            //        ChannelIndex = ch.ChannelIndex,
            //        CabinetId = ch.Executor.CabinetId,
            //        RecipeName = recipe.Name
            //    });
            //    if (!chk.Allowed)
            //    {
            //        AppendLog($"通道{ch.ChannelIndex} MES 过站拒绝: {chk.Message}");
            //        _dialogService.ShowWarning($"MES 不允许开测: {chk.Message}");
            //        return;   // 拦停
            //    }
            //    AppendLog($"通道{ch.ChannelIndex} MES 过站通过");
            //}

            try
            {
                ch.Reset();
                ch.RecipeName = recipe.Name;
                ch.TotalSteps = recipe.Steps.Count;
                if (!string.IsNullOrWhiteSpace(BarCode)) ch.BarCode = BarCode;

                var record = new TestRecord
                {
                    CabinetId = ch.Executor.CabinetId,
                    ChannelIndex = ch.ChannelIndex,
                    BarCode = string.IsNullOrWhiteSpace(ch.BarCode)
                        ? $"AUTO_{DateTime.Now:yyMMddHHmmss}_CH{ch.ChannelIndex}"
                        : ch.BarCode,
                    RecipeId = recipe.Id,
                    RecipeName = recipe.Name,
                    NominalCapacity = recipe.NominalCapacity,
                    StartTime = DateTime.Now,
                    Status = ChannelStatus.Running,
                    LastCheckpointTime = DateTime.Now
                };
                record = await _dataService.CreateRecordAsync(record);
                ch.TestRecordId = record.Id;
                if (recipe.Steps.Any(s => s.Type == StepType.SubCall))
                {
                    var flat = RecipeFlattener.Flatten(recipe, id => _dataService.GetRecipeAsync(id).GetAwaiter().GetResult());
                    recipe = new TestRecipe
                    {
                        Id = recipe.Id,
                        Name = recipe.Name,
                        NominalCapacity = recipe.NominalCapacity,
                        NominalVoltage = recipe.NominalVoltage,
                        BatteryType = recipe.BatteryType,
                        Steps = flat
                    };
                }

                AppendLog($"通道{ch.ChannelIndex} [{record.BarCode}] 启动: {recipe.Name}");
                _ = ch.Executor.StartAsync(recipe, record);
                RefreshCommands();
            }
            catch (Exception ex) { _dialogService.ShowError($"启动失败: {ex.Message}"); }
        }

        private void StopChannel() { SelectedChannel?.Executor.Stop(); RefreshCommands(); }
        private void PauseChannel() { SelectedChannel?.Executor.Pause(); RefreshCommands(); }
        private void ResumeChannel() { SelectedChannel?.Executor.Resume(); RefreshCommands(); }

        private async Task StartAllAsync()
        {
            if (SelectedRecipe == null) { _dialogService.ShowWarning("请先选择测试方案"); return; }
            foreach (var ch in Channels.Where(c =>
                c.Status == ChannelStatus.Idle || c.Status == ChannelStatus.Completed ||
                c.Status == ChannelStatus.Stopped))
            {
                await StartOneAsync(ch, SelectedRecipe);
            }
        }

        /// <summary>多通道同步启动 - 使用 Barrier 在同一时刻触发</summary>
        private async Task SyncStartAllAsync()
        {
            if (SelectedRecipe == null) { _dialogService.ShowWarning("请先选择测试方案"); return; }
            var targets = Channels.Where(c =>
                c.Status == ChannelStatus.Idle || c.Status == ChannelStatus.Completed ||
                c.Status == ChannelStatus.Stopped).ToList();
            if (targets.Count == 0) return;

            // 先展开子程序（只做一次），所有通道共用同一份扁平化方案，避免污染绑定属性 SelectedRecipe
            var runRecipe = SelectedRecipe;
            if (runRecipe.Steps.Any(s => s.Type == StepType.SubCall))
            {
                var flat = RecipeFlattener.Flatten(runRecipe,
                    id => _dataService.GetRecipeAsync(id).GetAwaiter().GetResult());
                runRecipe = new TestRecipe
                {
                    Id = runRecipe.Id,
                    Name = runRecipe.Name,
                    NominalCapacity = runRecipe.NominalCapacity,
                    NominalVoltage = runRecipe.NominalVoltage,
                    BatteryType = runRecipe.BatteryType,
                    Steps = flat
                };
            }

            var jobs = new List<(ChannelExecutor, TestRecipe, TestRecord)>();
            foreach (var ch in targets)
            {
                ch.Reset();
                ch.RecipeName = runRecipe.Name;
                ch.TotalSteps = runRecipe.Steps.Count;
                if (!string.IsNullOrWhiteSpace(BarCode)) ch.BarCode = BarCode;

                var record = new TestRecord
                {
                    CabinetId = ch.Executor.CabinetId,
                    ChannelIndex = ch.ChannelIndex,
                    BarCode = string.IsNullOrWhiteSpace(ch.BarCode)
                        ? $"SYNC_{DateTime.Now:yyMMddHHmmss}_CH{ch.ChannelIndex}"
                        : ch.BarCode,
                    RecipeId = runRecipe.Id,
                    RecipeName = runRecipe.Name,
                    NominalCapacity = runRecipe.NominalCapacity,
                    StartTime = DateTime.Now,
                    Status = ChannelStatus.Running,
                    LastCheckpointTime = DateTime.Now
                };
                record = await _dataService.CreateRecordAsync(record);
                ch.TestRecordId = record.Id;
                jobs.Add((ch.Executor, runRecipe, record));
            }

            AppendLog($"同步启动 {jobs.Count} 个通道");
            _ = _channelManager.SyncStartAsync(jobs);
            RefreshCommands();
        }

        private void StopAll() { _channelManager.StopAll(); RefreshCommands(); }

        // ── 数据采样 ──
        private void OnDataSampled(object sender, DataSampleEventArgs e)
        {
            var list = _pendingPoints.GetOrAdd(e.ChannelIndex, _ => new List<DataPoint>());
            lock (list) { list.Add(e.Data); }
        }

        // ── 断点持久化 ──
        private async void OnCheckpointReached(object sender, CheckpointEventArgs e)
        {
            try
            {
                var ch = Channels.FirstOrDefault(c => c.ChannelIndex == e.ChannelIndex);
                if (ch == null || ch.TestRecordId == 0) return;
                await _dataService.UpdateRecordCheckpointAsync(
                    ch.TestRecordId, e.StepIndex, e.LoopIndex, e.TotalElapsedSeconds,
                    e.TotalChargeCapacity, e.TotalDischargeCapacity,
                    e.TotalChargeEnergy, e.TotalDischargeEnergy);
            }
            catch { }
        }

        private void OnChannelStatusChanged(object sender, ChannelStatusChangedEventArgs e)
        {
            App.UIDispatch(() =>
            {
                AppendLog($"通道{e.ChannelIndex}: {e.Status} - {e.Message}");
                RefreshCommands();
                if (e.Status == ChannelStatus.Completed || e.Status == ChannelStatus.Stopped
                    || e.Status == ChannelStatus.Protected || e.Status == ChannelStatus.Error)
                {
                    _ = FinalizeRecordAsync(e.ChannelIndex, e.Status, e.Protection, e.Message);
                }
            });
        }

        private void OnChannelStepChanged(object sender, StepChangedEventArgs e)
        {
            App.UIDispatch(() =>
            {
                if (e.CurrentStep != null)
                    AppendLog($"通道{e.ChannelIndex}: → 工步 #{e.CurrentStep.Sequence} {e.CurrentStep.Name} [Loop {e.LoopIndex}]");
            });
        }

        private async void OnCycleCompleted(object sender, CycleCompletedEventArgs e)
        {
            try
            {
                var ch = Channels.FirstOrDefault(c => c.ChannelIndex == e.ChannelIndex);
                if (ch == null || ch.TestRecordId == 0) return;
                var ce = e.ChargeCapacity > 0 ? e.DischargeCapacity / e.ChargeCapacity : 0;
                var ee = e.ChargeEnergy > 0 ? e.DischargeEnergy / e.ChargeEnergy : 0;
                await _dataService.SaveCycleDataAsync(new CycleData
                {
                    TestRecordId = ch.TestRecordId,
                    ChannelIndex = e.ChannelIndex,
                    CycleIndex = e.CycleIndex,
                    ChargeCapacity = e.ChargeCapacity,
                    DischargeCapacity = e.DischargeCapacity,
                    ChargeEnergy = e.ChargeEnergy,
                    DischargeEnergy = e.DischargeEnergy,
                    CoulombicEfficiency = Math.Round(ce, 4),
                    EnergyEfficiency = Math.Round(ee, 4)
                });
                AppendLog($"通道{e.ChannelIndex} 第{e.CycleIndex}循环: 放电{e.DischargeCapacity:F4}Ah CE={ce * 100:F1}%");
            }
            catch (Exception ex) { AppendLog($"循环记录保存失败: {ex.Message}"); }
        }

        private async Task FinalizeRecordAsync(int channelIndex, ChannelStatus status,
            ProtectionType protection, string message)
        {
            try
            {
                var ch = Channels.FirstOrDefault(c => c.ChannelIndex == channelIndex);
                if (ch == null || ch.TestRecordId == 0) return;

                await FlushPointsAsync(channelIndex);

                var executor = ch.Executor;
                var record = new TestRecord
                {
                    Id = ch.TestRecordId,
                    CabinetId = executor.CabinetId,
                    ChannelIndex = ch.ChannelIndex,
                    BarCode = ch.BarCode,
                    RecipeId = executor.CurrentRecipe?.Id,
                    RecipeName = executor.CurrentRecipe?.Name,
                    NominalCapacity = executor.CurrentRecipe?.NominalCapacity ?? 0,
                    StartTime = executor.CurrentRecord?.StartTime ?? DateTime.Now,
                    EndTime = DateTime.Now,
                    Status = status,
                    ProtectionTrigger = protection,
                    FailReason = message,
                    TotalChargeCapacity = executor.TotalChargeCapacity,
                    TotalDischargeCapacity = executor.TotalDischargeCapacity,
                    TotalChargeEnergy = executor.TotalChargeEnergy,
                    TotalDischargeEnergy = executor.TotalDischargeEnergy,
                    CompletedCycles = executor.CompletedCycles,
                    LastStepIndex = executor.CurrentStepIndex,
                    LastLoopIndex = executor.CurrentLoopIndex,
                    LastTotalElapsed = executor.TotalElapsedSeconds,
                    LastCheckpointTime = DateTime.Now
                };

                // SOH 估算 + 容量分档
                if (record.TotalDischargeCapacity > 0 && record.NominalCapacity > 0)
                {
                    record.SohEstimate = _analytics.EstimateSohByCapacity(
                        record.TotalDischargeCapacity, record.NominalCapacity);
                    record.Grade = _analytics.GradeCapacity(
                        record.TotalDischargeCapacity, record.NominalCapacity);
                    AppendLog($"通道{channelIndex} SOH={record.SohEstimate * 100:F1}% 档位={record.Grade}");
                }

                await _dataService.UpdateRecordAsync(record);

                // ── 上传 MES(失败仅记日志,不影响产线)──
                //if (_mes.IsEnabled)
                //{
                //    var ack = await _mes.UploadResultAsync(new MesTestResult
                //    {
                //        BarCode = record.BarCode,
                //        ChannelIndex = record.ChannelIndex,
                //        RecipeName = record.RecipeName,
                //        StartTime = record.StartTime,
                //        EndTime = record.EndTime,
                //        Status = record.Status.ToString(),
                //        TotalChargeCapacity = record.TotalChargeCapacity,
                //        TotalDischargeCapacity = record.TotalDischargeCapacity,
                //        TotalChargeEnergy = record.TotalChargeEnergy,
                //        TotalDischargeEnergy = record.TotalDischargeEnergy,
                //        SohEstimate = record.SohEstimate,
                //        Grade = record.Grade.ToString(),
                //        CompletedCycles = record.CompletedCycles
                //    });
                //    AppendLog($"通道{channelIndex} MES 上传{(ack.Success ? "成功" : "失败: " + ack.Message)}");
                //}
            }
            catch (Exception ex) { AppendLog($"更新记录失败: {ex.Message}"); }
        }

        private void StartBatchSaveTimer()
        {
            var timer = new System.Timers.Timer(5000) { AutoReset = true };
            timer.Elapsed += async (s, e) =>
            {
                foreach (var key in _pendingPoints.Keys.ToList())
                    await FlushPointsAsync(key);
            };
            timer.Start();
        }

        private async Task FlushPointsAsync(int channelIndex)
        {
            if (!_pendingPoints.TryGetValue(channelIndex, out var list)) return;
            List<DataPoint> toSave;
            lock (list)
            {
                if (list.Count == 0) return;
                toSave = new List<DataPoint>(list);
                list.Clear();
            }
            try { await _dataService.SaveDataPointsAsync(toSave); }
            catch (Exception ex) { AppendLog($"保存数据点失败 CH{channelIndex}: {ex.Message}"); }
        }

        private void AppendLog(string msg)
        {
            App.UIDispatch(() =>
            {
                var t = DateTime.Now.ToString("HH:mm:ss");
                LogText = $"[{t}] {msg}\n" + LogText;
                if (LogText.Length > 30000) LogText = LogText.Substring(0, 20000);
            });
        }
    }
}