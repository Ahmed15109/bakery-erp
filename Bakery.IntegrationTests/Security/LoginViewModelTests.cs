using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Infrastructure.Data;
using Bakery.WPF;
using Bakery.WPF.Services;
using Bakery.WPF.ViewModels;
using Bakery.WPF.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Bakery.IntegrationTests;

[Collection("Login WPF")]
public sealed class LoginViewModelTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;
    private static readonly BranchDto BranchOne = new(1, "B1", "الفرع الأول", true, null);
    private static readonly BranchDto BranchTwo = new(2, "B2", "الفرع الثاني", true, null);
    private static readonly UserDto UserOne = new(11, "cashier-one", "Cashier One");
    private static readonly UserDto UserTwo = new(22, "cashier-two", "Cashier Two");

    public LoginViewModelTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task InitializationAndBranchChange_LoadBranchScopedUsersAutomatically()
    {
        var auth = new FakeAuthService(
            [BranchOne, BranchTwo],
            new Dictionary<int, IReadOnlyList<UserDto>>
            {
                [BranchOne.Id] = [UserOne],
                [BranchTwo.Id] = [UserTwo]
            });
        var viewModel = CreateViewModel(auth);

        await viewModel.InitializationTask;

        viewModel.SelectedBranch.Should().Be(BranchOne);
        viewModel.Users.Should().Equal(UserOne);
        viewModel.SelectedUser.Should().Be(UserOne);

        viewModel.SelectedBranch = BranchTwo;
        await viewModel.PendingUserRefresh;

        viewModel.Users.Should().Equal(UserTwo);
        viewModel.SelectedUser.Should().Be(UserTwo);
        auth.RequestedBranchIds.Should().Equal(BranchOne.Id, BranchTwo.Id);
        viewModel.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task LoginCommand_DisablesWhileAuthenticating_AndRestoresFailedLoginState()
    {
        var loginStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loginCompletion = new TaskCompletionSource<AuthResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var auth = CreateSingleUserAuth(async request =>
        {
            loginStarted.SetResult();
            return await loginCompletion.Task;
        });
        var viewModel = CreateViewModel(auth);
        await viewModel.InitializationTask;
        viewModel.Password = "wrong-password";
        var resetRequests = 0;
        viewModel.LoginFailed += (_, _) => resetRequests++;

        var loginTask = viewModel.LoginCommand.ExecuteAsync(null);
        await loginStarted.Task;

        viewModel.IsBusy.Should().BeTrue();
        viewModel.LoginCommand.CanExecute(null).Should().BeFalse();

        loginCompletion.SetResult(new AuthResult(false, "اسم المستخدم أو كلمة المرور غير صحيحة.", null));
        await loginTask;

        viewModel.IsBusy.Should().BeFalse();
        viewModel.LoginCommand.CanExecute(null).Should().BeTrue();
        viewModel.ErrorMessage.Should().Be("اسم المستخدم أو كلمة المرور غير صحيحة.");
        viewModel.Password.Should().BeEmpty();
        resetRequests.Should().Be(1);
        auth.LastLoginRequest.Should().Be(new LoginRequest(UserOne.Username, "wrong-password", BranchOne.Id));
    }

    [Fact]
    public void ActualWindowAndAuthentication_InvalidPasswordThenValidPassword_AllowsImmediateRetry()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            App? app = null;
            LoginWindow? window = null;
            IServiceScope? loginScope = null;
            try
            {
                const string correctPassword = "CorrectPassword!123";
                var username = $"wpf-login-{Guid.NewGuid():N}";
                int branchId;

                using (var setupScope = _fixture.ServiceProvider.CreateScope())
                {
                    var db = setupScope.ServiceProvider.GetRequiredService<BakeryDbContext>();
                    var passwordHasher = setupScope.ServiceProvider.GetRequiredService<IPasswordHasher>();
                    var branch = db.Branches.IgnoreQueryFilters()
                        .First(entity => entity.IsActive && !entity.IsDeleted);
                    var user = new User
                    {
                        Username = username,
                        FullName = "WPF Login Runtime User",
                        PasswordHash = passwordHasher.HashPassword(correctPassword),
                        IsActive = true
                    };
                    db.Users.Add(user);
                    db.SaveChanges();
                    db.UserBranches.Add(new UserBranch { UserId = user.Id, BranchId = branch.Id });
                    db.SaveChanges();
                    branchId = branch.Id;
                }

                app = new App();
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                // Construct feature views against the real application resources so
                // resource and populated-row binding failures occur in CI instead of navigation.
                var backupManagementView = new BackupManagementView();
                var backupGrid = (DataGrid)backupManagementView.FindName("BackupHistoryGrid");
                backupGrid.ItemsSource = new[]
                {
                    new BackupMetadata
                    {
                        Id = 1,
                        FileName = "Backup_test.zip",
                        FilePath = @"C:\Backups\Backup_test.zip",
                        CreatedAt = DateTime.Now,
                        BackupType = Bakery.Domain.Enums.BackupType.Automatic,
                        Status = Bakery.Domain.Enums.BackupStatus.Success,
                        CloudStatus = Bakery.Domain.Enums.CloudBackupStatus.Uploaded,
                        SizeBytes = 1_048_576,
                        LocalFileAvailable = true,
                        GoogleDriveFileId = "test-drive-file-id"
                    }
                };
                backupManagementView.Measure(new Size(1_100, 650));
                backupManagementView.Arrange(new Rect(0, 0, 1_100, 650));
                backupManagementView.UpdateLayout();
                PumpDispatcherFor(TimeSpan.FromMilliseconds(250));
                backupGrid.Items.Count.Should().Be(1);
                backupGrid.Columns.OfType<DataGridCheckBoxColumn>()
                    .Select(column => ((System.Windows.Data.Binding)column.Binding).Mode)
                    .Should().OnlyContain(mode => mode == System.Windows.Data.BindingMode.OneWay);

                var settingsView = new SettingsView();
                settingsView.Measure(new Size(1_100, 700));
                settingsView.Arrange(new Rect(0, 0, 1_100, 700));
                settingsView.UpdateLayout();
                ((FrameworkElement)settingsView.FindName("WorkingDaySection")).Should().NotBeNull();
                var reopenButton = (Button)settingsView.FindName("ReopenWorkingDayButton");
                reopenButton.Should().NotBeNull();
                System.Windows.Data.BindingOperations.GetBinding(reopenButton, ContentControl.ContentProperty)!
                    .Path.Path.Should().Be("ReopenButtonText");
                ((TextBlock)settingsView.FindName("CurrentWorkingDayDateText")).Should().NotBeNull();
                ((TextBlock)settingsView.FindName("LastClosedWorkingDayDateText")).Should().NotBeNull();
                ((TextBlock)settingsView.FindName("LastClosedWorkingDayUserText")).Should().NotBeNull();
                ((TextBlock)settingsView.FindName("LastClosedWorkingDayTimeText")).Should().NotBeNull();
                ((TextBlock)settingsView.FindName("ReopenEligibilityStatusText")).Should().NotBeNull();

                loginScope = _fixture.ServiceProvider.CreateScope();
                var authService = loginScope.ServiceProvider.GetRequiredService<IAuthService>();
                var viewModel = new LoginViewModel(authService);
                PumpDispatcherUntil(() => viewModel.InitializationTask.IsCompleted, TimeSpan.FromSeconds(15));
                viewModel.InitializationTask.GetAwaiter().GetResult();

                window = new LoginWindow(viewModel);
                window.Show();
                PumpDispatcherFor(TimeSpan.FromSeconds(1));

                var mainContainer = (FrameworkElement)window.FindName("MainContainer");
                var loginForm = (FrameworkElement)window.FindName("LoginForm");
                var branchInput = (ComboBox)window.FindName("BranchInput");
                var userInput = (ComboBox)window.FindName("UserInput");
                var passwordInput = (PasswordBox)window.FindName("PasswordInput");
                var loginButton = (Button)window.FindName("LoginButton");

                branchInput.SelectedItem = viewModel.Branches.Single(branch => branch.Id == branchId);
                PumpDispatcherUntil(
                    () => viewModel.PendingUserRefresh.IsCompleted &&
                          viewModel.Users.Any(user => user.Username == username),
                    TimeSpan.FromSeconds(15));
                userInput.SelectedItem = viewModel.Users.Single(user => user.Username == username);
                passwordInput.Password = "invalid-password";

                var invalidDuration = Stopwatch.StartNew();
                InvokeButton(loginButton);
                PumpDispatcherUntil(
                    () => !viewModel.LoginCommand.IsRunning &&
                          !string.IsNullOrWhiteSpace(viewModel.ErrorMessage),
                    TimeSpan.FromSeconds(10));
                DrainDispatcher();
                invalidDuration.Stop();

                invalidDuration.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
                viewModel.IsBusy.Should().BeFalse();
                viewModel.LoginCommand.IsRunning.Should().BeFalse();
                viewModel.LoginCommand.CanExecute(null).Should().BeTrue();
                viewModel.ErrorMessage.Should().Be(Bakery.Shared.Helpers.Loc.ErrInvalidCredentials);
                window.IsVisible.Should().BeTrue();
                window.IsEnabled.Should().BeTrue();
                mainContainer.IsEnabled.Should().BeTrue();
                mainContainer.IsHitTestVisible.Should().BeTrue();
                mainContainer.Opacity.Should().Be(1);
                loginForm.IsEnabled.Should().BeTrue();
                loginForm.IsHitTestVisible.Should().BeTrue();
                loginForm.Opacity.Should().Be(1);
                branchInput.IsEnabled.Should().BeTrue();
                branchInput.IsHitTestVisible.Should().BeTrue();
                userInput.IsEnabled.Should().BeTrue();
                userInput.IsHitTestVisible.Should().BeTrue();
                passwordInput.IsEnabled.Should().BeTrue();
                passwordInput.IsHitTestVisible.Should().BeTrue();
                loginButton.IsEnabled.Should().BeTrue();
                loginButton.IsHitTestVisible.Should().BeTrue();
                passwordInput.Password.Should().BeEmpty();
                Keyboard.FocusedElement.Should().BeSameAs(passwordInput);
                FindVisualChildren<FrameworkElement>(window)
                    .Where(element =>
                        (element.Name.Contains("Overlay", StringComparison.OrdinalIgnoreCase) ||
                         element.Name.Contains("Loading", StringComparison.OrdinalIgnoreCase)) &&
                        element.Visibility == Visibility.Visible &&
                        element.IsHitTestVisible)
                    .Should().BeEmpty();
                FindVisualChildren<ProgressBar>(window)
                    .Where(progress => progress.Visibility == Visibility.Visible)
                    .Should().BeEmpty();
                DependencyPropertyHelper.GetValueSource(mainContainer, UIElement.OpacityProperty)
                    .IsAnimated.Should().BeFalse();

                var loginSucceeded = false;
                window.LoginSucceeded += (_, _) => loginSucceeded = true;
                passwordInput.Password = correctPassword;
                InvokeButton(loginButton);
                PumpDispatcherUntil(
                    () => loginSucceeded && !viewModel.LoginCommand.IsRunning,
                    TimeSpan.FromSeconds(10));

                loginSucceeded.Should().BeTrue();
                viewModel.ErrorMessage.Should().BeNull();
                viewModel.IsBusy.Should().BeFalse();
                viewModel.LoginCommand.IsRunning.Should().BeFalse();
                authService.LogoutAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                window?.Close();
                loginScope?.Dispose();
                if (app is not null && !Dispatcher.CurrentDispatcher.HasShutdownStarted)
                {
                    Dispatcher.CurrentDispatcher.InvokeShutdown();
                }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(60)).Should().BeTrue("the actual WPF login integration must complete");
        failure.Should().BeNull();
    }

    [Fact]
    public async Task SuccessfulAuthentication_IgnoresLegacyPasswordChangeFlag_AndResetsBusyState()
    {
        var authenticatedUser = new AuthenticatedUserDto(
            UserOne.Id,
            UserOne.Username,
            UserOne.FullName,
            [],
            MustChangePassword: true);
        var auth = CreateSingleUserAuth(_ => Task.FromResult(new AuthResult(true, null, authenticatedUser)));
        var viewModel = CreateViewModel(auth);
        await viewModel.InitializationTask;
        viewModel.Password = "correct-password";
        var succeeded = false;
        viewModel.LoginSucceeded += (_, _) => succeeded = true;

        await viewModel.LoginCommand.ExecuteAsync(null);

        succeeded.Should().BeTrue();
        viewModel.ErrorMessage.Should().BeNull();
        viewModel.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task MainWindowStartupFailure_KeepsLoginUsableAndClearsAuthenticatedSession()
    {
        var authenticatedUser = new AuthenticatedUserDto(UserOne.Id, UserOne.Username, UserOne.FullName, []);
        var auth = CreateSingleUserAuth(_ => Task.FromResult(new AuthResult(true, null, authenticatedUser)));
        var viewModel = CreateViewModel(auth);
        await viewModel.InitializationTask;
        viewModel.Password = "correct-password";
        viewModel.LoginSucceeded += (_, _) => throw new InvalidOperationException("Main window failed to open.");

        await viewModel.LoginCommand.ExecuteAsync(null);

        auth.LogoutCount.Should().Be(1);
        viewModel.IsBusy.Should().BeFalse();
        viewModel.LoginCommand.CanExecute(null).Should().BeTrue();
        viewModel.ErrorMessage.Should().Be("تعذر تسجيل الدخول. تحقق من البيانات وحاول مرة أخرى.");
    }

    private static LoginViewModel CreateViewModel(FakeAuthService auth) => new(auth);

    private static FakeAuthService CreateSingleUserAuth(Func<LoginRequest, Task<AuthResult>> login) =>
        new(
            [BranchOne],
            new Dictionary<int, IReadOnlyList<UserDto>> { [BranchOne.Id] = [UserOne] },
            login);

    private static void DrainDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.SystemIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void PumpDispatcherFor(TimeSpan duration)
    {
        var stopwatch = Stopwatch.StartNew();
        PumpDispatcherUntil(() => stopwatch.Elapsed >= duration, duration + TimeSpan.FromSeconds(2));
    }

    private static void PumpDispatcherUntil(Func<bool> condition, TimeSpan timeout)
    {
        if (condition())
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(10)
        };
        timer.Tick += (_, _) =>
        {
            if (condition() || stopwatch.Elapsed >= timeout)
            {
                frame.Continue = false;
            }
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();

        condition().Should().BeTrue($"the WPF operation should complete within {timeout}");
    }

    private static void InvokeButton(Button button)
    {
        var peer = new ButtonAutomationPeer(button);
        var invokeProvider = (IInvokeProvider?)peer.GetPattern(PatternInterface.Invoke);
        invokeProvider.Should().NotBeNull();
        invokeProvider!.Invoke();
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private sealed class FakeAuthService : IAuthService
    {
        private readonly IReadOnlyList<BranchDto> _branches;
        private readonly IReadOnlyDictionary<int, IReadOnlyList<UserDto>> _users;
        private readonly Func<LoginRequest, Task<AuthResult>> _login;

        public FakeAuthService(
            IReadOnlyList<BranchDto> branches,
            IReadOnlyDictionary<int, IReadOnlyList<UserDto>> users,
            Func<LoginRequest, Task<AuthResult>>? login = null)
        {
            _branches = branches;
            _users = users;
            _login = login ?? (_ => Task.FromResult(new AuthResult(false, "فشل تسجيل الدخول.", null)));
        }

        public List<int> RequestedBranchIds { get; } = [];
        public LoginRequest? LastLoginRequest { get; private set; }
        public int LogoutCount { get; private set; }

        public Task<IReadOnlyList<BranchDto>> GetActiveBranchesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_branches);

        public Task<IReadOnlyList<UserDto>> GetUsersForBranchAsync(int branchId, CancellationToken cancellationToken = default)
        {
            RequestedBranchIds.Add(branchId);
            return Task.FromResult(_users.TryGetValue(branchId, out var users)
                ? users
                : (IReadOnlyList<UserDto>)Array.Empty<UserDto>());
        }

        public Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
        {
            LastLoginRequest = request;
            return _login(request);
        }

        public Task LogoutAsync(CancellationToken cancellationToken = default)
        {
            LogoutCount++;
            return Task.CompletedTask;
        }
    }

}

[CollectionDefinition("Login WPF", DisableParallelization = true)]
public sealed class LoginWpfCollection;
