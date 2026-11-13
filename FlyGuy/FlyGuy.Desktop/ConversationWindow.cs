using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;
using Panel = System.Windows.Controls.Panel;
using MessageBox = System.Windows.MessageBox;

namespace FlyGuy.Desktop;

internal sealed class ConversationWindow : Window
{
    private readonly FlyConversation conversation;
    private readonly Button listen = new() { Padding = new Thickness(12,6,12,6) };
    private readonly TextBox phrase = new() { MaxLength = 200, Margin = new Thickness(0,6,0,6) };
    private readonly TextBox reply = new() { MaxLength = 24, Margin = new Thickness(0,6,0,6) };
    private readonly TextBlock feedback = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,12,0,12) };
    private readonly TextBlock memory = new() { TextWrapping = TextWrapping.Wrap };
    private string lastHeard = "";

    public ConversationWindow(FlyConversation conversation)
    {
        this.conversation = conversation;
        Title = "Fly Guy - talk and teach"; Width = 560; Height = 700; MinWidth = 400; MinHeight = 400;
        CaptureExclusion.Register(this);
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock { Text = "Talk to your fly", FontSize = 25 });
        root.Children.Add(new TextBlock { Text = "Start listening to use your default microphone. Recognition runs locally; audio is not saved or uploaded. Learned words and phrases are saved privately for your Windows account. Closing this panel stops the microphone.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,12,0,12) });
        listen.Click += (_,_) => { if (conversation.IsListening) conversation.StopListening(); else conversation.StartListening(); };
        root.Children.Add(listen);
        root.Children.Add(feedback);
        root.Children.Add(new TextBlock { Text = "Heard phrase (or type here)" }); root.Children.Add(phrase);
        System.Windows.Automation.AutomationProperties.SetName(phrase, "Heard phrase");
        System.Windows.Automation.AutomationProperties.SetName(reply, "Teach reply");
        AddButton(root, "Say this to the fly", () => conversation.Hear(phrase.Text));
        var actions = new CheckBox { Content = "Enable desktop actions for this session", IsChecked = conversation.Actions.Enabled, Margin = new Thickness(0,12,0,6) };
        actions.Checked += (_,_) => conversation.Actions.Enabled = true;
        actions.Unchecked += (_,_) => conversation.Actions.Enabled = false;
        root.Children.Add(actions);
        var autonomous = new CheckBox { Content = "Let the fly act on its own (this session)", IsChecked = conversation.Actions.AutonomousEnabled, Margin = new Thickness(0,6,0,6) };
        autonomous.Checked += (_,_) => conversation.Actions.AutonomousEnabled = true;
        autonomous.Unchecked += (_,_) => conversation.Actions.AutonomousEnabled = false;
        root.Children.Add(autonomous);
        root.Children.Add(new TextBlock { Text = "After 30 seconds, a curious, calm fly may open Notepad once per session. An alarmed fly may nudge a nearby cursor. Autonomous attempts are at least 60 seconds apart; pause stops them. This toggle stays active when you close this panel.", TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new TextBlock { Text = "Type 'open notepad' or 'move cursor away', or use the buttons below. Actions require typed input, respect pause/mute, and have a five-second cooldown. Cursor movement never clicks or holds a button.", TextWrapping = TextWrapping.Wrap });
        AddButton(root, "Open Notepad", () => conversation.Hear("open notepad"));
        AddButton(root, "Move cursor away", () => conversation.Hear("move cursor away"));
        root.Children.Add(new TextBlock { Text = "Teach a one-word reply", Margin = new Thickness(0,18,0,0) }); root.Children.Add(reply);
        AddButton(root, "Remember this reply", () => conversation.Teach(phrase.Text, reply.Text));
        root.Children.Add(new TextBlock { Text = "Try saying ‘hello’ or ‘say apple’. Otherwise it learns associations between what you say and how it feels. It keeps replies to one word. This is simple associative learning, so use the teaching control to correct a reply.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,12,0,12) });
        root.Children.Add(memory);
        AddButton(root, "Clear learned words and phrases", () =>
        {
            if (MessageBox.Show(this, "Forget all learned words, phrases and replies?", "Clear language memory", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                conversation.Clear();
        });
        Content = new ScrollViewer { Content = root };
        conversation.Changed += Refresh;
        Closed += (_,_) => { conversation.Actions.Enabled = false; conversation.StopListening(); conversation.Changed -= Refresh; };
        Refresh();
    }
    private void Refresh()
    {
        listen.Content = conversation.IsListening ? "Stop listening" : "Start listening";
        feedback.Text = conversation.Status;
        if (lastHeard != conversation.LastHeard) { lastHeard = conversation.LastHeard; phrase.Text = lastHeard; }
        var state = conversation.Memory.Export();
        memory.Text = $"Memory: {state.Words.Count} words · {state.Phrases.Length} phrases\n" + string.Join(" · ", state.Words.OrderByDescending(w => w.Value).ThenBy(w => w.Key).Take(40).Select(w => w.Key));
    }
    private static void AddButton(Panel root, string text, Action action)
    {
        var button = new Button { Content = text, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Padding = new Thickness(12,6,12,6), Margin = new Thickness(0,4,0,4) };
        button.Click += (_,_) => action(); root.Children.Add(button);
    }
}
