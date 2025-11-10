# modbus_server_monitor_fast.py
import time
import random
import math
import argparse
from threading import Thread
from pymodbus.server.sync import StartTcpServer
from pymodbus.device import ModbusDeviceIdentification
from pymodbus.datastore import ModbusSequentialDataBlock, ModbusSlaveContext, ModbusServerContext

def build_args():
    p = argparse.ArgumentParser(description="Modbus dynamic server simulator (fast mode).")
    p.add_argument("--update-interval", type=float, default=0.005,  # 200 Hz
                   help="Seconds between register updates (default 0.005s -> 200Hz).")
    p.add_argument("--print-interval", type=float, default=0.25,
                   help="Seconds between console prints (default 0.25s). Set larger to reduce console load.")
    p.add_argument("--host", type=str, default="localhost", help="Bind host (default localhost)")
    p.add_argument("--port", type=int, default=5020, help="Bind port (default 5020)")
    return p.parse_args()

# initial data: 16 registers (two registers per signal: high, low)
initial_regs = [
    2200, 0,   # Voltage -> 22.00
    1500, 0,   # Current -> 15.00
    3000, 0,   # Temperature -> 30.00
    500, 0,    # Frequency -> 5.00
    20, 0,     # Vibration -> 0.2 (scaled)
    1000, 0,   # FlowRate -> 10.00
    1800, 0,   # RPM -> 18.00
    250, 0     # Torque -> 2.5
]

# create store & context
store = ModbusSlaveContext(hr=ModbusSequentialDataBlock(0, initial_regs))
context = ModbusServerContext(slaves=store, single=True)

identity = ModbusDeviceIdentification()
identity.VendorName = 'Varad Simulator'
identity.ProductCode = 'VS'
identity.VendorUrl = 'https://example.com'
identity.ProductName = 'Python Modbus Simulator'
identity.ModelName = 'ModbusTCPv1'
identity.MajorMinorRevision = '1.0'

def start_server(bind_host, bind_port):
    # StartTcpServer blocks — so run it in a background thread
    StartTcpServer(context, identity=identity, address=(bind_host, bind_port))

def simulate_changes(update_interval, jitter_scale=0.02):
    """
    Fast updater: writes new register values at `update_interval` seconds.
    jitter_scale reduces random jitter relative to amplitude.
    """
    base_highs = [initial_regs[i] for i in range(0, len(initial_regs), 2)]
    amplitudes = [50, 30, 100, 10, 5, 80, 120, 20]
    periods = [8, 6, 12, 10, 3, 9, 7, 11]

    t0 = time.time()
    i = 0
    while True:
        t = time.time() - t0
        new_regs = []
        # produce values; added small phase offset per signal so they don't all move in lockstep
        for idx, base in enumerate(base_highs):
            amp = amplitudes[idx]
            period = periods[idx]
            phase = (idx * 0.13) + (i * 0.0001)
            delta = int(amp * math.sin(2 * math.pi * t / period + phase) + random.uniform(-amp*jitter_scale, amp*jitter_scale))
            new_high = max(0, base + delta)
            new_regs.append(new_high)
            new_regs.append(0)
        # write to holding registers
        context[0].setValues(3, 0, new_regs)
        i += 1
        # busy-wait guard: time.sleep is used; for super-tight loops use lower-level timing
        time.sleep(update_interval)

def pretty_monitor(print_interval):
    # allow server to start
    time.sleep(0.05)
    signal_names = [
        "Voltage (x0.01 V)",
        "Current (x0.01 A)",
        "Temperature (x0.01 °C)",
        "Frequency (x0.01 Hz)",
        "Vibration",
        "FlowRate (x0.01 L/min)",
        "RPM (x0.01 rpm)",
        "Torque"
    ]
    while True:
        regs = context[0].getValues(3, 0, count=16)
        # very compact print (one line per signal)
        now = time.time()
        lines = []
        for i in range(0, 16, 2):
            idx = i // 2
            raw_high = regs[i]
            scaled = raw_high / 100.0
            lines.append(f"{signal_names[idx]:<18} | {scaled:8.3f}  (regs {i},{i+1} => {raw_high})")
        # print all at once to reduce interleaving
        print("="*70)
        print(f"Fast Monitor @ {time.strftime('%H:%M:%S', time.localtime(now))} (prints every {print_interval}s)")
        print("-"*70)
        print("\n".join(lines))
        print("="*70)
        time.sleep(print_interval)

if __name__ == "__main__":
    args = build_args()

    # ensure initial state
    context[0].setValues(3, 0, initial_regs)

    # start server
    t_server = Thread(target=start_server, args=(args.host, args.port), daemon=True)
    t_server.start()

    # start fast simulator
    t_sim = Thread(target=simulate_changes, args=(args.update_interval,), daemon=True)
    t_sim.start()

    # start fast monitor (main thread)
    pretty_monitor(args.print_interval)
