using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections.Features;
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
        private const int Concurrency = 200;

        private static string _clientUrl;
        private static SigningCredentials _credentials;
        private static readonly JwtSecurityTokenHandler Handler = new JwtSecurityTokenHandler();

        private static readonly IList<HubConnection> _connections = new List<HubConnection>();
        private static int _connected;
        private static int _sentMessageCount;
        private static int _errorSendMessageCount;
        private static int _receivedMessageCount;

        static void Main(string[] args)
        {
            Init();

            Run().GetAwaiter().GetResult();

            PrintStatistics();
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
                //await StartConnection();

                tasks.Add(StartConnection());
                if (i > 0 && i % Concurrency == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
            }
            await Task.WhenAll(tasks);
            var endTimestamp = Stopwatch.GetTimestamp();

            Console.WriteLine($"{_connected}/{TotalConnection} Connections established. Concurrency = {Concurrency}/s. Time elapsed: {(endTimestamp - startTimestamp) * TickInMillisecond} ms");

            if (_connections.Count == 0)
            {
                Console.WriteLine("No connection is connected. Exit.");
                return;
            }

            tasks = _connections.Select(StartSendingMessageAsync).ToList();
            await Task.WhenAll(tasks);

            // Wait for some extra time to allow receiving all messages.
            await Task.Delay(TimeSpan.FromSeconds(10));
        }

        private static async Task<HubConnection> StartConnection()
        {
            var connection = new HubConnectionBuilder()
                .WithUrl(_clientUrl, options => options.AccessTokenProvider = GenerateAccessToken)
                .ConfigureLogging(builder => builder.SetMinimumLevel(LogLevel.Debug))
                .Build();

            try
            {
                connection.On<string, string>("echo", (name, message) => Interlocked.Increment(ref _receivedMessageCount));

                await connection.StartAsync();
                
                Interlocked.Increment(ref _connected);
                _connections.Add(connection);
            }
            catch (Exception)
            {
                //Console.WriteLine($"Failed to start connection: {ex} \n");
            }

            return connection;
        }

        private static async Task StartSendingMessageAsync(HubConnection connection)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(StaticRandom.Next(1000)));
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1)))
            {
                while (!cts.IsCancellationRequested)
                {
                    try
                    {
                        await connection.SendAsync("echo", "id", $"{Stopwatch.GetTimestamp()}");
                        Interlocked.Increment(ref _sentMessageCount);
                    }
                    catch
                    {
                        Interlocked.Increment(ref _errorSendMessageCount);
                    }

                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
            }
        }

        private static Task<string> GenerateAccessToken()
        {
            var token = Handler.CreateJwtSecurityToken(
                audience: _clientUrl,
                expires: DateTime.UtcNow.AddDays(1),
                signingCredentials: _credentials);
            return Task.FromResult(Handler.WriteToken(token));
        }

        private static void PrintStatistics()
        {
            Console.WriteLine($"Initiated       : {TotalConnection}");
            Console.WriteLine($"Connected       : {_connected}");
            Console.WriteLine($"Sent Count      : {_sentMessageCount}");
            Console.WriteLine($"Send Error      : {_errorSendMessageCount}");
            Console.WriteLine($"Received Count  : {_receivedMessageCount}");
        }
    }
}
