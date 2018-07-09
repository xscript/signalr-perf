using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace signalr.perf.client
{
    public class Program
    {
        private const string AzureSignalRConnectionStringKey = "AzureSignalRConnectionString";
        private const string Hub = "chat";
        private static readonly double TickInMillisecond = 1000d / Stopwatch.Frequency;
        private const int TotalConnection = 1000;

        private static string _clientUrl;
        private static SigningCredentials _credentials;
        private static readonly JwtSecurityTokenHandler Handler = new JwtSecurityTokenHandler();

        private static int _connected;

        static void Main(string[] args)
        {
            Init();

            Run().GetAwaiter().GetResult();
        }

        private static void Init()
        {
            var configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .AddUserSecrets<Program>()
                .Build();

            var connectionString = configuration[AzureSignalRConnectionStringKey];
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentNullException("Azure SignalR Service connection string not found.");
            }

            string endpoint;
            string accessKey;
            try
            {
                var dict = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Split('=', 2))
                    .ToDictionary(y => y[0].Trim().ToLower(), y => y[1].Trim(), StringComparer.OrdinalIgnoreCase);
                endpoint = dict["endpoint"];
                accessKey = dict["accesskey"];
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Invalid Azure SignalR Service connection string: {connectionString}", ex);
            }

            _clientUrl = $"{endpoint}:5001/client/?hub={Hub}";
            _credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(accessKey)), SecurityAlgorithms.HmacSha256);
        }

        private static async Task Run()
        {
            var tasks = new List<Task>(TotalConnection);
            var startTimestamp = Stopwatch.GetTimestamp();
            for (var i = 0; i < TotalConnection; i++)
            {
                tasks.Add(StartConnection());
            }
            await Task.WhenAll(tasks);
            var endTimestamp = Stopwatch.GetTimestamp();

            Console.WriteLine($"{_connected}/{TotalConnection} Connections established. Time elapsed: {(endTimestamp - startTimestamp) * TickInMillisecond} ms");
        }

        private static int _receivedMessageCount;
        private static async Task<HubConnection> StartConnection()
        {
            var connection = new HubConnectionBuilder()
                .WithUrl(_clientUrl, options => options.AccessTokenProvider = GenerateAccessToken)
                .ConfigureLogging(builder => builder.SetMinimumLevel(LogLevel.Debug))
                .Build();

            try
            {
                connection.On<string>("echo", message => Interlocked.Increment(ref _receivedMessageCount));

                await connection.StartAsync();
                
                Interlocked.Increment(ref _connected);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start connection: {ex} \n");
            }

            return connection;
        }

        private static async Task StartSendingMessageAsync()
        {
            await Task.CompletedTask;
        }

        private static Task<string> GenerateAccessToken()
        {
            var token = Handler.CreateJwtSecurityToken(
                audience: _clientUrl,
                expires: DateTime.UtcNow.AddDays(1),
                signingCredentials: _credentials);
            return Task.FromResult(Handler.WriteToken(token));
        }
    }
}
