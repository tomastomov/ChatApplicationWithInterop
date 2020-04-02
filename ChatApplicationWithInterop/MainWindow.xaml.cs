using DOT.AGM;
using DOT.AGM.Client;
using DOT.AGM.Core;
using DOT.AGM.Server;
using DOT.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Tick42;

namespace ChatApplicationWithInterop
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    ///
    public partial class MainWindow : Window
    {
        private const string SEND_MESSAGE = "SendMessage";
        private const string GET_CURRENT_USERS = "GetCurrentUsers";
        private const string OPEN_WINDOW = "OpenWindow";

        private Glue42 glue;
        public ObservableCollection<ComboBoxItem> cbItems { get; set; }
        public ComboBoxItem SelectedcbItem { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            cbItems = new ObservableCollection<ComboBoxItem>();
        }

        public void RegisterGlue(Glue42 glue)
        {
            this.glue = glue;

            this.RegisterSendMessageMethod();
            this.RegisterGetCurrentUsersMethod();
            this.RegisterOpenNewWindowMethod();

            this.glue.Interop.Invoke(GET_CURRENT_USERS, mib => mib.SetContext().SkipInvocationMetrics().SetInvocationLoggingLevel(LogLevel.Info), new TargetSettings().WithTargetInvokeTimeout(TimeSpan.FromSeconds(8)).WithTargetType(MethodTargetType.All));
        }

        private void RegisterOpenNewWindowMethod()
        {
            IServerMethod method = this.glue.Interop.RegisterEndpoint(mdb => mdb.SetMethodName(OPEN_WINDOW),
    (meth, context, caller, resultBuilder, asyncResponseCallback, cookie) =>
        ThreadPool.QueueUserWorkItem(_ =>
        {
            Value roomId = context.Arguments.First(cv => cv.Name == "id").Value;

            // can it be done without dispatcher

            this.Dispatcher.Invoke(() =>
            {
                OpenNewChatRoom(roomId);
            });

            asyncResponseCallback(resultBuilder.SetMessage(roomId + " opened").
                SetContext(cb => cb.AddValue("id", roomId)).Build());
        }));
        }

        private async void OnSendPrivateMessage(object sender, RoutedEventArgs args)
        {
            var selectedUserId = SelectedcbItem?.Content.ToString();
            var currentUserId = this.glue.Identity.InstanceId;
            var firstRoomId = "_" + selectedUserId + "_" + currentUserId;

            await this.glue.Interop.Invoke(OPEN_WINDOW, mib => mib.SetContext(c => c.AddValue("id", firstRoomId)).SkipInvocationMetrics().SetInvocationLoggingLevel(LogLevel.Info), new TargetSettings().WithTargetInvokeTimeout(TimeSpan.FromSeconds(8)).WithTargetSelector((method, instance) => instance.InstanceId == selectedUserId));

            var secondRoomId = "_" + currentUserId + "_" + selectedUserId;

            await this.glue.Interop.Invoke(OPEN_WINDOW, mib => mib.SetContext(c => c.AddValue("id", secondRoomId)).SkipInvocationMetrics().SetInvocationLoggingLevel(LogLevel.Info), new TargetSettings().WithTargetInvokeTimeout(TimeSpan.FromSeconds(8)).WithTargetSelector((method, instance) => instance.InstanceId == currentUserId));
        }

        private Grid GetCurrentGrid(string roomId)
        {
            if (roomId == "GlobalButton") return (Grid)Application.Current.MainWindow.Content;
            else
            {
               var grid = (Grid)Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.Name == roomId)?.Content;
                return grid;
            }
        }

        private T GetGridElement<T>(Grid grid)
        {
            foreach(var child in grid.Children)
            {
                if (child.GetType() == typeof(T)) return (T)child;
            }

            throw new Exception("No such element found");
        }

        private void RegisterGetCurrentUsersMethod()
        {
            IServerMethod method = this.glue.Interop.RegisterEndpoint(mdb => mdb.SetMethodName(GET_CURRENT_USERS),
    (meth, context, caller, resultBuilder, asyncResponseCallback, cookie) =>
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var endPoints = this.glue.Interop.GetTargetEndpoints();

            // can it be done without dispatcher

            this.Dispatcher.Invoke(() =>
            {
                foreach (var endPoint in endPoints)
                {
                    if(endPoint.Value.Any(m => m.Definition.Name == SEND_MESSAGE) && endPoint.Key.InstanceId != caller.InstanceId && endPoint.Key.InstanceId.Length == caller.InstanceId.Length)
                    {
                        this.cbItems.Add(new ComboBoxItem { Content = endPoint.Key.InstanceId });
                    }
                }
            });

            asyncResponseCallback(resultBuilder.SetMessage("Users processed").
                Build());
        }));
        }

        private void RegisterSendMessageMethod()
        {
            IServerMethod method = this.glue.Interop.RegisterEndpoint(mdb => mdb.SetMethodName(SEND_MESSAGE),
    (meth, context, caller, resultBuilder, asyncResponseCallback, cookie) =>
        ThreadPool.QueueUserWorkItem(_ =>
        {
            Value roomId = context.Arguments.First(cv => cv.Name == "id").Value;
            Value textMessage = context.Arguments.First(cv => cv.Name == "message").Value;

            this.Dispatcher.Invoke(() =>
            {
                var grid = this.GetCurrentGrid(roomId);
                var conversationBoxForCaller = this.GetGridElement<RichTextBox>(grid);
                conversationBoxForCaller.Document.Blocks.Add(new Paragraph(new Run($"{caller.InstanceId} said: {textMessage}")));
            });

            asyncResponseCallback(resultBuilder.SetMessage(roomId + " processed").
                SetContext(cb => cb.AddValue("id", roomId)).Build());
        }));
        }

        private async void OnSendMessage(object sender, RoutedEventArgs args)
        {
            var buttonName = (sender as Button).Name;

            var userIds = buttonName.Split('_');
            var roomId = new ContextValue("id", buttonName);
            var grid = this.GetCurrentGrid(buttonName);
            var message = this.GetGridElement<TextBox>(grid).Text;
            var textMessage = new ContextValue("message", message);

            
            if (userIds[0] == "GlobalButton")
            {
                await this.glue.Interop.Invoke(SEND_MESSAGE, mib => mib.SetContext(c => c.AddValues(textMessage, roomId)).SkipInvocationMetrics().SetInvocationLoggingLevel(LogLevel.Info), new TargetSettings().WithTargetInvokeTimeout(TimeSpan.FromSeconds(8)).WithTargetType(MethodTargetType.All));
            }
            else
            {
                var firstUserId = userIds[1];
                var secondUserId = userIds[2];
                var secondRoomId = "_" + secondUserId + "_" + firstUserId;
                var reverseRoom = new ContextValue("id", secondRoomId);

                // Can it be sent to both without invoking twice

                await this.glue.Interop.Invoke(SEND_MESSAGE, mib => mib.SetContext(c => c.AddValues(textMessage, roomId)).SkipInvocationMetrics().SetInvocationLoggingLevel(LogLevel.Info), new TargetSettings().WithTargetInvokeTimeout(TimeSpan.FromSeconds(8)).WithTargetSelector((method, instance) => instance.InstanceId == firstUserId));

                await this.glue.Interop.Invoke(SEND_MESSAGE, mib => mib.SetContext(c => c.AddValues(textMessage, reverseRoom)).SkipInvocationMetrics().SetInvocationLoggingLevel(LogLevel.Info), new TargetSettings().WithTargetInvokeTimeout(TimeSpan.FromSeconds(8)).WithTargetSelector((method, instance) => instance.InstanceId == secondUserId));
            }
      
        }

        private Grid CreateGrid(string roomId)
        {
            var grid = new Grid();

            var sendMessageButton = new Button
            {
                Width = 75,
                Margin = new Thickness
                {
                    Left = 690,
                    Top = 391,
                    Right = 0,
                    Bottom = 0
                },
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Content = "Send",
                Name = roomId
            };

            sendMessageButton.Click += OnSendMessage;

            var textMessageBox = new TextBox()
            {
                Margin = new Thickness
                {
                    Left = 10,
                    Top = 369,
                    Right = 0,
                    Bottom = 0
                },
                Height = 41,
                Width = 533,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Text = "Type a message..."
            };

            var richBox = new RichTextBox
            {
                Margin = new Thickness
                {
                    Left = 0,
                    Top = 0,
                    Right = -0.4,
                    Bottom = 0
                },
                Width = 794,
                Height = 364,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                IsReadOnly = true
            };

            grid.Children.Add(textMessageBox);
            grid.Children.Add(richBox);
            grid.Children.Add(sendMessageButton);

            return grid;
        }

        private void OpenNewChatRoom(string roomId)
        {
            var window = new Window();

            var grid = CreateGrid(roomId);

            window.Content = grid;

            window.Height = 450;
            window.Width = 800;
            window.Name = roomId;

            window.Show();

        }
    }
}
