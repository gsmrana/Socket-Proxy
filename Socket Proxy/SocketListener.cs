using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace Socket.Proxy
{
    public class SocketListener
    {
        #region Data

        private readonly TcpListener _localListener;
        public int Id { get; set; }
        public bool IsListening { get; private set; }
        public IPEndPoint BindEndPoint { get; private set; }
        public IPEndPoint RemoteEndPoint { get; private set; }

        public readonly List<SocketTunnel> SocketTunnels = new List<SocketTunnel>();

        public int ActiveTunnelsCount
        {
            get => SocketTunnels.Where(p => p.IsBridged).Count();
        }

        public int TotalTunnelsCount
        {
            get => SocketTunnels.Count();
        }

        public long UploadedBytes
        {
            get => SocketTunnels.Sum(p => p.UploadedBytes);
        }

        public long DownloadedBytes
        {
            get => SocketTunnels.Sum(p => p.DownloadedBytes);
        }

        #endregion

        #region ctor

        public SocketListener(IPEndPoint bindEndPoint, IPEndPoint remoteEndPoint)
        {
            BindEndPoint = bindEndPoint;
            RemoteEndPoint = remoteEndPoint;
            _localListener = new TcpListener(BindEndPoint);
        }

        public void Start()
        {
            _localListener.Start();
            IsListening = true;
            _localListener.BeginAcceptTcpClient(LocalListener_OnClientConnecting, null);
        }

        public void Stop()
        {
            IsListening = false;
            _localListener.Stop();
            foreach (var tunnel in SocketTunnels)
                if (tunnel.IsBridged) tunnel.Close();
            SocketTunnels.Clear();
        }

        #endregion

        #region Event and Delegates

        public delegate void LogEventHandler(object sender, TunnelEventArg e);
        public event LogEventHandler OnSocketTunnelEvent;

        #endregion

        #region Internal Methods

        private void LocalListener_OnClientConnecting(IAsyncResult ar)
        {
            try
            {
                if (!IsListening) return;
                var localSocket = _localListener.EndAcceptTcpClient(ar);
                var localEndPoint = (IPEndPoint)localSocket.Client.RemoteEndPoint;
                localEndPoint.Port = BindEndPoint.Port; //override the actual local port with bind port
                var tunnel = new SocketTunnel(localEndPoint, RemoteEndPoint, localSocket, this);
                tunnel.OnSocketEvent += SocketTunnel_OnSocketEvent;
                tunnel.Id = SocketTunnels.Count;
                SocketTunnels.Add(tunnel);
                _localListener.BeginAcceptTcpClient(LocalListener_OnClientConnecting, null);
                tunnel.Open();
            }
            catch (Exception ex)
            {
                ExceptionLog("LocalListener_OnClientConnecting Error : " + ex.Message);
            }
        }

        private void SocketTunnel_OnSocketEvent(object sender, TunnelEventArg e)
        {
            SocketTunnelEventLog(e);
        }

        #endregion

        #region Event Handlers

        public virtual void SocketTunnelEventLog(TunnelEventArg e)
        {
            OnSocketTunnelEvent?.Invoke(this, e);
        }

        public virtual void ExceptionLog(string errormessage)
        {
            OnSocketTunnelEvent?.Invoke(this, new TunnelEventArg { ErrorMessage = errormessage });
        }

        #endregion

    }

}
