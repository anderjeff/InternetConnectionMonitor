using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace InternetConnectionMonitor
{
    public class Worker : BackgroundService
    {
        /*
         *  tracert give the for 1st 6 stops
         *   1     2 ms     1 ms     2 ms  192.168.0.1
             2    10 ms    10 ms    10 ms  142.254.152.129
             3    24 ms    22 ms    28 ms  ae62.nprrwi0102h.midwest.rr.com [24.164.239.37]
             4    12 ms    13 ms    20 ms  be52.gnfdwibb01r.midwest.rr.com [65.31.113.122]
             5    17 ms    16 ms    14 ms  bu-ether16.chcgildt87w-bcr00.tbone.rr.com [66.109.6.204]
             6    18 ms    15 ms    14 ms  0.ae13.pr1.lax00.tbone.rr.com [66.109.7.57]
         */

        private readonly ILogger<Worker> _logger;

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var startTime = DateTime.Now;

                using (var client = new HttpClient())
                {
                    try
                    {
                        var ping = new Ping();
                        var reply = ping.Send("google.com");
                        Log.Information($"{reply.Status}");

                        if (reply.Status != IPStatus.Success)
                        {
                            Log.Information("Looking up traceroute...");

                            // find out why, start pinging intermediate stuff
                            foreach (var ipAddress in GetTraceRoute("google.com"))
                            {
                                Log.Information($"Pinging {ipAddress}");
                                startTime = DateTime.Now;
                                var traceReply = ping.Send(ipAddress);
                                Log.Information($"Reply from {traceReply.Status}: bytes={traceReply.Buffer.Length} time={traceReply.RoundtripTime}ms");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error pinging out");
                    }
                }

                var stopTime = DateTime.Now;
                var timeToWait = TimeSpan.FromSeconds(5).Subtract(stopTime.Subtract(startTime));
                await Task.Delay((int)timeToWait.TotalMilliseconds, stoppingToken);
            }
        }

        public static IEnumerable<IPAddress> GetTraceRoute(string hostname)
        {
            // following are similar to the defaults in the "traceroute" unix command.
            const int timeout = 10000;
            const int maxTTL = 30;
            const int bufferSize = 32;

            var buffer = new byte[bufferSize];
            new Random().NextBytes(buffer);

            using (var pinger = new Ping())
            {
                for (int ttl = 1; ttl <= maxTTL; ttl++)
                {
                    PingOptions options = new PingOptions(ttl, true);
                    PingReply reply = pinger.Send(hostname, timeout, buffer, options);

                    // we've found a route at this ttl
                    if (reply.Status == IPStatus.Success || reply.Status == IPStatus.TtlExpired)
                        yield return reply.Address;

                    // if we reach a status other than expired or timed out, we're done searching or there has been an error
                    if (reply.Status != IPStatus.TtlExpired && reply.Status != IPStatus.TimedOut)
                        break;
                }
            }
        }
    }
}
