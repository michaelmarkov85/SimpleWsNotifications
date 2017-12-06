using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;

namespace SimpleWs
{
	internal class SimpleWsMiddleware
	{
		const string COOKIE_USER_KEY = "user";
		const string COOKIE_TOKEN_KEY = "token";
		private readonly RequestDelegate _next;

		private readonly Dictionary<string, string> _users = new Dictionary<string, string>
			{
				{ "a6241f60-bf59-4cfe-8dcb-a17c81d7abb5", "superJwtSecretSecretSecret_key1" },
				{ "9bf6771c-f88c-4b1d-9023-1620eba39c6d", "superJwtSecretSecretSecret_key2" },
				{ "a31a2a69-eb6e-4257-b8f8-73b5addecc15", "superJwtSecretSecretSecret_key3" }
			};

		public SimpleWsMiddleware(RequestDelegate next)
		{
			_next = next;
		}

		public async Task Invoke(HttpContext context)
		{
			// If not WebSockets request - ignore this and go to next middleware
			if (!context.WebSockets.IsWebSocketRequest)
			{
				await _next.Invoke(context);
				return;
			}

			// Establishing WebSocket connection
			CancellationToken ct = context.RequestAborted;
			WebSocket currentSocket = await context.WebSockets.AcceptWebSocketAsync();

			if (currentSocket == null || currentSocket.State != WebSocketState.Open)
				return;

			// Getting token from which determine a user/owner
			string userIdCookie;
			string tokenCookie;
			DateTime expirationTime;

			try
			{
				userIdCookie = context.Request.Query[COOKIE_USER_KEY].ToString();
				tokenCookie = context.Request.Query[COOKIE_TOKEN_KEY].ToString();
				expirationTime = new JwtSecurityTokenHandler().ReadJwtToken(tokenCookie).ValidTo;
			}
			catch (Exception)
			{
				await currentSocket.CloseAsync(
					WebSocketCloseStatus.PolicyViolation, "Wrong cookies or damaged JWT.", default(CancellationToken));
				return;
			}

			if (expirationTime < DateTime.UtcNow)
			{
				await currentSocket.CloseAsync(
					WebSocketCloseStatus.PolicyViolation, "JWT expired.", default(CancellationToken));
				return;
			}

			if (!TokenIsValid(userIdCookie, tokenCookie))
			{
				await currentSocket.CloseAsync(
					WebSocketCloseStatus.PolicyViolation, "Not authorized.", default(CancellationToken));
				return;
			}

			WsClient wsClient = new WsClient(currentSocket, userIdCookie, expirationTime);
			
			// Adding socket to Manager and subscribing for new messages.
			try
			{
				WsManager.AddClient(wsClient);
				await wsClient.Listen(ct);
			}
			catch (Exception ex)
			{
				// TODO: Cleanup - remove socket if Aborted
				Console.WriteLine(ex.Message);
				WsManager.RemoveClient(wsClient);
			}
		}
		private bool TokenIsValid(string userId, string token)
		{
			if (string.IsNullOrWhiteSpace(userId)
				|| !Guid.TryParse(userId, out Guid guid)
				|| string.IsNullOrWhiteSpace(token))
				return false;

			if (!_users.ContainsKey(userId))
				return false;

			string secret = _users[userId];
			if (string.IsNullOrWhiteSpace(secret))
				return false;

			SecurityKey securityKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(secret));
			TokenValidationParameters validationParameters = new TokenValidationParameters()
			{
				IssuerSigningKey = securityKey,
				ValidateAudience = false,
				ValidateIssuer = false
			};
			try
			{
				System.Security.Claims.ClaimsPrincipal principal 
					= new JwtSecurityTokenHandler()
					.ValidateToken(token, validationParameters, out var vt);

				bool result = (vt != null);
				return result;
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.Message);
				return false;
			}
		}
	}
}