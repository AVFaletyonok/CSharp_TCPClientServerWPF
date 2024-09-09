using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Remoting.Messaging;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Xml;
using System.Xml.Linq;
using static System.Net.Mime.MediaTypeNames;

namespace TCPServerWPF
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private TcpListener tcpListener = null;
        private Message message = null;
        private Dictionary<string, TcpClient> dictTcpClients = null; // string - IP:Port

        private Thread serverThread = null;
        private Dictionary<string, Thread> dictThreadsConnections = null;
        private ManualResetEventSlim responseFlag = null;
        private CancellationTokenSource cancellationTokenSource = null;
        private HashSet<int> setDoneThreads = null;

        public MainWindow()
        {
            InitializeComponent();
            responseFlag = new ManualResetEventSlim(false);
        }

        private void BStartServer_Click(object sender, RoutedEventArgs e)
        {
            buttonStartServer.IsEnabled = false;
            if (tcpListener != null) {
                StopServer();
                buttonStartServer.IsEnabled = true;
                return;
            }

            string serverIP = tboxIP.Text;
            string serverPort = tboxPort.Text;
            if (!checkIPPort(serverIP, serverPort)) return;

            IPAddress localAddr = IPAddress.Parse(serverIP);
            int port = int.Parse(serverPort);

            dictTcpClients = new Dictionary<string, TcpClient>();
            dictThreadsConnections = new Dictionary<string, Thread>();
            setDoneThreads = new HashSet<int>();
            cancellationTokenSource = new CancellationTokenSource();

            // Launch the main server thread
            serverThread = new Thread(() => StartServer(localAddr, port));
            serverThread.Name = "_Server main cycle";
            serverThread.IsBackground = true;
            serverThread.Start();
        }

        private void StartServer(IPAddress localAddr, int port)
        {
            try {
                tcpListener = new TcpListener(localAddr, port);
                tcpListener.Start(5);
                this.Dispatcher.Invoke(new Action(() => {
                    sbTextServerStatus.Content = tcpListener == null ? "Startup error" : "Server is on";
                    buttonStartServer.Content = tcpListener == null ? "Refresh" : "Stop Server";
                    buttonStartServer.IsEnabled = true;
                }));

                Thread thread = null;
                while (tcpListener != null)
                {
                    TcpClient tcpClient = tcpListener.AcceptTcpClient();
                    string clientIPPort = (tcpClient?.Client.RemoteEndPoint as IPEndPoint)?.ToString();

                    if (dictTcpClients.ContainsKey(clientIPPort))
                    {
                        dictTcpClients[clientIPPort] = tcpClient;
                    } else {
                        dictTcpClients.Add(clientIPPort, tcpClient);
                    }
                    // TO DO : Change to list, add list on the window
                    this.Dispatcher.Invoke(new Action(() => {
                        if (sbTextClientInfo.Text.Length != 0)
                            sbTextClientInfo.Text += ", ";
                        sbTextClientInfo.Text += clientIPPort;
                    }));

                    thread = new Thread(() => { CreateNewConnection(tcpClient, clientIPPort); });
                    thread.IsBackground = true;
                    thread.Name = "_Server client thread : " + clientIPPort;
                    thread.Start();
                    Task.Delay(50).Wait();
                }
            } catch (SocketException ex) when (ex.ErrorCode == 10004) {
                return;
            } catch (Exception ex) {
                MessageBox.Show("An error in the server :\n" + ex.Message);
            }
        }

        private void CreateNewConnection(TcpClient tcpClient, string clientIPPort)
        {
            try
            {
                using (NetworkStream stream = tcpClient?.GetStream())
                using (BinaryReader binaryReader = new BinaryReader(stream))
                using (BinaryWriter binaryWriter = new BinaryWriter(stream))
                {
                    while (!cancellationTokenSource.IsCancellationRequested)
                    {
                        // check if the client is disconnect
                        byte[] checkConnection = new byte[1];
                        if (tcpClient.Client.Receive(checkConnection, SocketFlags.Peek) == 0) break;

                        // service messages :
                        // request from the client
                        bool reqClientFlag = binaryReader.ReadBoolean();
                        // request from this server thread
                        bool sendFlag = false;
                        if (message != null && (reqClientFlag || (responseFlag.IsSet &&
                            !setDoneThreads.Contains(Thread.CurrentThread.ManagedThreadId)))) sendFlag = true;

                        binaryWriter.Write(sendFlag);
                        binaryWriter.Flush();

                        if (sendFlag)
                        {
                            SendMessage(binaryWriter);
                            if (responseFlag.IsSet) setDoneThreads.Add(Thread.CurrentThread.ManagedThreadId);
                        }
                        Task.Delay(50).Wait();
                    }
                }
            }
            catch (SocketException se)
            {
                MessageBox.Show("Client " + clientIPPort + " is disconnected\n" + se.Message);
            }
            catch (Exception ex)
            {
                MessageBox.Show("An error in connection with a client " + clientIPPort + "\n" + ex.Message);
            }
            finally
            {
                if (tcpClient != null) DisconnectClient(tcpClient, clientIPPort);
            }
        }

        private async void Load_Click(object sender, RoutedEventArgs e)
        {
            buttonLoadMsg.IsEnabled = false;
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "(*.xml)|*.xml|All files (*.*)|*.*";

            if (ofd.ShowDialog() != true) return;
            if (!ofd.CheckFileExists) return;

            XmlDocument xDoc = new XmlDocument();
            xDoc.Load(ofd.FileName);
            if (xDoc == null) return;

            if (message == null) message = new Message();

            await Task.Run(() => ParseXML(xDoc));
            DisplayMessage(ofd);

            responseFlag.Set();
            setDoneThreads?.Clear();

            if (tcpListener != null && message != null &&
                dictTcpClients != null && dictTcpClients.Count > 0)
            {
                await Task.Factory.StartNew(async () => 
                {
                    while(responseFlag.IsSet)
                    { 
                        if (setDoneThreads.Count == dictTcpClients?.Count) responseFlag.Reset();
                        await Task.Delay(100, cancellationTokenSource.Token);
                    }
                });
            }
            buttonLoadMsg.IsEnabled = true;
        }

        private void DisplayMessage(OpenFileDialog ofd)
        {
            if (message == null) return;

            tblockMessageInfo.Text = "Message info:\n";
            tblockMessageInfo.Text += "FormatVersion : " + message.FormatVersion + "\n";
            tblockMessageInfo.Text += "from : " + message.From + "\n";
            tblockMessageInfo.Text += "to : " + message.To + "\n";
            tblockMessageInfo.Text += "id : " + message.Id + "\n";
            Color color = (Color)ColorConverter.ConvertFromString(message.TextColor);
            SolidColorBrush brush = new SolidColorBrush(color);
            rtMessage.Foreground = brush;
            rtMessage.Document.Blocks.Clear();
            rtMessage.AppendText(message.Text);

            string imagePath = ofd.FileName.Replace(ofd.SafeFileName, "") + "image.bmp";
            byte[] imageData = Convert.FromBase64String(message.Image);
            using (MemoryStream ms = new MemoryStream(imageData))
            {
                System.Drawing.Image image = System.Drawing.Bitmap.FromStream(ms);
                image.Save(imagePath, System.Drawing.Imaging.ImageFormat.Bmp);
                image.Dispose();
            }

            BitmapImage bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.UriSource = new Uri(imagePath);
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.EndInit();
            imMessage.Source = bitmapImage;

            mainWindow.Height = Math.Max(mainWindow.ActualHeight, 235 + rtMessage.ActualHeight + imMessage.ActualHeight);
            mainWindow.Width = Math.Max(mainWindow.ActualWidth, 210 + imMessage.ActualWidth);
        }

        private bool ParseXML(XmlDocument xDoc)
        {
            XmlElement xRoot = xDoc.DocumentElement;
            if (xRoot == null) return false;

            if (message == null) message = new Message();
            XmlNode messageNode = xDoc.GetElementsByTagName("Message")?[0];
            string tempStr = messageNode?.Attributes["FormatVersion"]?.Value;
            message.FormatVersion = tempStr;
            if (tempStr != null) {
                tempStr = messageNode?.Attributes["from"]?.Value;
                message.From = tempStr;
            }
            if (tempStr != null) {
                tempStr = messageNode?.Attributes["to"]?.Value;
                message.To = tempStr;
            }
            if (tempStr != null) {
                tempStr = xDoc.GetElementsByTagName("msg")?[0].Attributes["id"]?.Value;
                message.Id = tempStr;
            }
            XmlNode textNode = xDoc.GetElementsByTagName("text")?[0];
            if (tempStr != null) {
                tempStr = textNode?.Attributes["color"]?.Value;
                message.TextColor = "#" + tempStr;
            }
            if (tempStr != null) {
                tempStr = textNode?.InnerText;
                message.Text = tempStr;
            }
            if (tempStr != null) {
                tempStr = xDoc.GetElementsByTagName("image")?[0]?.InnerText;
                message.Image = tempStr;
            }

            if (tempStr == null) message.ResetMessage();
            return tempStr != null;
        }

        private bool checkIPPort(string strIP, string strPort)
        {
            bool parseIPCod = IPAddress.TryParse(strIP, out IPAddress ip);
            bool parsePortCod = int.TryParse(strPort, out int port);
            if (!(parseIPCod && parsePortCod && 1024 <= port && port <= 49151))
            {
                MessageBox.Show("The wrong IP address or port. Try again.");
                return false;
            } 
            return true;
        }

        private void SendMessage(BinaryWriter binaryWriter)
        {
            binaryWriter.Write(message.From);
            binaryWriter.Write(message.Text);
            binaryWriter.Write(message.TextColor);
            binaryWriter.Write(message.Image);
            binaryWriter.Flush();
        }

        private void mainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            StopServer();
        }

        private void DisconnectClient(TcpClient tcpClient, string clientIPPort)
        {
            tcpClient?.Close();
            dictTcpClients.Remove(clientIPPort);
            this.Dispatcher.Invoke(new Action(() => {
                try {
                    sbTextClientInfo.Text = sbTextClientInfo.Text.Replace(", " + clientIPPort, "");
                    sbTextClientInfo.Text = sbTextClientInfo.Text.Replace(clientIPPort + ", ", "");
                    sbTextClientInfo.Text = sbTextClientInfo.Text.Replace(clientIPPort, "");
                } catch { }
            }));
        }

        private void StopServer()
        {
            responseFlag.Reset();
            if (cancellationTokenSource != null) cancellationTokenSource.Cancel();
            //Task.Delay(1000).Wait();
            // TO DO :
            // wait for all threads are close

            try {
                if (dictTcpClients != null)
                {
                    string clientIPPort = "";
                    foreach (TcpClient tcpClient in dictTcpClients.Values)
                    {
                        if (tcpClient != null)
                        {
                            Socket clientSocker = tcpClient.Client;
                            EndPoint clientEndPoint = null;
                            if (clientSocker != null) clientEndPoint = tcpClient?.Client?.RemoteEndPoint;
                            if (clientEndPoint != null)
                            {
                                clientIPPort = (clientEndPoint as IPEndPoint)?.ToString();
                                DisconnectClient(tcpClient, clientIPPort);
                            }
                        }
                    }
                }
                dictTcpClients = null;
                if (dictThreadsConnections != null)
                {
                    foreach (Thread thread in dictThreadsConnections.Values)
                    {
                        thread.Abort();
                    }
                    dictThreadsConnections = null;
                }
            } catch { }
            

            //Task.Delay(1000).Wait();
            if (tcpListener != null) {
                tcpListener.Stop();
                tcpListener = null;
            }
            
            sbTextServerStatus.Content = "Server is off";
            buttonStartServer.Content = "Start Server";
            sbTextClientInfo.Text = "";
        }

        private class Message
        {
            public string FormatVersion { get; set; }
            public string From { get; set; }
            public string To { get; set; }
            public string Id { get; set; }
            public string Text { get; set; }
            public string TextColor { get; set; }
            public string Image {  get; set; }
            public Message() { }
            
            public void ResetMessage()
            {
                FormatVersion = From = To = Id = Text = TextColor = Image = null;
            }
        }
    }
}
