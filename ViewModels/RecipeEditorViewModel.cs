using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BatteryAging.Core;
using BatteryAging.Core.Enums;
using BatteryAging.Core.Models;
using BatteryAging.Services;

namespace BatteryAging.ViewModels
{
    public partial class RecipeEditorViewModel : ObservableObject
    {
        private readonly IDataService _dataService;
        private readonly IDialogService _dialogService;
        private readonly IAuthService _auth;

        public ObservableCollection<TestRecipe> Recipes { get; } = new();
        public ObservableCollection<StepViewModel> Steps { get; } = new();

        public List<EnumDisplayItem<StepType>> StepTypes { get; } = EnumHelper.GetItems<StepType>();
        public List<EnumDisplayItem<TriggerType>> TriggerTypes { get; } = EnumHelper.GetItems<TriggerType>();
        public List<EnumDisplayItem<CompareOperator>> CompareOperators { get; } = EnumHelper.GetItems<CompareOperator>();

        [ObservableProperty] private TestRecipe _selectedRecipe;
        [ObservableProperty] private StepViewModel _selectedStep;
        [ObservableProperty] private bool _isDirty;

        // 方案信息
        [ObservableProperty] private string _recipeName;
        [ObservableProperty] private string _recipeDescription;
        [ObservableProperty] private string _batteryType = "NCM";
        [ObservableProperty] private double _nominalCapacity = 2.6;
        [ObservableProperty] private double _nominalVoltage = 3.7;

        public IAsyncRelayCommand LoadCommand { get; }
        public IRelayCommand NewRecipeCommand { get; }
        public IAsyncRelayCommand SaveRecipeCommand { get; }
        public IAsyncRelayCommand DeleteRecipeCommand { get; }
        public IRelayCommand AddStepCommand { get; }
        public IRelayCommand RemoveStepCommand { get; }
        public IRelayCommand MoveUpCommand { get; }
        public IRelayCommand MoveDownCommand { get; }
        public IRelayCommand LoadSampleCommand { get; }
        public IRelayCommand GenerateBuilderCommand { get; }
        public IAsyncRelayCommand ExportRecipeCommand { get; }
        public IAsyncRelayCommand ImportRecipeCommand { get; }
        public IAsyncRelayCommand ImportWaveformCommand { get; }

        public RecipeEditorViewModel(IDataService dataService, IDialogService dialogService, IAuthService auth)
        {
            _dataService = dataService;
            _dialogService = dialogService;
            _auth = auth;

            LoadCommand = new AsyncRelayCommand(LoadAsync);
            NewRecipeCommand = new RelayCommand(NewRecipe,
                () => _auth.HasPermission(Permission.FlowEditor_New));
            SaveRecipeCommand = new AsyncRelayCommand(SaveRecipeAsync,
                () => _auth.HasPermission(Permission.FlowEditor_Save));
            DeleteRecipeCommand = new AsyncRelayCommand(DeleteRecipeAsync,
                () => _auth.HasPermission(Permission.FlowEditor_Delete));
            AddStepCommand = new RelayCommand(AddStep,
                () => _auth.HasPermission(Permission.FlowEditor_Edit));
            RemoveStepCommand = new RelayCommand(RemoveStep,
                () => SelectedStep != null && _auth.HasPermission(Permission.FlowEditor_Edit));
            MoveUpCommand = new RelayCommand(MoveUp,
                () => SelectedStep != null && Steps.IndexOf(SelectedStep) > 0 && _auth.HasPermission(Permission.FlowEditor_Edit));
            MoveDownCommand = new RelayCommand(MoveDown,
                () => SelectedStep != null && Steps.IndexOf(SelectedStep) < Steps.Count - 1 && _auth.HasPermission(Permission.FlowEditor_Edit));
            LoadSampleCommand = new RelayCommand(LoadSampleRecipe,
                () => _auth.HasPermission(Permission.FlowEditor_New));
            GenerateBuilderCommand = new RelayCommand(GenerateFromBuilder,
                () => _auth.HasPermission(Permission.FlowEditor_New));
            ExportRecipeCommand = new AsyncRelayCommand(ExportRecipeAsync,
                () => SelectedRecipe != null && _auth.HasPermission(Permission.FlowEditor_Export));
            ImportRecipeCommand = new AsyncRelayCommand(ImportRecipeAsync,
                () => _auth.HasPermission(Permission.FlowEditor_Import));
            ImportWaveformCommand = new AsyncRelayCommand(ImportWaveformAsync,
                () => SelectedStep != null && _auth.HasPermission(Permission.FlowEditor_Edit));

            _ = LoadAsync();
        }

        partial void OnSelectedStepChanged(StepViewModel value)
        {
            ((RelayCommand)RemoveStepCommand).NotifyCanExecuteChanged();
            ((RelayCommand)MoveUpCommand).NotifyCanExecuteChanged();
            ((RelayCommand)MoveDownCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)ImportWaveformCommand).NotifyCanExecuteChanged();
        }

        partial void OnSelectedRecipeChanged(TestRecipe value)
        {
            if (value != null)
            {
                RecipeName = value.Name;
                RecipeDescription = value.Description;
                BatteryType = value.BatteryType;
                NominalCapacity = value.NominalCapacity;
                NominalVoltage = value.NominalVoltage;

                Steps.Clear();
                foreach (var s in value.Steps.OrderBy(s => s.Sequence))
                    Steps.Add(new StepViewModel(s));

                IsDirty = false;
            }
            ((AsyncRelayCommand)ExportRecipeCommand)?.NotifyCanExecuteChanged();
        }

        private async Task LoadAsync()
        {
            try
            {
                var list = await _dataService.GetAllRecipesAsync();
                Recipes.Clear();
                foreach (var r in list) Recipes.Add(r);

                if (Recipes.Count == 0)
                {
                    // 首次启动加载示例
                    LoadSampleRecipe();
                }
                else
                {
                    SelectedRecipe = Recipes.First();
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"加载方案失败: {ex.Message}");
            }
        }

        private void NewRecipe()
        {
            SelectedRecipe = null;
            RecipeName = "新建方案";
            RecipeDescription = "";
            BatteryType = "NCM";
            NominalCapacity = 2.6;
            NominalVoltage = 3.7;
            Steps.Clear();
            IsDirty = true;
        }

        private async Task SaveRecipeAsync()
        {
            try
            {
                var recipe = SelectedRecipe ?? new TestRecipe();
                recipe.Name = RecipeName;
                recipe.Description = RecipeDescription;
                recipe.BatteryType = BatteryType;
                recipe.NominalCapacity = NominalCapacity;
                recipe.NominalVoltage = NominalVoltage;
                recipe.Creator = _auth.CurrentSession.Username;

                recipe.Steps.Clear();
                int seq = 1;
                foreach (var s in Steps)
                {
                    s.Sequence = seq++;
                    recipe.Steps.Add(s.Model);
                }

                // 校验：Pulse / CR_Discharge 必须有时长/容量/触发任一截止条件，否则会一直跑到超时保护
                foreach (var s in recipe.Steps)
                {
                    if (s.Type == StepType.Pulse || s.Type == StepType.CR_Discharge)
                    {
                        bool hasCutoff = s.DurationSeconds > 0
                            || s.CapacityLimit > 0
                            || s.CutoffVoltage > 0
                            || s.TriggerValue != 0;
                        if (!hasCutoff)
                        {
                            _dialogService.ShowWarning(
                                $"工步 #{s.Sequence} ({EnumHelper.GetDescription(s.Type)}) " +
                                "未设置任何截止条件（时长/容量/截止电压/触发），\n请补充后再保存。");
                            return;
                        }
                    }
                    if (s.Type == StepType.Waveform && string.IsNullOrWhiteSpace(s.WaveformProfileJson))
                    {
                        _dialogService.ShowWarning(
                            $"工步 #{s.Sequence} ({EnumHelper.GetDescription(s.Type)}) 尚未导入工况波形文件，\n请导入 CSV 波形后再保存。");
                        return;
                    }
                }

                await _dataService.SaveRecipeAsync(recipe);
                await LogAuditAsync("Update", "TestRecipe", recipe.Id, $"保存测试方案: {recipe.Name}");
                await LoadAsync();
                SelectedRecipe = Recipes.FirstOrDefault(r => r.Id == recipe.Id);
                IsDirty = false;
                _dialogService.ShowMessage("方案已保存");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"保存失败: {ex.Message}");
            }
        }

        private async Task DeleteRecipeAsync()
        {
            if (SelectedRecipe == null) return;
            if (!_dialogService.Confirm($"确定删除方案 '{SelectedRecipe.Name}' 吗?")) return;
            try
            {
                var id = SelectedRecipe.Id;
                var name = SelectedRecipe.Name;
                await _dataService.DeleteRecipeAsync(id);
                await LogAuditAsync("Delete", "TestRecipe", id, $"删除测试方案: {name}");
                await LoadAsync();
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"删除失败: {ex.Message}");
            }
        }

        private async Task ExportRecipeAsync()
        {
            if (SelectedRecipe == null) return;
            var path = _dialogService.SaveFileDialog("JSON 文件|*.json", $"{SelectedRecipe.Name}.json", "导出工艺方案");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var json = JsonSerializer.Serialize(SelectedRecipe, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(path, json);
                await LogAuditAsync("Export", "TestRecipe", SelectedRecipe.Id, $"导出测试方案: {SelectedRecipe.Name}");
                _dialogService.ShowMessage($"已导出方案到\n{path}");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"导出失败: {ex.Message}");
            }
        }

        private async Task ImportRecipeAsync()
        {
            var path = _dialogService.OpenFileDialog("JSON 文件|*.json", "导入工艺方案");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var json = await File.ReadAllTextAsync(path);
                var recipe = JsonSerializer.Deserialize<TestRecipe>(json);
                if (recipe == null || recipe.Steps == null || recipe.Steps.Count == 0)
                {
                    _dialogService.ShowWarning("文件内容无效或不含工步");
                    return;
                }

                // 生成新 Id，避免与本地已有方案冲突
                recipe.Id = Guid.NewGuid().ToString();
                recipe.Name = string.IsNullOrWhiteSpace(recipe.Name) ? "导入方案" : $"{recipe.Name} (导入)";
                recipe.CreateTime = DateTime.Now;
                recipe.UpdateTime = DateTime.Now;
                recipe.Creator = _auth.CurrentSession.IsAuthenticated ? _auth.CurrentSession.Username : Environment.UserName;
                recipe.IsActive = true;
                foreach (var step in recipe.Steps)
                    step.Id = Guid.NewGuid().ToString();

                await _dataService.SaveRecipeAsync(recipe);
                await LogAuditAsync("Create", "TestRecipe", recipe.Id, $"导入测试方案: {recipe.Name}");
                await LoadAsync();
                SelectedRecipe = Recipes.FirstOrDefault(r => r.Id == recipe.Id);
                _dialogService.ShowMessage($"已导入方案 [{recipe.Name}]，共 {recipe.Steps.Count} 个工步");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"导入失败: {ex.Message}");
            }
        }

        private async Task ImportWaveformAsync()
        {
            if (SelectedStep == null) return;
            var path = _dialogService.OpenFileDialog("CSV 文件|*.csv", "导入工况波形（每行：时间秒,电流A）");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var lines = await File.ReadAllLinesAsync(path);
                var points = new List<WaveformPoint>();
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split(new[] { ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2) continue;
                    if (!double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var t)) continue;
                    if (!double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var i)) continue;
                    points.Add(new WaveformPoint { TimeSeconds = t, Current = i });
                }

                if (points.Count == 0)
                {
                    _dialogService.ShowWarning("未解析到有效数据行，格式应为每行：时间(秒),电流(A)");
                    return;
                }

                points = points.OrderBy(p => p.TimeSeconds).ToList();
                SelectedStep.WaveformProfileJson = JsonSerializer.Serialize(points);
                SelectedStep.WaveformFileName = Path.GetFileName(path);
                IsDirty = true;
                _dialogService.ShowMessage($"已导入波形 {points.Count} 点，时长 {points[points.Count - 1].TimeSeconds:F0} s");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"导入波形失败: {ex.Message}");
            }
        }

        private async Task LogAuditAsync(string action, string entityType, string entityId, string detail)
        {
            try
            {
                var s = _auth.CurrentSession;
                await _dataService.LogAuditAsync(new AuditLog
                {
                    UserId = s.UserId,
                    Username = s.IsAuthenticated ? s.Username : "system",
                    Action = action,
                    EntityType = entityType,
                    EntityId = entityId,
                    Detail = detail
                });
            }
            catch { /* 审计日志失败不应阻断主业务操作 */ }
        }

        private void AddStep()
        {
            var step = new TestStep
            {
                Sequence = Steps.Count + 1,
                Name = $"步骤{Steps.Count + 1}",
                Type = StepType.Rest,
                DurationSeconds = 60,
                MaxVoltage = 4.2,
                MinVoltage = 2.5,
                MaxCurrent = 5.0,
                MaxTemperature = 60,
                ProtectionTimeSeconds = 3600,
                LoopCount = 1
            };
            var vm = new StepViewModel(step);
            Steps.Add(vm);
            SelectedStep = vm;
            IsDirty = true;
        }

        private void RemoveStep()
        {
            if (SelectedStep == null) return;
            Steps.Remove(SelectedStep);
            ResequenceSteps();
            IsDirty = true;
        }

        private void MoveUp()
        {
            int idx = Steps.IndexOf(SelectedStep);
            if (idx > 0)
            {
                Steps.Move(idx, idx - 1);
                ResequenceSteps();
                IsDirty = true;
            }
        }

        private void MoveDown()
        {
            int idx = Steps.IndexOf(SelectedStep);
            if (idx < Steps.Count - 1)
            {
                Steps.Move(idx, idx + 1);
                ResequenceSteps();
                IsDirty = true;
            }
        }

        private void ResequenceSteps()
        {
            for (int i = 0; i < Steps.Count; i++) Steps[i].Sequence = i + 1;
        }

        /// <summary>
        /// 加载标准的"充放电老化"示例方案
        /// 步骤1：恒流充电
        /// 步骤2：恒压充电
        /// 步骤3：静置
        /// 步骤4：恒流放电
        /// 步骤5：静置
        /// 步骤6：循环
        /// </summary>
        private void LoadSampleRecipe()
        {
            SelectedRecipe = null;
            RecipeName = "标准老化方案_2.6Ah";
            RecipeDescription = "1C充放电老化 - 3 次循环";
            BatteryType = "NCM";
            NominalCapacity = 2.6;
            NominalVoltage = 3.7;

            Steps.Clear();
            Steps.Add(new StepViewModel(new TestStep
            {
                Sequence = 1, Name = "1C 恒流充电", Type = StepType.CC_Charge,
                Current = 2.6, CutoffVoltage = 4.2,
                MaxVoltage = 4.25, MinVoltage = 2.5, MaxCurrent = 5.0,
                MaxTemperature = 60, ProtectionTimeSeconds = 7200, LoopCount = 1
            }));
            Steps.Add(new StepViewModel(new TestStep
            {
                Sequence = 2, Name = "恒压充电", Type = StepType.CV_Charge,
                Voltage = 4.2, Current = 2.6, CutoffCurrent = 0.13,
                MaxVoltage = 4.25, MinVoltage = 2.5, MaxCurrent = 5.0,
                MaxTemperature = 60, ProtectionTimeSeconds = 7200, LoopCount = 1
            }));
            Steps.Add(new StepViewModel(new TestStep
            {
                Sequence = 3, Name = "静置 10 分钟", Type = StepType.Rest,
                DurationSeconds = 600,
                MaxVoltage = 4.25, MinVoltage = 2.5, MaxCurrent = 5.0,
                MaxTemperature = 60, ProtectionTimeSeconds = 7200, LoopCount = 1
            }));
            Steps.Add(new StepViewModel(new TestStep
            {
                Sequence = 4, Name = "1C 恒流放电", Type = StepType.CC_Discharge,
                Current = 2.6, CutoffVoltage = 2.8,
                MaxVoltage = 4.25, MinVoltage = 2.5, MaxCurrent = 5.0,
                MaxTemperature = 60, ProtectionTimeSeconds = 7200, LoopCount = 1
            }));
            Steps.Add(new StepViewModel(new TestStep
            {
                Sequence = 5, Name = "静置 10 分钟", Type = StepType.Rest,
                DurationSeconds = 600,
                MaxVoltage = 4.25, MinVoltage = 2.5, MaxCurrent = 5.0,
                MaxTemperature = 60, ProtectionTimeSeconds = 7200, LoopCount = 1
            }));
            Steps.Add(new StepViewModel(new TestStep
            {
                Sequence = 6, Name = "循环 3 次", Type = StepType.Loop,
                LoopStartIndex = 0, LoopCount = 3
            }));

            IsDirty = true;
        }

        /// <summary>用内置 Builder 生成老化方案模板，参数取默认值 + 当前标称容量，生成后可在工步表继续微调</summary>
        private void GenerateFromBuilder()
        {
            // 用 InputDialog 让用户三选一（1=日历寿命 2=HPPC 3=最大能量）
            var pick = _dialogService.InputDialog(
                "选择要生成的方案模板：\n1 = 日历寿命 (Calendar)\n2 = HPPC 功率特性\n3 = 最大能量",
                "生成方案模板", "1");
            if (string.IsNullOrWhiteSpace(pick)) return;

            var cn = NominalCapacity > 0 ? NominalCapacity : 2.6;
            TestRecipe recipe;
            switch (pick.Trim())
            {
                case "1":
                    recipe = CalendarAgingBuilder.Build(new CalendarAgingOptions { NominalCapacity = cn });
                    break;
                case "2":
                    recipe = HppcBuilder.Build(new HppcOptions { NominalCapacity = cn });
                    break;
                case "3":
                    recipe = MaxEnergyBuilder.Build(new MaxEnergyOptions { NominalCapacity = cn });
                    break;
                default:
                    _dialogService.ShowWarning("请输入 1、2 或 3");
                    return;
            }

            // 载入到编辑器（视为新建未保存方案）
            SelectedRecipe = null;
            RecipeName = recipe.Name;
            RecipeDescription = recipe.Description;
            BatteryType = recipe.BatteryType;
            NominalCapacity = recipe.NominalCapacity;
            NominalVoltage = recipe.NominalVoltage;

            Steps.Clear();
            foreach (var s in recipe.Steps.OrderBy(s => s.Sequence))
                Steps.Add(new StepViewModel(s));
            ResequenceSteps();

            IsDirty = true;
        }
    }
}
