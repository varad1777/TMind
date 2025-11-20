# simulator_with_register_control.py
import time
import random
import math
from threading import Thread
from pymodbus.server.sync import StartTcpServer
from pymodbus.device import ModbusDeviceIdentification
from pymodbus.datastore import ModbusSequentialDataBlock, ModbusSlaveContext, ModbusServerContext
from flask import Flask, jsonify, request
from flask_cors import CORS

# ---------- initial registers (pairs: high, 0) ----------
initial_regs_slave1 = [2200,0, 1500,0, 3000,0, 500,0, 20,0, 1000,0, 1800,0, 250,0]
initial_regs_slave2 = [2100,0, 1400,0, 2800,0, 490,0, 30,0, 900,0, 1600,0, 300,0]

# ---------- Build contexts ----------
store1 = ModbusSlaveContext(hr=ModbusSequentialDataBlock(0, initial_regs_slave1))
store2 = ModbusSlaveContext(hr=ModbusSequentialDataBlock(0, initial_regs_slave2))
stores = {1: store1, 2: store2}
context = ModbusServerContext(slaves=stores, single=False)

# ---------- Identity ----------
identity = ModbusDeviceIdentification()
identity.VendorName = 'modbus Simulator'
identity.ProductCode = 'MS'
identity.VendorUrl = 'https://example.com'
identity.ProductName = 'Python Modbus Simulator'
identity.ModelName = 'ModbusTCPv1'
identity.MajorMinorRevision = '1.0'

# ---------- Global control state ----------
_sim_state = {
    "paused": False,
    "update_interval": 0.005,
    "print_interval": 0.25,
    # map unit id -> base highs list (length 8)
    "base_highs": {
        1: [initial_regs_slave1[i] for i in range(0, len(initial_regs_slave1), 2)],
        2: [initial_regs_slave2[i] for i in range(0, len(initial_regs_slave2), 2)]
    },
    # per-unit set of stopped register indexes (0..7). If stopped, that signal's high word is set to 0 and not updated.
    "stopped_indices": {
        1: set(),
        2: set()
    },
    # per-unit fluctuation params (amplitudes list length 8, periods list length 8, jitter_scale float)
    "params": {
        1: {
            "amplitudes": [50, 30, 100, 10, 5, 80, 120, 20],
            "periods":    [8, 6, 12, 10, 3, 9, 7, 11],
            "jitter_scale": 0.02
        },
        2: {
            "amplitudes": [50, 30, 100, 10, 5, 80, 120, 20],
            "periods":    [8, 6, 12, 10, 3, 9, 7, 11],
            "jitter_scale": 0.02
        }
    },
    # active spikes: list of dicts {unit, idx, magnitude, end_time, kind}
    "spikes": []
}

# ---------- Simulator functions ----------
def simulate_slave(unit_id, update_interval):
    t0 = time.time()
    i = 0
    while True:
        if not _sim_state["paused"]:
            t = time.time() - t0
            new_regs = []
            base_highs = _sim_state["base_highs"].get(unit_id, [0]*8)
            stopped = _sim_state["stopped_indices"].get(unit_id, set())
            params = _sim_state["params"].get(unit_id, {})
            amplitudes = params.get("amplitudes", [50]*8)
            periods = params.get("periods", [10]*8)
            jitter_scale = params.get("jitter_scale", 0.02)

            # collect active spikes for this unit
            now = time.time()
            active_spikes = [s for s in _sim_state["spikes"] if s["unit"] == unit_id and s["end_time"] > now]

            for idx, base in enumerate(base_highs):
                if idx in stopped:
                    new_high = 0
                else:
                    amp = amplitudes[idx % len(amplitudes)]
                    period = periods[idx % len(periods)]
                    phase = (idx * 0.13) + (i * 0.0001)
                    delta = int(amp * math.sin(2 * math.pi * t / period + phase) + random.uniform(-amp*jitter_scale, amp*jitter_scale))
                    new_high = max(0, base + delta)

                    # apply spikes (additive). There can be multiple spikes; sum magnitudes or apply types.
                    for sp in active_spikes:
                        if sp["idx"] == idx:
                            # simple additive spike; magnitude is integer to add to raw high
                            new_high = max(0, new_high + int(sp.get("magnitude", 0)))

                new_regs.append(new_high)
                new_regs.append(0)
            try:
                context[unit_id].setValues(3, 0, new_regs)
            except Exception as ex:
                print(f"Simulator: failed to set values for unit {unit_id}: {ex}")
        i += 1
        time.sleep(update_interval)

def pretty_monitor(print_interval):
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
        now = time.time()
        for unit_id in stores.keys():
            try:
                regs = context[unit_id].getValues(3, 0, count=16)
            except Exception:
                regs = []
            print("="*60)
            print(f"Slave {unit_id} @ {time.strftime('%H:%M:%S', time.localtime(now))}  (paused={_sim_state['paused']})")
            if regs:
                for i in range(0, 16, 2):
                    idx = i // 2
                    raw_high = regs[i]
                    scaled = raw_high / 100.0
                    stopped_flag = idx in _sim_state["stopped_indices"].get(unit_id, set())
                    stopped_text = " [STOPPED]" if stopped_flag else ""
                    print(f"{signal_names[idx]:<18} | {scaled:8.3f}  (regs {i},{i+1} => {raw_high}){stopped_text}")
            else:
                print("no registers available")
            # show active spikes for the unit
            now = time.time()
            unit_spikes = [s for s in _sim_state["spikes"] if s["unit"] == unit_id and s["end_time"] > now]
            if unit_spikes:
                print(" Active spikes:")
                for sp in unit_spikes:
                    remaining = max(0, sp["end_time"] - now)
                    print(f"  - idx={sp['idx']} mag={sp['magnitude']} remaining={remaining:.2f}s kind={sp.get('kind')}")
            print("="*60)
        time.sleep(print_interval)

# ---------- Start Modbus server (blocking) ----------
def start_modbus_server(bind_host, bind_port):
    print(f"Starting Modbus TCP server on {bind_host}:{bind_port}")
    StartTcpServer(context, identity=identity, address=(bind_host, bind_port))

# ---------- Flask API ----------
app = Flask("modbus-sim-api")
CORS(app)

@app.route("/api/status", methods=["GET"])
def api_status():
    data = {
        "paused": _sim_state["paused"],
        "update_interval": _sim_state["update_interval"],
        "print_interval": _sim_state["print_interval"],
        "stopped_indices": {u: sorted(list(s)) for u,s in _sim_state["stopped_indices"].items()},
        "params": _sim_state["params"],
        # return only active spikes
        "spikes": [s for s in _sim_state["spikes"] if s["end_time"] > time.time()]
    }
    units = {}
    for unit in stores.keys():
        try:
            regs = context[unit].getValues(3, 0, count=16)
        except Exception:
            regs = []
        units[unit] = regs
    data["units"] = units
    return jsonify(data)

@app.route("/api/units/<int:unit_id>/regs", methods=["GET"])
def api_get_regs(unit_id):
    try:
        regs = context[unit_id].getValues(3, 0, count=16)
    except Exception:
        regs = []
    stopped = sorted(list(_sim_state["stopped_indices"].get(unit_id, set())))
    return jsonify({"unit": unit_id, "regs": regs, "stopped": stopped})

@app.route("/api/units/<int:unit_id>/stop/<int:idx>", methods=["POST"])
def api_stop_register(unit_id, idx):
    if unit_id not in _sim_state["stopped_indices"]:
        _sim_state["stopped_indices"][unit_id] = set()
    _sim_state["stopped_indices"][unit_id].add(idx)
    try:
        regs = context[unit_id].getValues(3, 0, count=16)
        regs[2*idx] = 0
        context[unit_id].setValues(3, 0, regs)
    except Exception:
        pass
    return jsonify({"ok": True, "unit": unit_id, "stopped_index": idx})

@app.route("/api/units/<int:unit_id>/start/<int:idx>", methods=["POST"])
def api_start_register(unit_id, idx):
    if unit_id in _sim_state["stopped_indices"]:
        _sim_state["stopped_indices"][unit_id].discard(idx)
    return jsonify({"ok": True, "unit": unit_id, "started_index": idx})

@app.route("/api/units/<int:unit_id>/base", methods=["POST"])
def api_set_base(unit_id):
    payload = request.json or {}
    base_highs = payload.get("base_highs")
    if not isinstance(base_highs, list) or len(base_highs) != 8:
        return jsonify({"error": "base_highs must be an array of 8 integers"}), 400
    _sim_state["base_highs"][unit_id] = [int(x) for x in base_highs]
    return jsonify({"ok": True, "unit": unit_id, "base_highs": _sim_state["base_highs"][unit_id]})

@app.route("/api/pause", methods=["POST"])
def api_pause():
    payload = request.json or {}
    paused = payload.get("paused")
    if paused is None:
        return jsonify({"error": "missing 'paused' boolean in json body"}), 400
    _sim_state["paused"] = bool(paused)
    return jsonify({"ok": True, "paused": _sim_state["paused"]})

@app.route("/api/units/<int:unit_id>/params", methods=["GET", "POST"])
def api_params(unit_id):
    if request.method == "GET":
        return jsonify(_sim_state["params"].get(unit_id, {}))
    payload = request.json or {}
    # optional keys: amplitudes (list of 8 ints), periods (list of 8 ints), jitter_scale (float)
    if "amplitudes" in payload:
        amps = payload["amplitudes"]
        if not isinstance(amps, list) or len(amps) != 8:
            return jsonify({"error": "amplitudes must be list of 8 numbers"}), 400
        _sim_state["params"].setdefault(unit_id, {})["amplitudes"] = [int(x) for x in amps]
    if "periods" in payload:
        p = payload["periods"]
        if not isinstance(p, list) or len(p) != 8:
            return jsonify({"error": "periods must be list of 8 numbers"}), 400
        _sim_state["params"].setdefault(unit_id, {})["periods"] = [float(x) for x in p]
    if "jitter_scale" in payload:
        js = float(payload["jitter_scale"])
        _sim_state["params"].setdefault(unit_id, {})["jitter_scale"] = js
    return jsonify({"ok": True, "params": _sim_state["params"].get(unit_id)})

@app.route("/api/units/<int:unit_id>/spike", methods=["POST"])
def api_spike(unit_id):
    """
    Body JSON:
    {
      "idx": 2,                 # signal index 0..7
      "magnitude": 2000,       # additive to raw high register (integer)
      "duration_ms": 2000,     # duration in milliseconds
      "kind": "burst"          # optional label
    }
    """
    payload = request.json or {}
    idx = payload.get("idx")
    magnitude = int(payload.get("magnitude", 0))
    duration_ms = int(payload.get("duration_ms", 1000))
    kind = payload.get("kind", "burst")
    if idx is None or not (0 <= int(idx) <= 7):
        return jsonify({"error": "idx must be 0..7"}), 400
    now = time.time()
    end_time = now + (duration_ms / 1000.0)
    sp = {"unit": unit_id, "idx": int(idx), "magnitude": magnitude, "end_time": end_time, "kind": kind}
    _sim_state["spikes"].append(sp)
    # return active spikes
    active = [s for s in _sim_state["spikes"] if s["end_time"] > now]
    return jsonify({"ok": True, "spike": sp, "active_spikes": active})

@app.route("/api/throw-starts", methods=["GET", "POST", "DELETE"])
def api_throw_starts():
    # kept for backwards compatibility; earlier code used this but not actively used in simulation.
    if request.method == "GET":
        return jsonify(sorted(list(_sim_state.get("force_throw_starts", set()))))
    if request.method == "POST":
        payload = request.json or {}
        start = payload.get("start")
        if start is None:
            return jsonify({"error": "missing 'start'"}), 400
        _sim_state.setdefault("force_throw_starts", set()).add(int(start))
        return jsonify({"ok": True, "force_throw_starts": sorted(list(_sim_state["force_throw_starts"]))})
    if request.method == "DELETE":
        payload = request.json or {}
        start = payload.get("start")
        if start is None:
            _sim_state.setdefault("force_throw_starts", set()).clear()
        else:
            _sim_state.setdefault("force_throw_starts", set()).discard(int(start))
        return jsonify({"ok": True, "force_throw_starts": sorted(list(_sim_state.get("force_throw_starts", set()))) })

# ---------- Main runner ----------
def main():
    import argparse
    p = argparse.ArgumentParser()
    p.add_argument("--modbus-host", default="0.0.0.0")
    p.add_argument("--modbus-port", default=5020, type=int)
    p.add_argument("--api-host", default="127.0.0.1")
    p.add_argument("--api-port", default=8000, type=int)
    args = p.parse_args()

    _sim_state["update_interval"] = 0.005
    _sim_state["print_interval"] = 0.25

    Thread(target=start_modbus_server, args=(args.modbus_host, args.modbus_port), daemon=True).start()
    Thread(target=simulate_slave, args=(1, _sim_state["update_interval"]), daemon=True).start()
    Thread(target=simulate_slave, args=(2, _sim_state["update_interval"]), daemon=True).start()
    Thread(target=pretty_monitor, args=(_sim_state["print_interval"],), daemon=True).start()

    def run_flask():
        print(f"Control API: http://{args.api_host}:{args.api_port}")
        app.run(host=args.api_host, port=args.api_port, debug=False, use_reloader=False)

    Thread(target=run_flask, daemon=True).start()

    try:
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        print("Shutting down simulator")

if __name__ == "__main__":
    main()
