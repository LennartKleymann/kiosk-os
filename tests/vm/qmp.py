#!/usr/bin/env python3
"""Tiny QMP client to drive the test VM: screenshot, click, type, keys, usb hotplug."""
import json, os, socket, sys, time

SOCK = os.path.join(os.environ.get("VM_DIR", "/root/kiosk-vm"), "qmp.sock")
W, H = 1280, 800  # guest resolution (virtio-vga default)

def qmp(*cmds):
    s = socket.socket(socket.AF_UNIX); s.connect(SOCK)
    f = s.makefile("rw")
    json.loads(f.readline())
    def call(execute, **args):
        f.write(json.dumps({"execute": execute, "arguments": args}) + "\n"); f.flush()
        while True:
            r = json.loads(f.readline())
            if "return" in r or "error" in r:
                return r
    call("qmp_capabilities")
    out = [call(c, **a) for c, a in cmds]
    s.close()
    return out

def abs_ev(x, y):
    return [{"type": "abs", "data": {"axis": "x", "value": int(x * 32767 / W)}},
            {"type": "abs", "data": {"axis": "y", "value": int(y * 32767 / H)}}]

def click(x, y):
    qmp(("input-send-event", {"events": abs_ev(x, y)}))
    time.sleep(0.15)
    qmp(("input-send-event", {"events": [{"type": "btn", "data": {"down": True, "button": "left"}}]}))
    time.sleep(0.1)
    qmp(("input-send-event", {"events": [{"type": "btn", "data": {"down": False, "button": "left"}}]}))

KEYMAP = {" ": "spc", "\n": "ret", ".": "dot", "/": "slash", ":": "shift-semicolon", "-": "minus",
          "_": "shift-minus", "=": "equal", "?": "shift-slash"}

def key(combo):
    keys = [{"type": "qcode", "data": k} for k in combo.split("-")]
    qmp(("send-key", {"keys": keys}))

def type_text(text):
    for ch in text:
        if ch in KEYMAP:
            key(KEYMAP[ch])
        elif ch.isupper():
            key("shift-" + ch.lower())
        else:
            key(ch)
        time.sleep(0.05)

def shot(path):
    r = qmp(("screendump", {"filename": path, "format": "png"}))
    print(path, r[0])

if __name__ == "__main__":
    cmd, *a = sys.argv[1:]
    if cmd == "shot": shot(a[0])
    elif cmd == "click": click(float(a[0]), float(a[1]))
    elif cmd == "type": type_text(a[0])
    elif cmd == "key": key(a[0])
    elif cmd == "raw": print(qmp((a[0], json.loads(a[1]) if len(a) > 1 else {})))
    else: sys.exit("unknown command")
