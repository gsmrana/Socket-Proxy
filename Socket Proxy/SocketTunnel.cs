using System;
using System.Net;
using System.Net.Sockets;

namespace Socket.Proxy
{
    public enum TunnelEvent
    {
        Log,
        TunnelOpened,
        TunnelClosed,
        ConnectingToRemote,
        ReceivedFromLocal,
        UploadedToRemote,
        ReceivedFromRemote,
        DownloadedToLocal,
        DisconnectedFromLocal,
        DisconnectedFromRemote,
        Exception
    }

    public class TunnelEventArg : EventArgs
    {
        public int ListenerId { get; set; }
        public int TunnelId { get; set; }
        public TunnelEvent Event { get; set; }
        public IPEndPoint Source { get; set; }
        public IPEndPoint Destination { get; set; }
        public int DataSize { get; set; }
        public byte[] Data { get; set; }
        public string ErrorMessage { get; set; }
    }

    public class SocketTunnel
    {
        #region Data

        private TcpClient _remoteSocket;
        public IPEndPoint _localEp;
        public IPEndPoint _remoteEp;

        private readonly TcpClient _localSocket;

        private const int BUFF_SIZE = 0x100000; //1MB
        private readonly byte[] _localEpBuffer = new byte[BUFF_SIZE];
        private readonly byte[] _remoteEpBuffer = new byte[BUFF_SIZE];

        public int Id { get; set; }
        public bool IsBridged { get; private set; }
        public long UploadedBytes { get; private set; }
        public long DownloadedBytes { get; private set; }
        public SocketListener Listener { get; private set; }

        #endregion

        #region ctor

        public SocketTunnel(IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, TcpClient localSocket, SocketListener listener)
        {
            _localEp = localEndPoint;
            _remoteEp = remoteEndPoint;
            _localSocket = localSocket;
            Listener = listener;
            UploadedBytes = 0;
            DownloadedBytes = 0;
            IsBridged = false;
        }

        public void Open()
        {
            try
            {
                SocketEvent(TunnelEvent.ConnectingToRemote);
                _remoteSocket = new TcpClient();
                _remoteSocket.Connect(_remoteEp);
                IsBridged = true;
                _remoteSocket.Client.BeginReceive(_remoteEpBuffer, 0, BUFF_SIZE, SocketFlags.None, RemoteSocket_OnDataReceived, null);
                _localSocket.Client.BeginReceive(_localEpBuffer, 0, BUFF_SIZE, SocketFlags.None, LocalSocket_OnDataReceived, null);
                SocketEvent(TunnelEvent.TunnelOpened);
            }
            catch (Exception ex)
            {
                ExceptionLog("Connecting Error : " + ex.Message);
            }
        }

        public void Close()
        {
            try
            {
                IsBridged = false;
                if (_remoteSocket != null)
                {
                    _remoteSocket.Close();
                    _remoteSocket.Dispose();
                }
                if (_localSocket != null)
                {
                    _localSocket.Close();
                    _localSocket.Dispose();
                }
            }
            catch (Exception ex)
            {
                ExceptionLog("Closing Error : " + ex.Message);
            }
            SocketEvent(TunnelEvent.TunnelClosed);
        }

        #endregion

        #region Event and Delegates

        public delegate void LogEventHandler(object sender, TunnelEventArg e);
        public event LogEventHandler OnSocketEvent;

        #endregion

        #region Socket Send/Receive

        private void LocalSocket_OnDataReceived(IAsyncResult ar)
        {
            if (!IsBridged) return;
            try
            {
                var bytesReceived = _localSocket.Client.EndReceive(ar);
                if (bytesReceived == 0)
                {
                    SocketEvent(TunnelEvent.DisconnectedFromLocal);
                    Close();
                    return;
                }
                var data = new byte[bytesReceived];
                Array.Copy(_localEpBuffer, data, bytesReceived);
                SocketEvent(TunnelEvent.ReceivedFromLocal, bytesReceived, data);

                var bytesSent = _remoteSocket.Client.Send(data);
                UploadedBytes += bytesSent;
                SocketEvent(TunnelEvent.UploadedToRemote, bytesSent, data);
                _localSocket.Client.BeginReceive(_localEpBuffer, 0, BUFF_SIZE, SocketFlags.None, LocalSocket_OnDataReceived, null);
            }
            catch (Exception ex)
            {
                ExceptionLog("LocalSocket_OnDataReceived Error : " + ex.Message);
            }
        }

        private void RemoteSocket_OnDataReceived(IAsyncResult ar)
        {
            if (!IsBridged) return;
            try
            {
                var bytesReceived = _remoteSocket.Client.EndReceive(ar);
                if (bytesReceived == 0)
                {
                    SocketEvent(TunnelEvent.DisconnectedFromRemote);
                    Close();
                    return;
                }

                var data = new byte[bytesReceived];
                Array.Copy(_remoteEpBuffer, data, bytesReceived);
                SocketEvent(TunnelEvent.ReceivedFromRemote, bytesReceived, data);

                var bytesSent = _localSocket.Client.Send(data);
                DownloadedBytes += bytesSent;
                SocketEvent(TunnelEvent.DownloadedToLocal, bytesSent, data);
                _remoteSocket.Client.BeginReceive(_remoteEpBuffer, 0, BUFF_SIZE, SocketFlags.None, RemoteSocket_OnDataReceived, null);
            }
            catch (Exception ex)
            {
                ExceptionLog("RemoteSocket_OnDataReceived Error : " + ex.Message);
            }
        }

        #endregion

        #region Event Handlers

        public virtual void SocketEvent(TunnelEvent eventType, int dataSize = 0, byte[] data = null)
        {
            var args = new TunnelEventArg
            {
                Event = eventType,
                ListenerId = Listener.Id,
                TunnelId = Id,
                Source = _localEp,
                Destination = _remoteEp,
                DataSize = dataSize,
                Data = data
            };
            OnSocketEvent?.Invoke(this, args);
        }

        public virtual void ExceptionLog(string errorMessage)
        {
            var args = new TunnelEventArg
            {
                Event = TunnelEvent.Exception,
                ListenerId = Listener.Id,
                TunnelId = Id,
                Source = _localEp,
                Destination = _remoteEp,
                ErrorMessage = errorMessage
            };
            OnSocketEvent?.Invoke(this, args);
        }

        #endregion

    }
}
