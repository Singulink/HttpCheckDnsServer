# HttpCheck DNS Server

**HttpCheck** is a DNS server implementation intended to assist with spam filtering by testing if email domains have a working website.

The service is currently under development and experimentally hosted at `httpcheck.singulink.com`. It is freely available for your consumption and can be used to test "from" email address domains via SpamAssassin by adding the following lines to your `local.cf` file:

```
askdns AUTHOR_IN_HTTPCHECK  _AUTHORDOMAIN_.httpcheck.singulink.com A 1
score  AUTHOR_IN_HTTPCHECK  1 # Adjust score as desired
```

*WARNING:* If you host the DNS server on your own network **DO NOT** let the HTTP requests go out on the same IP as your mail server. If you do this then you risk getting your mail server blacklisted since it may appear to be infected with a botnet virus if it makes HTTP requests to honeypot websites.

This code is being shared mostly for educational and collaborative purposes. There is very little reason to host the DNS server yourself as this will result in less accurate results, so the intent is for everyone to query `httpcheck.singulink.com` in production environments. Hosting it yourself is not recommended and done at your own risk, but feel free to do with it as you please :)

As an example, if you wanted to test if `mail.groupon.com` or its parent domains (i.e. `groupon.com`) have a website, you would send an A record DNS query to `mail.groupon.com.httpcheck.singulink.com`.

New domains (i.e. domains that haven't been "seen" by the service yet) are given an initial 8 minute grace period to prevent a temporary website issue from affecting mail delivery. If a "valid" website result (currently defined as any HTTP status code < 400) is not obtained within the grace period then the DNS server will begin responding with an invalid result (`127.0.0.1`).

If a valid HTTP response is obtained then the DNS server responds with an `NXDOMAIN` result. If a previously valid website domain begins having problems with its website, a minimum grace period of ~2 days of retries will be attempted before it is declared invalid again.

Development is done using Visual Studio 2019 and the DNS server is a .NET Core 2.1 application.

Special thanks to @kapetan for his great DNS project that made this possible: https://github.com/kapetan/dns
