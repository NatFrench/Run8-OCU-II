"""Stand-in for Run8's Custom Input Device interface, for testing the overlay without the sim.

Listens for 5-byte input packets on 127.0.0.1:PORT and sends 27-byte telemetry packets to
127.0.0.1:PORT+1 ten times a second, using a crude single-locomotive physics model.

    python tools/fake_run8.py [--port 18888] [--grade 0.0] [--seconds 0]
"""

import argparse
import socket
import time

NAMES = {
    1: "Alerter", 2: "Bell", 4: "DynBrake", 5: "HeadlightFront", 6: "HeadlightRear", 8: "Horn",
    9: "IndyBrake", 10: "Bailoff", 14: "Reverser", 15: "Sander", 16: "Throttle", 18: "TrainBrake",
}

FEED = 90
MR = 135


def crc(data):
    c = data[0]
    for b in data[1:]:
        c ^= b
    return c


def clamp(v, lo, hi):
    return max(lo, min(hi, v))


def toward(value, target, rate, dt):
    step = rate * dt
    return target if abs(target - value) <= step else value + step * (1 if target > value else -1)


class Loco:
    def __init__(self, grade):
        self.grade = grade
        self.controls = {9: 255, 14: 128, 16: 0, 18: 0}
        self.speed = 0.0
        self.accel = 0.0
        self.er = float(FEED)
        self.bp = float(FEED)
        self.bc = 72.0
        self.airflow = 0.0

    def er_target(self):
        handle = self.controls[18]
        if handle >= 255:
            return 0.0
        if handle < 26:
            return float(FEED)
        return FEED - clamp(7 + (handle - 26) * 19 / 114, 7, 26)

    def step(self, dt):
        target = self.er_target()
        self.er = toward(self.er, target, 30 if target == 0 else 8, dt)
        charging = self.er > self.bp
        self.bp = toward(self.bp, self.er, 2 if charging else (60 if target == 0 else 6), dt)
        self.airflow = clamp((self.er - self.bp) * 6, 0, 120) if charging else 0.0

        train_bc = 72.0 if self.bp < 10 else clamp((FEED - self.bp) * 2.5, 0, 64)
        indy_bc = self.controls[9] / 255 * 72
        self.bc = toward(self.bc, max(train_bc, indy_bc), 20, dt)

        reverser = self.controls[14]
        tractive = 0.0 if reverser not in (0, 255) else self.controls[16] * 0.12
        braking = self.bc / 72 * 1.2 + 0.01
        accel = tractive - self.grade * 0.2 - braking
        if self.speed <= 0:
            accel = max(0.0, accel)
        self.speed = max(0.0, self.speed + accel * dt)
        self.accel = accel if self.speed > 0 else 0.0

    def packet(self):
        p = bytearray(27)
        p[0] = 97
        p[1] = 128 + round(self.speed * 10) if self.speed < 12.7 else clamp(round(self.speed), 0, 127)
        p[2] = MR
        p[3] = round(self.er)
        p[4] = round(self.bp)
        p[5] = round(self.bp)
        p[6] = round(self.bc)
        p[7] = round(self.airflow)
        p[8] = self.controls[16]
        p[9] = round(self.bp)
        p[10] = 255
        p[19] = clamp(round((self.accel * 60 / 100 + 1) / 2 * 255), 0, 255)
        p[20] = self.controls[14]
        p[22] = 30 + self.controls[16] * 8
        p[23] = 4
        p[25] = FEED
        p[26] = crc(p[:26])
        return bytes(p)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=18888)
    ap.add_argument("--grade", type=float, default=0.0, help="percent, positive is uphill")
    ap.add_argument("--seconds", type=float, default=0, help="stop after this long (0 = run forever)")
    args = ap.parse_args()

    rx = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    rx.bind(("127.0.0.1", args.port))
    rx.setblocking(False)
    tx = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    loco = Loco(args.grade)
    print(f"Fake Run8 listening on {args.port}, sending telemetry to {args.port + 1}", flush=True)
    start = last = time.monotonic()
    next_status = start
    while not args.seconds or time.monotonic() - start < args.seconds:
        while True:
            try:
                data = rx.recv(64)
            except BlockingIOError:
                break
            except ConnectionResetError:
                continue
            if len(data) != 5 or data[0] not in (96, 224) or crc(data[:4]) != data[4]:
                print(f"bad packet {list(data)}", flush=True)
                continue
            kind, value = (data[1] << 8) | data[2], data[3]
            if loco.controls.get(kind) != value:
                print(f"{time.monotonic() - start:7.1f}s  {NAMES.get(kind, kind)} = {value}", flush=True)
            loco.controls[kind] = value

        now = time.monotonic()
        loco.step(now - last)
        last = now
        try:
            tx.sendto(loco.packet(), ("127.0.0.1", args.port + 1))
        except ConnectionResetError:
            pass
        if now >= next_status:
            next_status = now + 5
            print(f"{now - start:7.1f}s  speed {loco.speed:5.2f} mph  ER {loco.er:5.1f}  BP {loco.bp:5.1f}  "
                  f"BC {loco.bc:4.1f}  flow {loco.airflow:5.1f}", flush=True)
        time.sleep(0.1)


if __name__ == "__main__":
    main()
