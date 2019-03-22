using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HttpCheckDnsServer
{
    public class DomainState
    {
        #region Static / Constant Members

        // Number of initial attempts before a domain is declared invalid.
        private const int NewDomainAttemptAllowance = 4; // 4 attempts = ~8 minutes

        // Number of invalid attempts before a previously valid domain is declared invalid.
        private const int ValidDomainAttemptAllowance = 13; // 13 attempts = ~2d3h

        // Timeout for HTTP response
        private static readonly TimeSpan s_httpTimeout = TimeSpan.FromSeconds(10);

        // Extra time added to TTL to accomodate variance in attempt scheduling / execution time
        private static readonly TimeSpan s_attemptTimeVariance = TimeSpan.FromSeconds(5);

        // Amount of time to wait before rechecking a valid domain.
        private static readonly TimeSpan s_validDomainFirstRecheck = TimeSpan.FromDays(2);

        // The TTL to use for "permanent" entries.
        private static readonly TimeSpan s_permanentDomainTtl = TimeSpan.FromDays(14);

        // Calculated TTL for initial attempt period on new domains.
        private static readonly TimeSpan s_newDomainTtl = GetAttemptsTotalTime(NewDomainAttemptAllowance);

        // Calulated TTL for valid domains
        private static readonly TimeSpan s_validDomainTtl = GetAttemptsTotalTime(ValidDomainAttemptAllowance) + s_validDomainFirstRecheck;

        // Shared HTTP client used to make requests
        private static readonly HttpClient s_httpClient = new HttpClient(new HttpClientHandler() { AllowAutoRedirect = false }) { Timeout = s_httpTimeout };

        private static TimeSpan GetAttemptsTotalTime(int numAttempts)
        {
            var attemptDuration = s_httpTimeout + s_attemptTimeVariance;
            var totalTime = attemptDuration * numAttempts;

            for (int i = 1; i < numAttempts; i++)
                totalTime += GetNextUpdateDelay(i);

            return totalTime;
        }

        #endregion

        #region Fields

        private readonly string _name;

        private bool _wasValid;
        private int _invalidAttempts;

        private DateTime _ttlExpiry;

        private readonly CancellationTokenSource _updateCancellationSource;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new domain state object.
        /// </summary>
        /// <param name="name">The domain name.</param>
        /// <param name="valid">If null then domain checks are performed, otherwise the provided value determines domain validity permanently.</param>
        public DomainState(string name, bool? valid = null)
        {
            _name = name;

            if (valid != null) {
                _invalidAttempts = valid == true ? 0 : int.MaxValue;
            }
            else {
                _ttlExpiry = DateTime.Now.Add(s_newDomainTtl);
                _updateCancellationSource = new CancellationTokenSource();
                RunUpdateLoopAsync();
            }
        }

        #endregion

        #region Properties

        public string Name => _name;

        public bool IsValid => _wasValid ? _invalidAttempts < ValidDomainAttemptAllowance : _invalidAttempts < NewDomainAttemptAllowance;

        public bool IsPermanent => _updateCancellationSource == null;

        public CancellationToken RefreshCancellationToken => _updateCancellationSource.Token;

        public TimeSpan Ttl
        {
            get {
                if (IsPermanent)
                    return s_permanentDomainTtl;

                var ttl = _ttlExpiry - DateTime.Now;
                return ttl.TotalMinutes < 1 ? TimeSpan.FromMinutes(1) : ttl;
            }
        }

        #endregion

        #region Methods

        public void CancelUpdateLoop()
        {
            Debug.Assert(_updateCancellationSource != null, "attempt to cancel refresh loop on permanent domain state");

            if (_updateCancellationSource != null) {
                _updateCancellationSource.Cancel();
                _updateCancellationSource.Dispose();
            }
        }

        private async void RunUpdateLoopAsync()
        {
            try {
                while (!_updateCancellationSource.IsCancellationRequested) {
                    bool valid = await GetDomainStateAsync().ConfigureAwait(false);

                    if (valid) {
                        _wasValid = true;
                        _invalidAttempts = 0;
                        _ttlExpiry = DateTime.Now.Add(s_validDomainTtl);
                    }
                    else {
                        if (_invalidAttempts < int.MaxValue)
                            _invalidAttempts++;

                        var nextUpdate = DateTime.Now + GetNextUpdateDelay(_invalidAttempts);

                        if (nextUpdate > _ttlExpiry)
                            _ttlExpiry = nextUpdate + s_httpTimeout + s_attemptTimeVariance;
                    }

                    await Task.Delay(GetNextUpdateDelay(_invalidAttempts), _updateCancellationSource.Token).ConfigureAwait(false);
                }
            }
            catch (TaskCanceledException) { }
        }

        private async Task<bool> GetDomainStateAsync()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "http://" + _name);

            // Spoof real browser headers to prevent blocking
            request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml");
            request.Headers.Add("Accept-Encoding", "gzip, deflate");
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 6.2; WOW64; rv:19.0) Gecko/20100101 Firefox/19.0");
            request.Headers.Add("Accept-Charset", "ISO-8859-1");

            // Only receive the first byte of content instead of the whole page if the Range header is supported
            request.Headers.Add("Range", "bytes=0-0");

            try {
                using (var response = await s_httpClient.SendAsync(request, _updateCancellationSource.Token).ConfigureAwait(false))
                    return (int)response.StatusCode < 400;
            }
            catch (OperationCanceledException ex) when (ex.GetType() == typeof(OperationCanceledException)) {
                // HTTP timeout
                return false;
            }
            catch (HttpRequestException) {
                return false;
            }
        }

        private static TimeSpan GetNextUpdateDelay(int invalidAttempts)
        {
            if (invalidAttempts == 0)
                return s_validDomainFirstRecheck;

            int minutes = 1 << Math.Min(invalidAttempts - 1, 10);
            return TimeSpan.FromMinutes(minutes);
        }

        #endregion
    }
}
