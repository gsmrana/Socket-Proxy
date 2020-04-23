using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Socket.Proxy;

namespace Socket_Proxy
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Data

        bool _appendTimestamp = true;
        bool _appendRequestTimespan = true;
        bool _appendResponseTimespan = true;
        bool _displayRawData = false;

        DateTime _lastReqTime;
        DateTime _lastRespTime;

        readonly List<SocketListener> _socketRoutes = new List<SocketListener>();
        readonly List<RouteListViewItem> _routeListBoxItems = new List<RouteListViewItem>();
        readonly DispatcherTimer _uiRefreshTimer = new DispatcherTimer();

        #endregion

        #region Data Maps

        readonly Dictionary<TunnelEvent, string> _tunnelEventSymbolMap = new Dictionary<TunnelEvent, string>
        {
            { TunnelEvent.ConnectingToRemote,     "----" },
            { TunnelEvent.TunnelOpened,           "o--o" },
            { TunnelEvent.TunnelClosed,           "x--x" },
            { TunnelEvent.ReceivedFromLocal,      ">---" },
            { TunnelEvent.UploadedToRemote,       "--->" },
            { TunnelEvent.ReceivedFromRemote,     "---<" },
            { TunnelEvent.DownloadedToLocal,      "<---" },
            { TunnelEvent.DisconnectedFromLocal,  "x--o" },
            { TunnelEvent.DisconnectedFromRemote, "o--x" },
            { TunnelEvent.Exception,              "xxxx" },
        };

        readonly Dictionary<TunnelEvent, SolidColorBrush> _textColorMap = new Dictionary<TunnelEvent, SolidColorBrush>
        {
            { TunnelEvent.ReceivedFromLocal, Brushes.DarkGreen },
            { TunnelEvent.UploadedToRemote, Brushes.DarkGreen },
            { TunnelEvent.ReceivedFromRemote, Brushes.BlueViolet },
            { TunnelEvent.DownloadedToLocal, Brushes.BlueViolet },
            { TunnelEvent.DisconnectedFromLocal, Brushes.Magenta },
            { TunnelEvent.DisconnectedFromRemote, Brushes.Magenta },
            { TunnelEvent.Exception, Brushes.Red },
        };

        #endregion

        #region ctor

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                RichTechBoxEvents.Foreground = Brushes.Blue;
                RichTechBoxEvents.FontFamily = new FontFamily("Consolas");
                RichTechBoxEvents.Document.PageWidth = 2500;
                ListBoxRoutes.ItemsSource = _routeListBoxItems;
                ListBoxRoutes.FontFamily = new FontFamily("Consolas");

                ComboBoxBindIp.Items.Add("0.0.0.0");
                ComboBoxBindIp.Items.Add("127.0.0.1");
                var hostentry = Dns.GetHostEntry(Dns.GetHostName());
                var localIps = hostentry.AddressList.Where(x => x.AddressFamily == AddressFamily.InterNetwork);
                foreach (var item in localIps) ComboBoxBindIp.Items.Add(item);
                ComboBoxBindIp.SelectedIndex = 0;

                ComboBoxRemoteIp.ItemsSource = new string[] { "127.0.0.1", "192.168.0.1" };
                ComboBoxRemoteIp.SelectedIndex = 0;

                _uiRefreshTimer.Interval = TimeSpan.FromSeconds(3);
                _uiRefreshTimer.Tick += UiRefreshTimer_Tick;
                _uiRefreshTimer.Start();
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        #endregion

        #region Internal Methods

        private void AppedEventLog(string text, SolidColorBrush color = null, bool appendNewLine = true)
        {
            if (color == null) color = Brushes.Black;
            if (appendNewLine) text += Environment.NewLine;
            Dispatcher.Invoke(() =>
            {
                var pos = RichTechBoxEvents.Document.ContentEnd;
                var tr = new TextRange(pos, pos)
                {
                    Text = text
                };
                tr.ApplyPropertyValue(TextElement.ForegroundProperty, color);
                RichTechBoxEvents.ScrollToEnd();
            });
        }

        private void AddRouteToListBox(SocketListener route)
        {
            var rlbvi = new RouteListViewItem
            {
                LocalEp = route.BindEndPoint.ToString(),
                RemoteEp = route.RemoteEndPoint.ToString()
            };

            Dispatcher.Invoke(() =>
            {
                _routeListBoxItems.Add(rlbvi);
                ListBoxRoutes.Items.Refresh();
                ListBoxRoutes.SelectedIndex = ListBoxRoutes.Items.Count - 1;
            });
        }

        private IPEndPoint ParseIPEndPoint(string ipEndPoint)
        {
            var element = ipEndPoint.Split(new[] { ":" }, StringSplitOptions.None);
            return new IPEndPoint(IPAddress.Parse(element[0]), int.Parse(element[1]));
        }

        private void PopupInfo(string message, string caption = "Info")
        {
            Dispatcher.Invoke(() =>
            {
                MessageBox.Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Information);
            });
        }

        private void PopupException(string message, string caption = "Exception")
        {
            Dispatcher.Invoke(() =>
            {
                MessageBox.Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        #endregion

        #region Menubar Events

        private void MenuItemOpen_Click(object sender, RoutedEventArgs e)
        {
            ToolBarButtonOpen_Click(sender, e);
        }

        private void MenuItemSave_Click(object sender, RoutedEventArgs e)
        {
            ToolBarButtonSave_Click(sender, e);
        }

        private void MenuItemExit_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void MenuItemAppnendTimestamp_Click(object sender, RoutedEventArgs e)
        {
            _appendTimestamp = MenuItemAppnendTimestamp.IsChecked;
        }

        private void MenuItemAppendReqTimespan_Click(object sender, RoutedEventArgs e)
        {
            _appendRequestTimespan = MenuItemAppendReqTimespan.IsChecked;
        }

        private void MenuItemAppendResTimespan_Click(object sender, RoutedEventArgs e)
        {
            _appendResponseTimespan = MenuItemAppendResTimespan.IsChecked;
        }

        private void MenuItemDisplayRawdata_Click(object sender, RoutedEventArgs e)
        {
            _displayRawData = MenuItemDisplayRawdata.IsChecked;
        }

        private void MenuItemUpdate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show("Do you want to browse release page?", "Confirmation",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                    Process.Start("https://github.com/gsmrana/Socket-Proxy/releases");
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        private void MenuItemAbout_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var aboutbox = new AboutBox();
                aboutbox.ShowDialog();
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        #endregion

        #region ToolBar Events

        private void ToolBarButtonOpen_Click(object sender, RoutedEventArgs e)
        {
            var routestr = "null";
            try
            {
                var ofd = new OpenFileDialog();
                ofd.Filter = "route map files (*.route)|*.route|All files (*.*)|*.*";
                ofd.FileName = "route_map.route";
                if (ofd.ShowDialog() == true)
                {
                    var lines = File.ReadAllLines(ofd.FileName);
                    this.Title = "Socket Proxy - " + System.IO.Path.GetFileName(ofd.FileName);
                    foreach (var routeold in _socketRoutes) routeold.Stop();
                    _socketRoutes.Clear();
                    _routeListBoxItems.Clear();
                    foreach (var line in lines)
                    {
                        routestr = line;
                        if (line.StartsWith("//")) continue; //ignore comment lines
                        var ep = line.Split(new[] { " <--> " }, StringSplitOptions.None);
                        var route = new SocketListener(ParseIPEndPoint(ep[0]), ParseIPEndPoint(ep[1]));
                        route.Id = _socketRoutes.Count;
                        route.OnSocketTunnelEvent += SocketRoute_OnSocketTunnelEvent;
                        route.Start();
                        AddRouteToListBox(route);
                        _socketRoutes.Add(route);
                    }
                }
            }
            catch (Exception ex)
            {
                PopupException(string.Format("Tunnel {0}\r\nError: {1}", routestr, ex.Message));
            }
        }

        private void ToolBarButtonSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sfd = new SaveFileDialog();
                sfd.Filter = "route map files (*.route)|*.route|All files (*.*)|*.*";
                sfd.FileName = "route_map.route";
                if (sfd.ShowDialog() == true)
                {
                    var lines = new List<string>();
                    foreach (var item in _socketRoutes)
                    {
                        var str = string.Format("{0} <--> {1}", item.BindEndPoint, item.RemoteEndPoint);
                        lines.Add(str);
                    }
                    File.WriteAllLines(sfd.FileName, lines.ToArray());
                }
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        private void ToolBarButtonCopyText_Click(object sender, RoutedEventArgs e)
        {
            RichTechBoxEvents.SelectAll();
            RichTechBoxEvents.Copy();
        }

        private void ToolBarButtonClear_Click(object sender, RoutedEventArgs e)
        {
            RichTechBoxEvents.Document.Blocks.Clear();
        }

        #endregion

        #region Form Button Events

        private void ButtonAdd_Click(object sender, RoutedEventArgs e)
        {
            SocketListener routenew = null;
            try
            {
                var bindip = IPAddress.Parse(ComboBoxBindIp.Text);
                var remoteip = IPAddress.None;
                if (!IPAddress.TryParse(ComboBoxRemoteIp.Text, out remoteip))
                    remoteip = Dns.GetHostEntry(ComboBoxRemoteIp.Text).AddressList.First();
                var bindEP = new IPEndPoint(bindip, int.Parse(TextBoxLocalPort.Text));
                var remoteEP = new IPEndPoint(remoteip, int.Parse(TextBoxRemotePort.Text));
                var route = new SocketListener(bindEP, remoteEP);
                routenew = route;
                route.OnSocketTunnelEvent += SocketRoute_OnSocketTunnelEvent;
                route.Start();
                AddRouteToListBox(route);
                _socketRoutes.Add(route);
            }
            catch (Exception ex)
            {
                PopupException(string.Format("Tunnel {0} <--> {1}\r\nError: {2}", routenew.BindEndPoint, routenew.RemoteEndPoint, ex.Message));
            }
        }

        private void ButtonRemove_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var index = ListBoxRoutes.SelectedIndex;
                if (index < 0)
                {
                    PopupInfo("No Route Selected!");
                    return;
                }
                var route = _socketRoutes[index];
                _socketRoutes.Remove(route);
                _routeListBoxItems.RemoveAt(index);
                ListBoxRoutes.Items.Refresh();
                route.Stop();
            }
            catch (Exception ex)
            {
                PopupException(ex.Message);
            }
        }

        #endregion

        #region Socket Events

        private void SocketRoute_OnSocketTunnelEvent(object sender, TunnelEventArg e)
        {
            try
            {
                var now = DateTime.Now;
                var textcolor = Brushes.Blue;
                var sb = new StringBuilder();
                if (_textColorMap.ContainsKey(e.Event))
                    textcolor = _textColorMap[e.Event];

                if (_appendTimestamp)
                {
                    sb.AppendFormat("{0:HH:mm:ss.fff} - ", now);
                }

                var timespanstr = "--:--.--- - ";
                if (e.ListenerId == 0 && e.TunnelId == 0) // for first one tunnel only
                {
                    if (e.Event == TunnelEvent.ConnectingToRemote)
                    {
                        _lastReqTime = _lastRespTime = now;
                    }
                    else if (e.Event == TunnelEvent.UploadedToRemote)
                    {
                        var ts = now.Subtract(_lastReqTime);
                        if (_appendRequestTimespan) timespanstr = string.Format("{0:00}:{1:00}.{2:000} - ", ts.Minutes, ts.Seconds, ts.Milliseconds);
                        _lastReqTime = _lastRespTime = now;
                    }
                    else if (e.Event == TunnelEvent.ReceivedFromRemote)
                    {
                        var ts = now.Subtract(_lastRespTime);
                        if (_appendResponseTimespan) timespanstr = string.Format("{0:00}:{1:00}.{2:000} - ", ts.Minutes, ts.Seconds, ts.Milliseconds);
                        _lastRespTime = now;
                    }
                }
                sb.AppendFormat(timespanstr);

                var symbol = "----";
                var datasizestr = "----";
                if (_tunnelEventSymbolMap.ContainsKey(e.Event)) symbol = _tunnelEventSymbolMap[e.Event];
                if (e.DataSize > 0) datasizestr = e.DataSize.ToString("X4");
                sb.AppendFormat("{0} {1} {2} #{3:X2} #{4:X2} {5} {6}", e.Source, symbol, e.Destination, e.ListenerId, e.TunnelId, datasizestr, e.Event);

                if (e.Event == TunnelEvent.Exception)
                {
                    sb.Append(" - " + e.ErrorMessage);
                }

                if (_displayRawData && e.Data != null)
                {
                    sb.Append(" - " + BitConverter.ToString(e.Data).Replace("-", ""));
                }

                AppedEventLog(sb.ToString(), textcolor);
            }
            catch (Exception ex)
            {
                AppedEventLog("Exception: " + ex.Message, Brushes.Red);
            }
        }

        #endregion

        #region Timer Event

        private void UiRefreshTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                for (int i = 0; i < _socketRoutes.Count; i++)
                {
                    _routeListBoxItems[i].TunnelCount = string.Format("{0}/{1}", _socketRoutes[i].ActiveTunnelsCount, _socketRoutes[i].TotalTunnelsCount);
                    _routeListBoxItems[i].UploadedBytes = _socketRoutes[i].UploadedBytes.ToString();
                    _routeListBoxItems[i].DownloadedBytes = _socketRoutes[i].DownloadedBytes.ToString();
                }
                ListBoxRoutes.Items.Refresh();
            }
            catch (Exception ex)
            {
                AppedEventLog("Exception: " + ex.Message, Brushes.Red);
            }
        }

        #endregion

    }

    #region Prop class

    public class RouteListViewItem
    {
        public int Id { get; set; }
        public string LocalEp { get; set; }
        public string RemoteEp { get; set; }
        public string TunnelCount { get; set; } = "0/0";
        public string UploadedBytes { get; set; } = "0";
        public string DownloadedBytes { get; set; } = "0";
        public string StatusIcon { get; set; } = Colors.LimeGreen.ToString();
        public string TextColor { get; set; } = Colors.Black.ToString();
    }

    #endregion
}
