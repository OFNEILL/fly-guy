using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FlyGuy.Messaging;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Brushes = System.Windows.Media.Brushes;
using Panel = System.Windows.Controls.Panel;
using TabControl = System.Windows.Controls.TabControl;
using CheckBox = System.Windows.Controls.CheckBox;
using System.Net.Http;

namespace FlyGuy.Desktop;

internal sealed class MessagingWindow : Window
{
    private readonly RelayConnection connection;
    private readonly TextBox server = new() { Text = "http://127.0.0.1:5187", Margin = new Thickness(0,4,0,10) };
    private readonly TextBox name = new() { Text = "Fly friend", MaxLength = 40, Margin = new Thickness(0,4,0,10) };
    private readonly PasswordBox invitation = new() { Margin = new Thickness(0,4,0,10) };
    private readonly PasswordBox adminKey = new() { Margin = new Thickness(0,4,0,10) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,10,0,10) };
    private readonly TextBlock adminStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,10,0,10) };
    private readonly System.Windows.Controls.ListBox users = new() { Height = 180, Margin = new Thickness(0,8,0,8) };
    private readonly TextBox message = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 500, Height = 110 };
    private readonly StackPanel composer = new() { Visibility = Visibility.Collapsed };
    private readonly System.Windows.Threading.DispatcherTimer refresh = new() { Interval = TimeSpan.FromSeconds(10) };
    private RelayClient? admin;
    private bool refreshing;
    public MessagingWindow(RelayConnection connection)
    {
        this.connection = connection;
        Title = "Fly Guy - messages"; Width = 560; Height = 790; MinWidth = 440; MinHeight = 500;
        System.Windows.Automation.AutomationProperties.SetName(server, "Relay address");
        System.Windows.Automation.AutomationProperties.SetName(name, "Display name");
        System.Windows.Automation.AutomationProperties.SetName(invitation, "Invitation key");
        System.Windows.Automation.AutomationProperties.SetName(adminKey, "Admin key");
        System.Windows.Automation.AutomationProperties.SetName(users, "Recipients");
        System.Windows.Automation.AutomationProperties.SetName(message, "Fly speech");
        CaptureExclusion.Register(this);
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock { Text = "Fly messages", FontSize = 24 });
        root.Children.Add(new TextBlock { Text = "Relay address" }); root.Children.Add(server);
        if (connection.Settings is { } settings) { server.Text = settings.Address; name.Text = settings.DisplayName; }
        var tabs = new TabControl();
        var receive = new StackPanel { Margin = new Thickness(12) };
        receive.Children.Add(new TextBlock { Text = "Connect to let your fly receive speech bubbles. You can disconnect at any time.", TextWrapping = TextWrapping.Wrap });
        receive.Children.Add(new TextBlock { Text = "Your display name", Margin = new Thickness(0,12,0,0) }); receive.Children.Add(name);
        receive.Children.Add(new TextBlock { Text = "Invitation key (leave blank to reconnect)" }); receive.Children.Add(invitation);
        AddButton(receive, "Use local invitation", () => Run(async () =>
        {
            RequireLocalAddress();
            var keys = RelayConnection.LocalKeys() ?? throw new ArgumentException("Start the local relay first using scripts/Start-Messaging.ps1.");
            invitation.Password = RelayConnection.Unprotect(keys.ProtectedInviteKey); await Task.CompletedTask;
        }, status));
        AddButton(receive, "Connect / receive messages", () => Run(async () =>
        {
            await connection.Connect(server.Text, name.Text, invitation.Password); invitation.Clear();
        }, status));
        AddButton(receive, "Disconnect", () => Run(() => { connection.Disconnect(); return Task.CompletedTask; }, status));
        receive.Children.Add(status);
        tabs.Items.Add(new TabItem { Header = "Receive messages", Content = receive });
        var control = new StackPanel { Margin = new Thickness(12) };
        control.Children.Add(new TextBlock { Text = "Admin access", FontSize = 19 });
        control.Children.Add(new TextBlock { Text = "Only the owner key can list users and send messages. Invitation and recipient keys do not grant access.", TextWrapping = TextWrapping.Wrap });
        control.Children.Add(new TextBlock { Text = "Owner key" }); control.Children.Add(adminKey);
        AddButton(control, "Use local owner key", () => Run(async () =>
        {
            RequireLocalAddress();
            var keys = RelayConnection.LocalKeys() ?? throw new ArgumentException("Start the local relay first using scripts/Start-Messaging.ps1.");
            adminKey.Password = RelayConnection.Unprotect(keys.ProtectedAdminKey); await Task.CompletedTask;
        }, adminStatus));
        AddButton(control, "Sign in as admin", () => Run(async () =>
        {
            var candidate = new RelayClient(server.Text, adminKey.Password);
            try
            {
                var roster = await candidate.Users();
                admin?.Dispose(); admin = candidate; adminKey.Clear();
                composer.Visibility = Visibility.Visible; ShowUsers(roster); refresh.Start();
                adminStatus.Text = "Signed in. Choose a recipient.";
            }
            catch { candidate.Dispose(); throw; }
        }, adminStatus));
        AddButton(control, "Sign out", () => { SignOut(); adminStatus.Text = "Signed out."; });
        composer.Children.Add(new TextBlock { Text = "Users (refreshes every 10 seconds)" }); composer.Children.Add(users);
        composer.Children.Add(new TextBlock { Text = "What should their fly say? (500 characters maximum)" }); composer.Children.Add(message);
        AddButton(composer, "Send as the fly", () => Run(async () =>
        {
            if (users.SelectedItem is not UserRow selected || admin is null) throw new ArgumentException("Choose a user first.");
            if (string.IsNullOrWhiteSpace(message.Text)) throw new ArgumentException("Write a message first.");
            var selectedUser = selected.User;
            await admin.Send(selectedUser.Id, message.Text);
            message.Clear();
            adminStatus.Text = $"Queued for {selectedUser.DisplayName}. Their fly will speak when connected.";
            await RefreshUsers();
        }, adminStatus));
        control.Children.Add(composer); control.Children.Add(adminStatus);
        tabs.Items.Add(new TabItem { Header = "Admin", Content = control });
        root.Children.Add(tabs); Content = new ScrollViewer { Content = root };
        connection.StatusChanged += UpdateStatus;
        UpdateStatus();
        refresh.Tick += (_,_) => Run(RefreshUsers, adminStatus);
        server.TextChanged += (_,_) => SignOut();
        Closed += (_,_) => { connection.StatusChanged -= UpdateStatus; SignOut(); };
    }
    private void RequireLocalAddress()
    {
        var address = RelayClient.ValidateAddress(server.Text);
        if (!address.IsLoopback || address.Port != 5187) throw new ArgumentException("Local keys are only for the local relay on port 5187.");
    }
    private void UpdateStatus() => status.Text = connection.Status;
    private void SignOut() { refresh.Stop(); admin?.Dispose(); admin = null; adminKey.Clear(); users.Items.Clear(); composer.Visibility = Visibility.Collapsed; }
    private void ShowUsers(RelayUser[] roster)
    {
        Guid? selected = (users.SelectedItem as UserRow)?.User.Id;
        users.Items.Clear();
        foreach (var user in roster)
        {
            var row = new UserRow(user); users.Items.Add(row);
            if (user.Id == selected) users.SelectedItem = row;
        }
    }
    private async Task RefreshUsers()
    {
        if (admin is null || refreshing) return;
        refreshing = true;
        try
        {
            var client = admin;
            var roster = await client.Users();
            if (ReferenceEquals(client, admin)) ShowUsers(roster);
        }
        catch (RelayException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized) { SignOut(); throw; }
        finally { refreshing = false; }
    }
    private static Button AddButton(Panel panel, string label, Action action)
    {
        var button = new Button { Content = label, Margin = new Thickness(0,4,0,4), Padding = new Thickness(10,6,10,6), HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        button.Click += (_,_) => action(); panel.Children.Add(button); return button;
    }
    private static async void Run(Func<Task> action, TextBlock feedback)
    {
        try { await action(); }
        catch (Exception ex) when (ex is ArgumentException or HttpRequestException or TaskCanceledException or RelayException or System.IO.IOException or System.UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or FormatException or System.Text.Json.JsonException or ObjectDisposedException)
        { feedback.Text = ex is HttpRequestException or TaskCanceledException ? "Cannot reach the relay. Check its address and that it is running." : ex.Message; }
    }
    private sealed record UserRow(RelayUser User)
    {
        public override string ToString() => $"{User.DisplayName} [{User.Id.ToString()[..8]}] - {(User.Online ? "online" : "offline")} - {User.PendingMessages} queued";
    }
}
