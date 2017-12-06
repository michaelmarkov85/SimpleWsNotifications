using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace SimpleWs
{
	public class WsManager
	{
		/// <summary>
		/// Key: socketId, Value: socketClient
		/// </summary>
		private static List<WsClient> _sockets = new List<WsClient>();
		private static Object thisLock = new Object();

		internal static WsClient[] GetClientsByUser(string id)
		{
			WsClient[] sockets = _sockets
				.Where(c => string.Equals(c.UserId, id)
					&& c.Socket.State == System.Net.WebSockets.WebSocketState.Open)
				?.ToArray();
			if (sockets == null || sockets.Length < 1)
				return null;

			var socketsToRemove = sockets.Where(s => s.ExpirationTime <= DateTime.UtcNow)?.ToArray();
			RemoveMultipleClients(socketsToRemove);
			var result = sockets.Where(s => s.ExpirationTime > DateTime.UtcNow)?.ToArray();


			return result;
		}

		internal static void AddClient(WsClient wsClient)
		{
			lock (thisLock)
			{
				_sockets.Add(wsClient);
			}
		}

		internal static void CheckExpired(int secFrequansy)
		{
			int rate = secFrequansy * 1000;
			while (true)
			{
				try
				{
					CleanExpired();
					Thread.Sleep(rate);
				}
				catch (Exception)
				{ }
			}
		}

		private static void CleanExpired()
		{
			var socketsToRemove = _sockets.Where(s => s.ExpirationTime <= DateTime.UtcNow)?.ToArray();
			RemoveMultipleClients(socketsToRemove);
		}


		internal static void RemoveClient(WsClient wsClient)
		{
			if (wsClient.Socket.State == System.Net.WebSockets.WebSocketState.Open)
			{
				Task.Run(async () => await wsClient.Socket.CloseAsync(
					System.Net.WebSockets.WebSocketCloseStatus.PolicyViolation, "WS session expired.", default(CancellationToken)));
			}

			if (wsClient == null || !_sockets.Contains(wsClient))
				return;
			lock (thisLock)
			{
				_sockets.Remove(wsClient);
			}
		}

		internal static void RemoveMultipleClients(WsClient[] wsClients)
		{
			if (wsClients == null || wsClients.Length < 1)
				return;

			foreach (var item in wsClients.Distinct().ToArray())
			{
				RemoveClient(item);
			}
		}
	}
}
