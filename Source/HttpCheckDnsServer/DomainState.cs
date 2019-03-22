using System;
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
        private static readonly TimeSpan s_attemptExtra = TimeSpan.FromSeconds(5);

        // Amount of time to wait before rechecking a valid domain.
        private static readonly TimeSpan s_validDomainFirstRecheck = TimeSpan.FromDays(2);

        // The TTL to use for "permanent" entries.
        private static readonly TimeSpan s_permanentDomainTtl = TimeSpan.FromDays(14);

        // Calculated TTL for initial attempt period on new domains.
        private static readonly TimeSpan s_newDomainTtl = GetAttemptsTime(NewDomainAttemptAllowance);

        // Calulated TTL for valid domains
        private static readonly TimeSpan s_validDomainTtl = GetAttemptsTime(ValidDomainAttemptAllowance) + s_validDomainFirstRecheck;

        // Shared HTTP client used to make requests
        private static readonly HttpClient s_httpClient = new HttpClient(new HttpClientHandler() { AllowAutoRedirect = false }) { Timeout = s_httpTimeout };

        private static TimeSpan GetAttemptsTime(int numAttempts)
        {
            var attemptDuration = s_httpTimeout + s_attemptExtra;
            var newDomainTtl = attemptDuration * numAttempts;

            for (int i = 1; i < numAttempts; i++)
                newDomainTtl += GetNextUpdateDelay(i);

            return newDomainTtl;
        }

        #endregion

        #region Fields

        private readonly string _domain;

        private bool _wasValid;
        private int _invalidAttempts;

        private DateTime _ttlExpiry;

        private readonly CancellationTokenSource _refreshCancellationTokenSource;

        #endregion

        #region Constructors and Initialization

        /// <summary>
        /// Creates a new domain state object.
        /// </summary>
        /// <param name="domain">The domain name.</param>
        /// <param name="isValid">If null then domain checks are performed, otherwise the provided value determines domain validity.</param>
        public DomainState(string domain, bool? isValid = null)
        {
            _domain = domain;

            if (isValid != null) {
                if (isValid == false)
                    _invalidAttempts = int.MaxValue;

                UpdateInternalState(isValid.Value);
            }
            else {
                _ttlExpiry = DateTime.Now.Add(s_newDomainTtl);
                _refreshCancellationTokenSource = new CancellationTokenSource();
                InitStateAsync();
            }
        }

        private async void InitStateAsync()
        {
            await UpdateStateAsync().ConfigureAwait(false);
            RunRefreshLoopAsync();
        }

        #endregion

        #region Properties

        public bool IsValid => _wasValid ? _invalidAttempts < ValidDomainAttemptAllowance : _invalidAttempts < NewDomainAttemptAllowance;

        public bool IsPermanent => _refreshCancellationTokenSource == null;

        public CancellationToken RefreshCancellationToken => _refreshCancellationTokenSource.Token;

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

        public void CancelRefreshLoop()
        {
            if (_refreshCancellationTokenSource == null)
                throw new InvalidOperationException("Attempted to cancel refresh loop on permanent domain state.");

            _refreshCancellationTokenSource.Cancel();
        }

        private void UpdateInternalState(bool valid)
        {
            if (valid) {
                _wasValid = true;
                _invalidAttempts = 0;
                _ttlExpiry = DateTime.Now.Add(s_validDomainTtl);
            }
            else {
                if (_invalidAttempts < int.MaxValue) {
                    _invalidAttempts++;
                }

                var nextUpdate = DateTime.Now + GetNextUpdateDelay(_invalidAttempts);

                if (nextUpdate > _ttlExpiry)
                    _ttlExpiry = nextUpdate + s_httpTimeout + s_attemptExtra;
            }
        }

        private async Task UpdateStateAsync()
        {
            bool validResult = await GetDomainState().ConfigureAwait(false);
            UpdateInternalState(validResult);
        }

        private async void RunRefreshLoopAsync()
        {
            while (true) {
                await Task.Delay(GetNextUpdateDelay(_invalidAttempts), _refreshCancellationTokenSource.Token).ConfigureAwait(false);

                if (_refreshCancellationTokenSource.IsCancellationRequested)
                    return;

                await UpdateStateAsync().ConfigureAwait(false);
            }
        }

        private async Task<bool> GetDomainState()
        {
            try {
                var request = new HttpRequestMessage(HttpMethod.Get, "http://" + _domain);

                // Spoof real browser headers to prevent blocking
                request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml");
                request.Headers.Add("Accept-Encoding", "gzip, deflate");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 6.2; WOW64; rv:19.0) Gecko/20100101 Firefox/19.0");
                request.Headers.Add("Accept-Charset", "ISO-8859-1");

                // Only receive the first byte of content instead of the whole page if the Range header is supported
                request.Headers.Add("Range", "bytes=0-0");

                using (var response = await s_httpClient.SendAsync(request, _refreshCancellationTokenSource.Token).ConfigureAwait(false))
                    return (int)response.StatusCode < 400;
            }
            catch {
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
