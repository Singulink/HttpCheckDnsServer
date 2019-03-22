using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HttpCheckDnsServer
{
    public sealed class TldCollection : IDisposable
    {
        private static readonly HttpClient s_httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(15) };
        private static readonly TimeSpan s_updateDelay = TimeSpan.FromDays(1);
        private static readonly TimeSpan s_retryDelay = TimeSpan.FromSeconds(15);

        private HashSet<string> _collection;
        private readonly CancellationTokenSource _updateCancellationSource = new CancellationTokenSource();

        public TldCollection()
        {
            RunUpdateLoopAsync();
        }

        public int Count => _collection?.Count ?? 0;

        public bool IsReady => _collection != null;

        public bool Contains(string tld) => _collection?.Contains(tld.ToLower()) ?? false;

        private async void RunUpdateLoopAsync()
        {
            try {
                while (!_updateCancellationSource.IsCancellationRequested) {
                    string tldContent = null;

                    try {
                        tldContent = await s_httpClient.GetStringAsync("http://data.iana.org/TLD/tlds-alpha-by-domain.txt").ConfigureAwait(false);
                    }
                    catch (HttpRequestException) {
                        await Task.Delay(s_retryDelay, _updateCancellationSource.Token).ConfigureAwait(false);
                        continue;
                    }

                    var newCollection = new HashSet<string>();
                    var reader = new StringReader(tldContent);
                    string line;

                    while ((line = reader.ReadLine()) != null) {
                        if (!(string.IsNullOrWhiteSpace(line) || line.StartsWith("#")))
                            newCollection.Add(line.ToLower());
                    }

                    _collection = newCollection;
                    await Task.Delay(s_updateDelay, _updateCancellationSource.Token).ConfigureAwait(false);
                }
            }
            catch (TaskCanceledException) { }
        }

        public void Dispose()
        {
            if (!_updateCancellationSource.IsCancellationRequested) {
                _updateCancellationSource.Cancel();
                _updateCancellationSource.Dispose();
            }
        }
    }
}
