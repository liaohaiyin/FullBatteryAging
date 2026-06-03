using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using BatteryAging.Core.Models;
using BatteryAging.Services;

namespace BatteryAging.ViewModels
{
    public class UserManagementViewModel : ObservableObject, IDisposable
    {
        private readonly IAuthService _authService;
        private readonly IDialogService _dialogService;
        private readonly ILanguageService _languageService;

        public ObservableCollection<User> Users { get; } = new();
        public ObservableCollection<Role> Roles { get; } = new();
        public ObservableCollection<PermissionItemViewModel> PermissionItems { get; } = new();

        private User _selectedUser;
        public User SelectedUser
        {
            get => _selectedUser;
            set
            {
                SetProperty(ref _selectedUser, value);
                LoadUserEditor(value);
                (EditUserCommand as RelayCommand)?.NotifyCanExecuteChanged();
                (DeleteUserCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                (ResetLockCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
            }
        }

        private bool _isEditing;
        public bool IsEditing { get => _isEditing; set => SetProperty(ref _isEditing, value); }

        private bool _isNewUser;
        public bool IsNewUser
        {
            get => _isNewUser;
            set
            {
                if (SetProperty(ref _isNewUser, value))
                {
                    OnPropertyChanged(nameof(EditorHeader));
                    OnPropertyChanged(nameof(PasswordLabel));
                }
            }
        }

        // ── 用户表单 ─────────────────────────────────────────────────────
        private string _editUsername = string.Empty;
        private string _editDisplayName = string.Empty;
        private string _editEmail = string.Empty;
        private Role _editRole;
        private bool _editIsActive = true;
        private string _editPassword = string.Empty;
        private string _editConfirmPwd = string.Empty;
        private string _formError = string.Empty;

        public string EditUsername { get => _editUsername; set { SetProperty(ref _editUsername, value); FormError = string.Empty; } }
        public string EditDisplayName { get => _editDisplayName; set => SetProperty(ref _editDisplayName, value); }
        public string EditEmail { get => _editEmail; set => SetProperty(ref _editEmail, value); }
        public Role EditRole { get => _editRole; set => SetProperty(ref _editRole, value); }
        public bool EditIsActive { get => _editIsActive; set => SetProperty(ref _editIsActive, value); }
        public string EditPassword { get => _editPassword; set { SetProperty(ref _editPassword, value); FormError = string.Empty; } }
        public string EditConfirmPwd { get => _editConfirmPwd; set { SetProperty(ref _editConfirmPwd, value); FormError = string.Empty; } }
        public string FormError { get => _formError; set => SetProperty(ref _formError, value); }

        // ── 角色编辑 ─────────────────────────────────────────────────────
        private Role _selectedRole;
        public Role SelectedRole
        {
            get => _selectedRole;
            set
            {
                SetProperty(ref _selectedRole, value);
                LoadRolePermissions(value);
                (EditRoleCommand as RelayCommand)?.NotifyCanExecuteChanged();
                (DeleteRoleCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
            }
        }

        private bool _isEditingRole;
        public bool IsEditingRole { get => _isEditingRole; set => SetProperty(ref _isEditingRole, value); }

        private bool _isNewRole;
        public bool IsNewRole { get => _isNewRole; set => SetProperty(ref _isNewRole, value); }

        private string _editRoleName = string.Empty;
        private string _editRoleDescription = string.Empty;
        private string _roleFormError = string.Empty;

        public string EditRoleName { get => _editRoleName; set => SetProperty(ref _editRoleName, value); }
        public string EditRoleDescription { get => _editRoleDescription; set => SetProperty(ref _editRoleDescription, value); }
        public string RoleFormError { get => _roleFormError; set => SetProperty(ref _roleFormError, value); }

        // ── 多语言派生属性 ───────────────────────────────────────────────
        public string EditorHeader => IsNewUser
            ? _languageService.GetString("UserManagement_NewUserHeader")
            : _languageService.GetString("UserManagement_EditUserHeader");

        public string PasswordLabel => IsNewUser
            ? _languageService.GetString("UserManagement_Password")
            : _languageService.GetString("UserManagement_NewPasswordOptional");

        // ── 命令 ─────────────────────────────────────────────────────────
        public IAsyncRelayCommand LoadCommand { get; }
        public IRelayCommand NewUserCommand { get; }
        public IRelayCommand EditUserCommand { get; }
        public IAsyncRelayCommand SaveUserCommand { get; }
        public IRelayCommand CancelUserCommand { get; }
        public IAsyncRelayCommand DeleteUserCommand { get; }
        public IAsyncRelayCommand ResetLockCommand { get; }
        public IRelayCommand NewRoleCommand { get; }
        public IRelayCommand EditRoleCommand { get; }
        public IAsyncRelayCommand SaveRoleCommand { get; }
        public IRelayCommand CancelRoleCommand { get; }
        public IAsyncRelayCommand DeleteRoleCommand { get; }

        public UserManagementViewModel(
            IAuthService authService,
            IDialogService dialogService,
            ILanguageService languageService)
        {
            _authService = authService;
            _dialogService = dialogService;
            _languageService = languageService;

            LoadCommand = new AsyncRelayCommand(LoadAsync);
            NewUserCommand = new RelayCommand(NewUser);
            EditUserCommand = new RelayCommand(EditUser, () => SelectedUser != null);
            SaveUserCommand = new AsyncRelayCommand(SaveUserAsync);
            CancelUserCommand = new RelayCommand(() => IsEditing = false);
            DeleteUserCommand = new AsyncRelayCommand(DeleteUserAsync, () => SelectedUser?.IsSystem == false);
            ResetLockCommand = new AsyncRelayCommand(ResetLockAsync, () => SelectedUser?.IsLocked == true);
            NewRoleCommand = new RelayCommand(NewRole);
            EditRoleCommand = new RelayCommand(EditRole_Impl, () => SelectedRole?.IsSystem == false);
            SaveRoleCommand = new AsyncRelayCommand(SaveRoleAsync);
            CancelRoleCommand = new RelayCommand(() => IsEditingRole = false);
            DeleteRoleCommand = new AsyncRelayCommand(DeleteRoleAsync, () => SelectedRole?.IsSystem == false);

            _languageService.LanguageChanged += OnLanguageChanged;

            InitPermissionItems();
            _ = LoadAsync();
        }

        private void OnLanguageChanged(object sender, LanguageChangedEventArgs e)
        {
            var currentGranted = PermissionItems
                .Where(p => !p.IsGroup && p.IsGranted)
                .Select(p => p.Permission)
                .ToHashSet();

            InitPermissionItems();

            foreach (var item in PermissionItems.Where(p => !p.IsGroup))
                if (currentGranted.Contains(item.Permission))
                    item.IsGranted = true;

            OnPropertyChanged(nameof(EditorHeader));
            OnPropertyChanged(nameof(PasswordLabel));
        }

        private void InitPermissionItems()
        {
            var L = _languageService;

            var groups = new (string GroupKey, (Permission Perm, string LabelKey)[] Items)[]
            {
                ("UserManagement_PermGroup_TestExecution", new[]
                {
                    (Permission.TestExecution_View,  "UserManagement_Perm_TestExecution_View"),
                    (Permission.TestExecution_Start, "UserManagement_Perm_TestExecution_Start"),
                    (Permission.TestExecution_Stop,  "UserManagement_Perm_TestExecution_Stop"),
                    (Permission.TestExecution_New,   "UserManagement_Perm_TestExecution_New"),
                    (Permission.TestExecution_Pause,         "UserManagement_Perm_TestExecution_Pause"),
                    (Permission.TestExecution_Resume,        "UserManagement_Perm_TestExecution_Resume"),
                    (Permission.TestExecution_StartAll,      "UserManagement_Perm_TestExecution_StartAll"),
                    (Permission.TestExecution_StopAll,       "UserManagement_Perm_TestExecution_StopAll"),
                    (Permission.TestExecution_SyncStart,     "UserManagement_Perm_TestExecution_SyncStart"),
                    (Permission.TestExecution_SkipStep,      "UserManagement_Perm_TestExecution_SkipStep"),
                    (Permission.TestExecution_ClearFault,    "UserManagement_Perm_TestExecution_ClearFault"),
                    (Permission.TestExecution_EmergencyStop, "UserManagement_Perm_TestExecution_EmergencyStop"),
                    (Permission.TestExecution_ExportLive,    "UserManagement_Perm_TestExecution_ExportLive"),
                }),
                ("UserManagement_PermGroup_FlowEditor", new[]
                {
                    (Permission.FlowEditor_View,     "UserManagement_Perm_FlowEditor_View"),
                    (Permission.FlowEditor_New,      "UserManagement_Perm_FlowEditor_New"),
                    (Permission.FlowEditor_Edit,     "UserManagement_Perm_FlowEditor_Edit"),
                    (Permission.FlowEditor_Save,     "UserManagement_Perm_FlowEditor_Save"),
                    (Permission.FlowEditor_Delete,   "UserManagement_Perm_FlowEditor_Delete"),
                    (Permission.FlowEditor_Clone,    "UserManagement_Perm_FlowEditor_Clone"),
                    (Permission.FlowEditor_Import,   "UserManagement_Perm_FlowEditor_Import"),
                    (Permission.FlowEditor_Export,   "UserManagement_Perm_FlowEditor_Export"),
                    (Permission.FlowEditor_Simulate, "UserManagement_Perm_FlowEditor_Simulate"),
                }),
                ("UserManagement_PermGroup_DataQuery", new[]
                {
                    (Permission.DataQuery_View,   "UserManagement_Perm_DataQuery_View"),
                    (Permission.DataQuery_Export, "UserManagement_Perm_DataQuery_Export"),
                    (Permission.DataQuery_Delete, "UserManagement_Perm_DataQuery_Delete"),
                    (Permission.DataQuery_Report, "UserManagement_Perm_DataQuery_Report"),
                }),
                ("UserManagement_PermGroup_Statistics", new[]
                {
                    (Permission.Statistics_View,   "UserManagement_Perm_Statistics_View"),
                    (Permission.Statistics_Export, "UserManagement_Perm_Statistics_Export"),
                }),
                ("UserManagement_PermGroup_SystemSettings", new[]
                {
                    (Permission.Settings_CommConfig,     "UserManagement_Perm_Settings_CommConfig"),
                    (Permission.Settings_UserManagement, "UserManagement_Perm_Settings_UserManagement"),
                    (Permission.Settings_RoleManagement, "UserManagement_Perm_Settings_RoleManagement"),
                    (Permission.Settings_SystemConfig,   "UserManagement_Perm_Settings_SystemConfig"),
                }),
            };

            PermissionItems.Clear();
            foreach (var (groupKey, items) in groups)
            {
                PermissionItems.Add(new PermissionItemViewModel { IsGroup = true, GroupName = L.GetString(groupKey) });
                foreach (var (perm, labelKey) in items)
                    PermissionItems.Add(new PermissionItemViewModel { IsGroup = false, Permission = perm, Label = L.GetString(labelKey) });
            }
        }

        private async Task LoadAsync()
        {
            var users = await _authService.GetUsersAsync();
            var roles = await _authService.GetRolesAsync();

            Users.Clear();
            foreach (var u in users) Users.Add(u);
            Roles.Clear();
            foreach (var r in roles) Roles.Add(r);
        }

        // ── 用户操作 ─────────────────────────────────────────────────────
        private void NewUser()
        {
            SelectedUser = null;
            IsEditing = true;
            IsNewUser = true;
            EditUsername = string.Empty;
            EditDisplayName = string.Empty;
            EditEmail = string.Empty;
            EditRole = Roles.FirstOrDefault();
            EditIsActive = true;
            EditPassword = string.Empty;
            EditConfirmPwd = string.Empty;
            FormError = string.Empty;
        }

        private void EditUser()
        {
            if (SelectedUser == null) return;
            IsEditing = true;
            IsNewUser = false;
            EditUsername = SelectedUser.Username;
            EditDisplayName = SelectedUser.DisplayName;
            EditEmail = SelectedUser.Email;
            EditRole = Roles.FirstOrDefault(r => r.Id == SelectedUser.RoleId);
            EditIsActive = SelectedUser.IsActive;
            EditPassword = string.Empty;
            EditConfirmPwd = string.Empty;
            FormError = string.Empty;
        }

        private void LoadUserEditor(User user)
        {
            if (user == null) { IsEditing = false; return; }
        }

        private async Task SaveUserAsync()
        {
            FormError = string.Empty;
            if (EditRole == null)
            {
                FormError = _languageService.GetString("UserManagement_Msg_SelectRole");
                return;
            }

            if (IsNewUser)
            {
                if (EditPassword != EditConfirmPwd)
                {
                    FormError = _languageService.GetString("UserManagement_Msg_PasswordMismatch");
                    return;
                }
                var (ok, msg) = await _authService.CreateUserAsync(
                    EditUsername, EditPassword, EditDisplayName, EditEmail, EditRole.Id);
                if (!ok) { FormError = msg; return; }
            }
            else
            {
                var (ok, msg) = await _authService.UpdateUserAsync(
                    SelectedUser.Id, EditDisplayName, EditEmail, EditRole.Id, EditIsActive);
                if (!ok) { FormError = msg; return; }

                if (!string.IsNullOrEmpty(EditPassword))
                {
                    if (EditPassword != EditConfirmPwd)
                    {
                        FormError = _languageService.GetString("UserManagement_Msg_PasswordMismatch");
                        return;
                    }
                    var (pwOk, pwMsg) = await _authService.ChangePasswordAsync(SelectedUser.Id, EditPassword);
                    if (!pwOk) { FormError = pwMsg; return; }
                }
            }

            IsEditing = false;
            await LoadAsync();
        }

        private async Task DeleteUserAsync()
        {
            if (SelectedUser == null) return;

            var confirmMsg = string.Format(
                _languageService.GetString("UserManagement_Msg_ConfirmDeleteUser"), SelectedUser.Username);
            var confirmTitle = _languageService.GetString("UserManagement_Msg_ConfirmDeleteTitle");

            if (!_dialogService.Confirm(confirmMsg, confirmTitle)) return;

            var (ok, msg) = await _authService.DeleteUserAsync(SelectedUser.Id);
            if (!ok) { _dialogService.ShowError(msg); return; }
            await LoadAsync();
        }

        private async Task ResetLockAsync()
        {
            if (SelectedUser == null) return;
            await _authService.ResetLoginFailCountAsync(SelectedUser.Id);
            await LoadAsync();
            _dialogService.ShowMessage(_languageService.GetString("UserManagement_Msg_LockReset"));
        }

        // ── 角色操作 ─────────────────────────────────────────────────────
        private void NewRole()
        {
            SelectedRole = null;
            IsEditingRole = true;
            IsNewRole = true;
            EditRoleName = string.Empty;
            EditRoleDescription = string.Empty;
            RoleFormError = string.Empty;
            foreach (var item in PermissionItems.Where(p => !p.IsGroup)) item.IsGranted = false;
        }

        private void EditRole_Impl()
        {
            if (SelectedRole == null || SelectedRole.IsSystem) return;
            IsEditingRole = true;
            IsNewRole = false;
            EditRoleName = SelectedRole.Name;
            EditRoleDescription = SelectedRole.Description;
            RoleFormError = string.Empty;
            LoadRolePermissions(SelectedRole);
        }

        private void LoadRolePermissions(Role role)
        {
            if (role == null) return;
            foreach (var item in PermissionItems.Where(p => !p.IsGroup))
                item.IsGranted = role.HasPermission(item.Permission);
        }

        private async Task SaveRoleAsync()
        {
            RoleFormError = string.Empty;

            var permissions = Permission.None;
            foreach (var item in PermissionItems.Where(p => !p.IsGroup && p.IsGranted))
                permissions |= item.Permission;

            if (IsNewRole)
            {
                var (ok, msg) = await _authService.CreateRoleAsync(EditRoleName, EditRoleDescription, permissions);
                if (!ok) { RoleFormError = msg; return; }
            }
            else
            {
                var (ok, msg) = await _authService.UpdateRoleAsync(
                    SelectedRole.Id, EditRoleName, EditRoleDescription, permissions);
                if (!ok) { RoleFormError = msg; return; }
            }

            IsEditingRole = false;
            await LoadAsync();
        }

        private async Task DeleteRoleAsync()
        {
            if (SelectedRole == null) return;

            var confirmMsg = string.Format(
                _languageService.GetString("UserManagement_Msg_ConfirmDeleteRole"), SelectedRole.Name);
            var confirmTitle = _languageService.GetString("UserManagement_Msg_ConfirmDeleteTitle");

            if (!_dialogService.Confirm(confirmMsg, confirmTitle)) return;

            var (ok, msg) = await _authService.DeleteRoleAsync(SelectedRole.Id);
            if (!ok) _dialogService.ShowError(msg);
            else await LoadAsync();
        }

        public void Dispose()
        {
            if (_languageService != null)
                _languageService.LanguageChanged -= OnLanguageChanged;
        }
    }

    public class PermissionItemViewModel : ObservableObject
    {
        public bool IsGroup { get; set; }
        public string GroupName { get; set; }
        public Permission Permission { get; set; }
        public string Label { get; set; }

        private bool _isGranted;
        public bool IsGranted { get => _isGranted; set => SetProperty(ref _isGranted, value); }
    }
}
