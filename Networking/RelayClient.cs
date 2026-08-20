using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Xna.Framework;

namespace GW2ProximityChat
{
    public class PeerSnapshot
    {
        public string PlayerId;
        public string Name;
        public Vector3 Position;
        public Vector3 Facing;
    }

    /// <summary>
    /// Owns the single WebSocket connection to the relay server: JSON text frames carry
    /// position state and the peer roster, binary frames carry raw Opus audio.
    /// </summary>
    public class RelayClient : IDisposable
    {
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();

        // ClientWebSocket supports exactly one send in flight at a time; state (10Hz) and audio
        // (up to ~50Hz while talking) are sent from independent fire-and-forget calls, so without
        // this they can race on the same socket. That race -- and colliding with .NET's own
        // automatic keep-alive PING below -- reliably knocks the socket into the unusable
        // "Aborted" state, which is what periodic "Lost connection" drops with no network cause
        // actually are.
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        private ClientWebSocket _socket;
        private CancellationTokenSource _cts;

        public event Action<PeerSnapshot[]> PeersReceived;
        public event Action<string, byte[]> AudioFrameReceived;
        public event Action Connected;
        public event Action<Exception> Disconnected;
        public event Action<string> ServerHelloReceived;
        public event Action<string> AuthFailed;

        public bool IsConnected => _socket != null && _socket.State == WebSocketState.Open;

        public async Task ConnectAsync(string host, int port)
        {
            Disconnect();

            _cts = new CancellationTokenSource();
            _socket = new ClientWebSocket();

            // Disabled: .NET Framework's ClientWebSocket sends this itself, on the same socket,
            // outside of our SendAsync calls -- it can collide with an app-level send in flight
            // and abort the connection. ProximityService sends its own app-level keep-alive
            // instead (through the same serialized send path as everything else) so idle
            // connections still don't get dropped by a NAT/router in between.
            _socket.Options.KeepAliveInterval = TimeSpan.Zero;

            var uri = new Uri($"ws://{host}:{port}/ws");
            await _socket.ConnectAsync(uri, _cts.Token).ConfigureAwait(false);

            Connected?.Invoke();
            var token = _cts.Token;
            _ = Task.Run(() => ReceiveLoopAsync(token));
        }

        public void Disconnect()
        {
            _cts?.Cancel();

            try
            {
                _socket?.Abort();
            }
            catch
            {
                // best-effort teardown; the socket may already be broken
            }

            _socket?.Dispose();
            _socket = null;
        }

        public Task SendStateAsync(StateMessage state)
        {
            string json = _serializer.Serialize(state);
            return SendAsyncInternal(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), WebSocketMessageType.Text);
        }

        public Task SendAudioAsync(byte[] opusFrame)
        {
            return SendAsyncInternal(new ArraySegment<byte>(opusFrame), WebSocketMessageType.Binary);
        }

        /// <summary>
        /// A minimal app-level heartbeat (the server's state handler just ignores it, since it
        /// has no PlayerId) so a connection that isn't otherwise sending anything -- e.g. GW2
        /// isn't running yet -- still produces periodic traffic and doesn't sit idle long enough
        /// for a NAT/router to drop the mapping.
        /// </summary>
        public Task SendKeepAliveAsync()
        {
            return SendAsyncInternal(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"Type\":\"ping\"}")), WebSocketMessageType.Text);
        }

        private async Task SendAsyncInternal(ArraySegment<byte> data, WebSocketMessageType messageType)
        {
            var socket = _socket;
            var cts = _cts;
            if (socket == null || socket.State != WebSocketState.Open || cts == null) return;

            await _sendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (socket.State != WebSocketState.Open) return;
                await socket.SendAsync(data, messageType, true, cts.Token).ConfigureAwait(false);
            }
            catch
            {
                // ReceiveLoopAsync will observe the same failure and raise Disconnected
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            var buffer = new byte[16 * 1024];
            var socket = _socket;

            try
            {
                while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    using (var messageStream = new MemoryStream())
                    {
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);

                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                Disconnected?.Invoke(null);
                                return;
                            }

                            messageStream.Write(buffer, 0, result.Count);
                        }
                        while (!result.EndOfMessage);

                        byte[] messageBytes = messageStream.ToArray();

                        if (result.MessageType == WebSocketMessageType.Text)
                        {
                            HandleTextMessage(Encoding.UTF8.GetString(messageBytes));
                        }
                        else
                        {
                            HandleBinaryMessage(messageBytes);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    Disconnected?.Invoke(ex);
                }
            }
        }

        private void HandleTextMessage(string json)
        {
            MessageEnvelope envelope;
            try
            {
                envelope = _serializer.Deserialize<MessageEnvelope>(json);
            }
            catch
            {
                return;
            }

            switch (envelope?.Type)
            {
                case "peers":
                    HandlePeersMessage(json);
                    break;
                case "hello":
                    var hello = _serializer.Deserialize<HelloMessage>(json);
                    if (hello != null) ServerHelloReceived?.Invoke(hello.ServerName);
                    break;
                case "auth_failed":
                    var authFailed = _serializer.Deserialize<AuthFailedMessage>(json);
                    AuthFailed?.Invoke(authFailed?.Reason ?? "Invalid password");
                    break;
            }
        }

        private void HandlePeersMessage(string json)
        {
            var message = _serializer.Deserialize<PeersMessage>(json);
            if (message?.Peers == null) return;

            var snapshots = new PeerSnapshot[message.Peers.Length];
            for (int i = 0; i < message.Peers.Length; i++)
            {
                var p = message.Peers[i];
                snapshots[i] = new PeerSnapshot
                {
                    PlayerId = p.PlayerId,
                    Name = p.Name,
                    Position = ToVector3(p.Pos),
                    Facing = ToVector3(p.Facing),
                };
            }

            PeersReceived?.Invoke(snapshots);
        }

        private void HandleBinaryMessage(byte[] data)
        {
            if (data.Length < 1) return;

            int idLen = data[0];
            if (data.Length < 1 + idLen) return;

            string peerId = Encoding.UTF8.GetString(data, 1, idLen);
            int payloadOffset = 1 + idLen;
            int payloadLen = data.Length - payloadOffset;

            var payload = new byte[payloadLen];
            Array.Copy(data, payloadOffset, payload, 0, payloadLen);

            AudioFrameReceived?.Invoke(peerId, payload);
        }

        private static Vector3 ToVector3(float[] v)
        {
            if (v == null || v.Length < 3) return Vector3.Zero;
            return new Vector3(v[0], v[1], v[2]);
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
