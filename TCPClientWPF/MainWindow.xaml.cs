using Microsoft.Win32;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Remoting.Messaging;
using System.Security.Cryptography;
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
using System.Xml;
using TCPClientWPF;
using static System.Net.Mime.MediaTypeNames;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace TCPClientWPF
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private TcpClient tcpClient = null;
        private BinaryReader binaryReader = null;
        private BinaryWriter binaryWriter = null;
        private Message message = null;

        private Thread connectionThread = null;
        private ManualResetEventSlim requestAgainFlag = null;
        private CancellationTokenSource cancellationTokenSource = null;

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void BConnect_Click(object sender, RoutedEventArgs e)
        {
            buttonConnect.IsEnabled = false;
            if (tcpClient != null) {
                Disconnect();
                buttonConnect.IsEnabled = true;
                return;
            }
            sbTextStatus.Content = sbTextServerInfo.Content = "";
            string serverIP = tboxServerIP.Text;
            string strServerPort = tboxServerPort.Text;
            string clientIP = tboxClientIP.Text;
            string strClientPort = tboxClientPort.Text;

            requestAgainFlag = new ManualResetEventSlim(false);
            cancellationTokenSource = new CancellationTokenSource();

            if (!CheckIPPort(serverIP, strServerPort))
            {
                MessageBox.Show("The wrong server IP address or port. Try again.");
            }
            else if (!CheckIPPort(clientIP, strClientPort))
            {
                MessageBox.Show("The wrong client IP address or port. Try again.");
            } else 
            {
                sbTextStatus.Content = "Connecting...";
                sbTextServerInfo.Content = serverIP + ":" + strServerPort;
                int serverPort = int.Parse(strServerPort);
                int clientPort = int.Parse(strClientPort);

                IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Parse(clientIP), clientPort);
                try
                {
                    tcpClient = new TcpClient(localEndPoint);
                    await tcpClient.ConnectAsync(IPAddress.Parse(serverIP), serverPort);

                    connectionThread = new Thread(() =>
                    {
                        Connect(serverIP, serverPort);
                        Disconnect();
                    });
                    connectionThread.Name = "_Client connection";
                    connectionThread.IsBackground = true;
                    connectionThread.Start();
                }
                catch (Exception ex) {
                    MessageBox.Show("An error while connecting, try again in two minutes later or change the Port:\n" + ex.Message);
                }
            }

            if (tcpClient != null && tcpClient.Connected) {
                buttonConnect.Content = "Disconnect";
                sbTextStatus.Content = "Connected";
            } else {
                buttonConnect.Content = "Refresh";
                sbTextStatus.Content = "Connection error";
            }
            buttonConnect.IsEnabled = true;
        }

        private void Connect(string serverIP, int serverPort)
        {
            try
            {
                using (NetworkStream stream = tcpClient.GetStream())
                using (binaryReader = new BinaryReader(stream))
                using (binaryWriter = new BinaryWriter(stream))
                {
                    while (tcpClient != null && tcpClient.Connected &&
                            !cancellationTokenSource.IsCancellationRequested)
                    {
                        if (requestAgainFlag.IsSet || message == null)
                        {
                            binaryWriter.Write(true);
                            requestAgainFlag.Reset();
                            this.Dispatcher.Invoke(new Action(() => { buttonRequest.IsEnabled = true; }));
                        }
                        else
                        {
                            binaryWriter.Write(false);
                        }
                        binaryWriter.Flush();

                        // check if the server is disconnect
                        byte[] checkConnection = new byte[1];
                        if (tcpClient.Client.Receive(checkConnection, SocketFlags.Peek) == 0) break;

                        // final desision from the server
                        bool recieveFlag = binaryReader.ReadBoolean();
                        if (recieveFlag)
                        {
                            GetMessage();
                            this.Dispatcher.Invoke(new Action(() => DisplayMessage()));
                        }
                        Task.Delay(50).Wait();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("An error while connecting in the main cycle, try again later :\n" + ex.Message);
            }
        }

        private void RequestAgain_Click(object sender, RoutedEventArgs e)
        {
            if (tcpClient == null || !tcpClient.Connected) return;

            buttonRequest.IsEnabled = false;
            requestAgainFlag.Set();
        }

        private void DisplayMessage()
        {
            if (message == null) return;
            
            tblockMessageInfo.Text = "Message info:\n";
            tblockMessageInfo.Text += "from : " + message.From + "\n";
            tblockMessageInfo.Text += "time : " + DateTime.Now.ToLongTimeString();

            var color = (Color)ColorConverter.ConvertFromString(message.TextColor);
            SolidColorBrush brush = new SolidColorBrush(color);
            rtMessage.Foreground = brush;
            rtMessage.Document.Blocks.Clear();
            rtMessage.AppendText(message.Text);

            BitmapImage bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.UriSource = new Uri(message.ImagePath);
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.EndInit();
            imMessage.Source = bitmapImage;

            mainWindow.Height = Math.Max(mainWindow.ActualHeight, 235 + rtMessage.ActualHeight + imMessage.ActualHeight);
            mainWindow.Width = Math.Max(mainWindow.ActualWidth, 210 + imMessage.ActualWidth);
        }

        private void GetMessage()
        {
            if (message == null) message = new Message();

            message.From = binaryReader.ReadString();
            message.Text = binaryReader.ReadString();
            message.TextColor = binaryReader.ReadString();
            message.Image = binaryReader.ReadString();
            message.ImagePath = Directory.GetCurrentDirectory() + "\\image.bmp";
            byte[] imageData = Convert.FromBase64String(message.Image);
            using (MemoryStream ms = new MemoryStream(imageData))
            {
                System.Drawing.Image image = System.Drawing.Bitmap.FromStream(ms);
                image.Save(message.ImagePath, System.Drawing.Imaging.ImageFormat.Bmp);
                image.Dispose();
            }
        }

        private bool CheckIPPort(string strIP, string strPort)
        {
            bool parseIPCod = IPAddress.TryParse(strIP, out IPAddress ip);
            bool parsePortCod = int.TryParse(strPort, out int port);
            return parseIPCod && parsePortCod && 1024 <= port && port <= 49151;
        }

        private async void Disconnect()
        {
            if (tcpClient != null) {
                try
                {
                    if (tcpClient.Connected)
                    {
                        cancellationTokenSource.Cancel(); // send request to close the thread
                        await Task.Delay(1000);
                        connectionThread = null;
                        message = null;
                    }
                } catch (Exception ex) {
                    MessageBox.Show("An error in the client while disconnecting :\n" + ex.Message);
                } finally {
                    tcpClient?.Close();
                    tcpClient?.Dispose();
                    tcpClient = null;
                }
            }
            this.Dispatcher.Invoke(new Action(() => {
                buttonConnect.Content = "Connect";
                sbTextStatus.Content = "Disconnected";
                buttonRequest.IsEnabled = true;
            }));
        }

        private void mainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Disconnect();
        }

        private class Message
        {
            public string From { get; set; }
            public string Text { get; set; }
            public string TextColor { get; set; }
            public string ImagePath { get; set; }
            public string Image { get; set; }
            public Message() { }

            public void ResetMessage()
            {
                From = Text = TextColor = ImagePath = null;
            }
        }
    }
}
