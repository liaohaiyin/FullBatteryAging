using System;
using System.Collections.ObjectModel;
using System.Linq;
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

        public RecipeEditorViewModel(IDataService dataService, IDialogService dialogService)
        {
            _dataService = dataService;
            _dialogService = dialogService;

            LoadCommand = new AsyncRelayCommand(LoadAsync);
            NewRecipeCommand = new RelayCommand(NewRecipe);
            SaveRecipeCommand = new AsyncRelayCommand(SaveRecipeAsync);
            DeleteRecipeCommand = new AsyncRelayCommand(DeleteRecipeAsync);
            AddStepCommand = new RelayCommand(AddStep);
            RemoveStepCommand = new RelayCommand(RemoveStep, () => SelectedStep != null);
            MoveUpCommand = new RelayCommand(MoveUp, () => SelectedStep != null && Steps.IndexOf(SelectedStep) > 0);
            MoveDownCommand = new RelayCommand(MoveDown, () => SelectedStep != null && Steps.IndexOf(SelectedStep) < Steps.Count - 1);
            LoadSampleCommand = new RelayCommand(LoadSampleRecipe);

            _ = LoadAsync();
        }

        partial void OnSelectedStepChanged(StepViewModel value)
        {
            ((RelayCommand)RemoveStepCommand).NotifyCanExecuteChanged();
            ((RelayCommand)MoveUpCommand).NotifyCanExecuteChanged();
            ((RelayCommand)MoveDownCommand).NotifyCanExecuteChanged();
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

                recipe.Steps.Clear();
                int seq = 1;
                foreach (var s in Steps)
                {
                    s.Sequence = seq++;
                    recipe.Steps.Add(s.Model);
                }

                await _dataService.SaveRecipeAsync(recipe);
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
                await _dataService.DeleteRecipeAsync(SelectedRecipe.Id);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"删除失败: {ex.Message}");
            }
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
    }
}
