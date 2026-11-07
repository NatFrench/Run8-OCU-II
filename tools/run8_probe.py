"""Talk to a live Run8 directly: send input messages and print telemetry.

    python tools/run8_probe.py [--port 18888] [--watch 5] [TYPE=VALUE ...]

Each TYPE=VALUE pair is sent once (e.g. 18=0 for train brake release), then telemetry is printed
once a second for --watch seconds. Close the overlay first; it holds the telemetry port.
"""

import argparse
import socket
import time


def crc(data):
    c = data[0]
    for b in data[1:]:
        c ^= b
    return c


def speed(b):
    return (b - 128) / 10 if b >= 128 else b


def describe(p):
    s1, s2 = p[15], p[23]
    flags1 = [n for bit, n in [(1, "park"), (2, "slip"), (4, "PCS"), (8, "sand"), (16, "alerter-pre"),
                               (32, "alerter"), (64, "horn"), (128, "bell")] if s1 & bit]
    flags2 = [n for bit, n in [(1, "penalty"), (2, "dyn"), (4, "compressor")] if s2 & bit]
    return (f"spd {speed(p[1]):5.1f}  MR {p[2]:3}  ER {p[3]:3}  BP {p[4]:3}  BC {p[6]:3}  flow {p[7]:3}  "
            f"notch {p[8]}  rev {p[20]:3}  feed {p[25]:3}  {' '.join(flags1 + flags2)}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=18888)
    ap.add_argument("--watch", type=float, default=5)
    ap.add_argument("--audio", action="store_true")
    ap.add_argument("sends", nargs="*")
    args = ap.parse_args()

    rx = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    rx.bind(("127.0.0.1", args.port + 1))
    rx.settimeout(0.5)
    tx = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    for item in args.sends:
        kind, value = (int(x) for x in item.split("="))
        msg = bytearray([224 if args.audio else 96, kind >> 8, kind & 0xFF, value])
        msg.append(crc(msg))
        tx.sendto(bytes(msg), ("127.0.0.1", args.port))
        print(f"sent {kind}={value}", flush=True)

    end = time.monotonic() + args.watch
    next_print = 0.0
    while time.monotonic() < end:
        try:
            p = rx.recv(64)
        except socket.timeout:
            continue
        if len(p) < 27 or p[0] != 97 or crc(p[:-1]) != p[-1]:
            continue
        now = time.monotonic()
        if now >= next_print:
            next_print = now + 1
            print(describe(p), flush=True)


if __name__ == "__main__":
    main()
