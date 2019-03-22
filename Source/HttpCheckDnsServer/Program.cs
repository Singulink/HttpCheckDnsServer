using DNS.Server;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;

namespace HttpCheckDnsServer
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            WriteHeader();

            bool logRequests = false;

            var requestResolver = new RequestResolver("httpcheck.singulink.com", "admin.singulink.com");
            requestResolver.AddPermanentRecord("singulink.com", true);

            requestResolver.RequestReceived += (s, e) => {
                if (logRequests) {
                    var now = DateTime.Now;
                    Console.WriteLine($"{now.ToShortDateString()} {now.ToShortTimeString()} REQ {e.Id} {e.RequestDomain} {e.EmailDomain}");
                }
            };

            requestResolver.ResponseSent += (s, e) => {
                if (logRequests) {
                    var now = DateTime.Now;
                    Console.WriteLine($"{now.ToShortDateString()} {now.ToShortTimeString()} RES {e.Id} {e.RequestDomain} {e.Result} {(int)e.Ttl.TotalSeconds}");
                }
            };

            var dnsServer = new DnsServer(requestResolver);

            RunDnsServer(dnsServer);

            Console.WriteLine("DNS Server started. Press D to toggle debug, C to clear window, or X to exit.");

            while (true) {
                var key = Console.ReadKey(true);

                if (key.Key == ConsoleKey.D) {
                    logRequests = !logRequests;
                    Console.WriteLine($"Request logging: {logRequests}");
                }
                else if (key.Key == ConsoleKey.C) {
                    Console.Clear();
                    WriteHeader();
                }
                else if (key.Key == ConsoleKey.X) {
                    break;
                }
            }

            Console.WriteLine("Shutting down DNS Server...");
            dnsServer.Dispose();
            Thread.Sleep(1000);
        }

        private static void WriteHeader()
        {
            Console.WriteLine("Email HTTP Check Server");
            Console.WriteLine("=======================\n");
        }

        private async static void RunDnsServer(DnsServer dnsServer)
        {
            try {
                await dnsServer.Listen().ConfigureAwait(false);
            }
            catch (Exception ex) {
                Console.Write(ex);
                return;
            }

            Console.WriteLine("DNS Server shut down.");
        }
    }
}
