using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleWs
{
	public class WsClient
	{
		// Properties
		public WebSocket Socket { get; }
		public string SocketId { get; }
		public string UserId { get; }
		public DateTime ExpirationTime { get; }
		// Constructor
		public WsClient(WebSocket currentSocket, string userIdCookie, DateTime expirationTime)
		{
			Socket = currentSocket;
			UserId = userIdCookie;
			ExpirationTime = expirationTime;
			SocketId = Guid.NewGuid().ToString();
		}
		// Public methods
		public async Task Listen(CancellationToken ct)
		{
			while (true)
			{
				if (ct.IsCancellationRequested)
					throw new Exception("CancellationRequested received");

				string response = await ReceiveStringAsync(Socket, ct);
				if (string.IsNullOrEmpty(response))
				{
					if (Socket.State != WebSocketState.Open)
					{
						throw new Exception("Connection is not opened");
					}
					continue;
				}

				await HandleMessage(response);
			}
			throw new Exception("Connection is not opened");
		}
		// Private methods
		private async Task HandleMessage(string message)
		{
			WsClient[] clientSockets = WsManager.GetClientsByUser(this.UserId);
			if (clientSockets == null || clientSockets.Length < 1)
				return;
			var sockets = clientSockets.Select(c => c.Socket).ToArray();

			await BroadcastMessage(message, sockets);
		}
		private async Task BroadcastMessage(string message, WebSocket[] sockets)
		{
			List<Task> tasks = new List<Task>();
			foreach (var s in sockets)
			{
				tasks.Add(Task.Run(async () => await SendToSocketAsync(message, s)));
			}
			await Task.WhenAll(tasks);
		}
		private async Task<string> ReceiveStringAsync(WebSocket socket, CancellationToken ct = default(CancellationToken))
		{
			ArraySegment<byte> buffer = new ArraySegment<byte>(new byte[1024 * 4]);
			using (MemoryStream ms = new MemoryStream())
			{
				WebSocketReceiveResult result;
				do
				{
					ct.ThrowIfCancellationRequested();

					result = await socket.ReceiveAsync(buffer, ct);
					ms.Write(buffer.Array, buffer.Offset, result.Count);
				}
				while (!result.EndOfMessage);

				ms.Seek(0, SeekOrigin.Begin);
				if (result.MessageType != WebSocketMessageType.Text)
				{
					return null;
				}

				// Encoding UTF8: https://tools.ietf.org/html/rfc6455#section-5.6
				using (StreamReader reader = new StreamReader(ms, System.Text.Encoding.UTF8))
				{
					return await reader.ReadToEndAsync();
				}
			}
		}
		private async Task SendToSocketAsync(string data, WebSocket socket, CancellationToken ct = default(CancellationToken))
		{
			byte[] buffer = System.Text.Encoding.UTF8.GetBytes(data);
			ArraySegment<byte> segment = new ArraySegment<byte>(buffer);
			await socket.SendAsync(segment, WebSocketMessageType.Text, true, ct);
		}
	}
}
