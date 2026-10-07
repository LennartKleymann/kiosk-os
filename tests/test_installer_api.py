import importlib.util, json, pathlib, sys, tempfile, threading, time, urllib.error, urllib.request

spec = importlib.util.spec_from_file_location(
    "api", str(pathlib.Path(__file__).resolve().parent.parent / "scripts" / "kiosk-installer-api.py")
)
api = importlib.util.module_from_spec(spec)
sys.modules["api"] = api
spec.loader.exec_module(api)

tmp = pathlib.Path(tempfile.mkdtemp())
api.TOKEN_FILE = str(tmp / "token")
api.STATE_FILE = str(tmp / "state.json")
api.CONFIG_FILE = str(tmp / "config")
api.INSTALLER_HTML = str(tmp / "installer.html")
TOKEN = "s3cr3t-token-value"
pathlib.Path(api.TOKEN_FILE).write_text(TOKEN)
pathlib.Path(api.CONFIG_FILE).write_text("homepage=https://kiosk.example.com\n")
pathlib.Path(api.INSTALLER_HTML).write_text(
    '<meta name="kiosk-token" content="__KIOSK_TOKEN__">'
)

from http.server import HTTPServer
srv = HTTPServer(("127.0.0.1", 0), api.Handler)
PORT = srv.server_address[1]
BASE = f"http://127.0.0.1:{PORT}"
api.ALLOWED_ORIGINS = {BASE}
threading.Thread(target=srv.serve_forever, daemon=True).start()
time.sleep(0.3)

def call(path, method="GET", headers=None, body=None):
    req = urllib.request.Request(
        BASE + path, method=method,
        data=json.dumps(body).encode() if body is not None else None,
        headers=headers or {},
    )
    try:
        with urllib.request.urlopen(req, timeout=5) as r:
            return r.status, r.read().decode()
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode()

results = []
def check(name, got, want):
    ok = got == want
    results.append(ok)
    print(f"{'PASS' if ok else 'FAIL'}  {name}: got {got}, want {want}")

# Angriff: fremde Webseite im Kiosk-Browser feuert auf die API
code, _ = call("/install", "POST", {"Origin": "https://evil.example.com",
                                    "Content-Type": "application/json"},
               {"disk": "/dev/sda"})
check("fremder Origin wird abgewiesen", code, 403)

# Angriff: simple request ohne Preflight, kein Origin-Header, kein Token
code, _ = call("/install", "POST", {"Content-Type": "text/plain"}, {"disk": "/dev/sda"})
check("Request ohne Token wird abgewiesen", code, 403)

# Angriff: richtiger Origin, aber geratenes Token
code, _ = call("/install", "POST", {"Origin": BASE, "X-Kiosk-Token": "falsch",
                                    "Content-Type": "application/json"},
               {"disk": "/dev/sda"})
check("falsches Token wird abgewiesen", code, 403)

# Angriff: reboot ohne Token
code, _ = call("/reboot", "POST", {"Content-Type": "application/json"})
check("reboot ohne Token wird abgewiesen", code, 403)

# Legitim: die ausgelieferte Seite mit Token, aber ungueltiges Geraet
code, body = call("/install", "POST", {"Origin": BASE, "X-Kiosk-Token": TOKEN,
                                       "Content-Type": "application/json"},
                  {"disk": "/dev/../etc/passwd"})
check("Pfad-Traversal im Geraetenamen abgewiesen", code, 400)

# Legitim: Seite wird ausgeliefert und Token ist eingebettet
code, body = call("/")
check("Installer-Seite wird ausgeliefert", code, 200)
check("Token ist in die Seite eingebettet", TOKEN in body, True)
check("Platzhalter wurde ersetzt", "__KIOSK_TOKEN__" in body, False)

# Legitim: health und progress ohne Token lesbar
code, _ = call("/health")
check("health ohne Token erreichbar", code, 200)

# Legitim: skip mit Token liefert die Homepage aus der Config
code, body = call("/skip", "POST", {"Origin": BASE, "X-Kiosk-Token": TOKEN,
                                    "Content-Type": "application/json"})
check("skip mit Token erlaubt", code, 200)
check("skip liefert Homepage", json.loads(body).get("homepage"),
      "https://kiosk.example.com")

print()
print(f"{sum(results)}/{len(results)} Checks bestanden")
sys.exit(0 if all(results) else 1)
