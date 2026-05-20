using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace ChatMessages
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private TcpClient _client;
        private StreamReader _reader;
        private StreamWriter _writer;
        private CancellationTokenSource _receiveCts;

        public MainWindow()
        {
            InitializeComponent();
            UpdateUiState(false);
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_client != null)
            {
                await DisconnectAsync();
                return;
            }

            var userName = UserNameTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(userName))
            {
                MessageBox.Show("Please enter a username.", "Chat", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync("127.0.0.1", 5000);

                var stream = _client.GetStream();
                _reader = new StreamReader(stream);
                _writer = new StreamWriter(stream) { AutoFlush = true };
                _receiveCts = new CancellationTokenSource();

                await _writer.WriteLineAsync(userName);
                MessagesListBox.Items.Add($"Connected as {userName}.");
                UpdateUiState(true);

                _ = ReceiveLoopAsync(_receiveCts.Token);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to connect: {ex.Message}", "Chat", MessageBoxButton.OK, MessageBoxImage.Error);
                await DisconnectAsync();
            }
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            if (_writer == null)
            {
                return;
            }

            var message = MessageTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            try
            {
                await _writer.WriteLineAsync(message);
                MessageTextBox.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to send: {ex.Message}", "Chat", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var line = await _reader.ReadLineAsync();
                    if (line == null)
                    {
                        break;
                    }

                    await Dispatcher.InvokeAsync(() => MessagesListBox.Items.Add(line));
                }
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() => MessageBox.Show($"Receive error: {ex.Message}", "Chat", MessageBoxButton.OK, MessageBoxImage.Error));
            }

            await Dispatcher.InvokeAsync(async () => await DisconnectAsync());
        }

        private async Task DisconnectAsync()
        {
            _receiveCts?.Cancel();
            _receiveCts = null;

            if (_writer != null)
            {
                await _writer.FlushAsync();
            }

            _reader?.Dispose();
            _writer?.Dispose();
            _client?.Close();

            _reader = null;
            _writer = null;
            _client = null;

            UpdateUiState(false);
        }

        private void UpdateUiState(bool isConnected)
        {
            ConnectButton.Content = isConnected ? "Disconnect" : "Connect";
            SendButton.IsEnabled = isConnected;
            MessageTextBox.IsEnabled = isConnected;
            UserNameTextBox.IsEnabled = !isConnected;
            StatusTextBlock.Text = isConnected ? "Connected" : "Disconnected";
            StatusTextBlock.Foreground = isConnected ? System.Windows.Media.Brushes.DarkGreen : System.Windows.Media.Brushes.DarkRed;
        }

        protected override async void OnClosed(EventArgs e)
        {
            await DisconnectAsync();
            base.OnClosed(e);
        }
    }
}
