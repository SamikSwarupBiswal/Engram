#!/usr/bin/env python3
"""
Engram KV Cache Experiments — Controlled Causal Validation

Tests the hypothesis: KV cache accumulation across requests causes survivability collapse.

Experiments:
  baseline  — No KV clearing (expect collapse at ~2000 tokens)
  clear     — Clear KV cache after each request (expect no collapse)
  fresh     — Fresh context per request (expect no collapse, measure overhead)
  unload    — Unload/reload model after collapse (test recovery)

Usage:
  python3 kv_experiments.py baseline 200
  python3 kv_experiments.py clear 200
  python3 kv_experiments.py fresh 200
  python3 kv_experiments.py unload 200
"""
import subprocess, json, time, sys, os
from datetime import datetime

BASE = "http://127.0.0.1:5000"
RESULTS_DIR = "/tmp/engram_kv_experiments"

PROMPTS = [
    "What is 2+2?",
    "Name three colors.",
    "What day comes after Monday?",
    "What is the capital of France?",
    "Explain gravity briefly.",
    "What is 10*10?",
    "Name a programming language.",
    "What season comes after winter?",
    "What is H2O?",
    "Summarize the concept of memory in one sentence.",
    "What is the boiling point of water?",
    "Name a mammal.",
    "What color is the sky?",
    "How many days in a week?",
    "What is 5*7?",
    "Name an ocean.",
    "What comes after Friday?",
    "What is photosynthesis?",
    "Name a planet in our solar system.",
    "What is 100/4?",
]


def api(method, path, body=None, timeout=120):
    """Make an API call."""
    cmd = ["curl", "-s", "-X", method, f"{BASE}{path}"]
    if body:
        cmd += ["-H", "Content-Type: application/json", "-d", json.dumps(body)]
    r = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout)
    try:
        return json.loads(r.stdout)
    except:
        return {"_raw": r.stdout, "_error": r.stderr}


def health():
    return api("GET", "/api/health")


def set_experiment_mode(clear_kv=None, fresh_ctx=None):
    body = {}
    if clear_kv is not None:
        body["clearKvCache"] = clear_kv
    if fresh_ctx is not None:
        body["freshContext"] = fresh_ctx
    return api("POST", "/api/experiment/mode", body)


def get_kv_status():
    return api("GET", "/api/experiment/kv-status")


def clear_kv():
    return api("POST", "/api/experiment/clear-kv")


def chat(msg, max_tokens=64, timeout=120):
    body = {"messages": [{"role": "user", "content": msg}], "maxTokens": max_tokens}
    start = time.time()
    try:
        data = api("POST", "/v1/chat/completions", body, timeout=timeout)
        elapsed = time.time() - start

        if "_raw" in data:
            return {"success": False, "finish": "parse_error", "tokens": 0,
                    "elapsed": round(elapsed, 3), "tps": 0, "content": str(data)[:60]}

        choices = data.get("choices", [])
        if not choices:
            return {"success": False, "finish": "no_choices", "tokens": 0,
                    "elapsed": round(elapsed, 3), "tps": 0, "content": "NO CHOICES"}

        content = choices[0].get("message", {}).get("content", "")
        finish = choices[0].get("finish_reason", "unknown")
        usage = data.get("usage", {})
        toks = usage.get("completion_tokens", 0)
        tps = toks / elapsed if elapsed > 0 else 0
        kv = data.get("_kv", {})

        return {
            "success": finish == "stop" and toks > 0,
            "finish": finish,
            "tokens": toks,
            "elapsed": round(elapsed, 3),
            "tps": round(tps, 2),
            "content": content[:60],
            "kv_tokens_before": kv.get("tokensBefore", -1),
            "kv_tokens_after": kv.get("tokensAfter", -1),
            "kv_cells_before": kv.get("cellsBefore", -1),
            "kv_cells_after": kv.get("cellsAfter", -1),
            "used_fresh_ctx": kv.get("usedFreshContext", False),
            "cleared_kv": kv.get("clearedKvCache", False),
        }
    except subprocess.TimeoutExpired:
        return {"success": False, "finish": "timeout", "tokens": 0,
                "elapsed": timeout, "tps": 0, "content": "TIMEOUT"}
    except Exception as e:
        return {"success": False, "finish": "error", "tokens": 0,
                "elapsed": round(time.time() - start, 3), "tps": 0, "content": str(e)[:60]}


def get_process_rss():
    try:
        r = subprocess.run(["pgrep", "-f", "Engram.Api.*--urls"], capture_output=True, text=True)
        for pid in r.stdout.strip().split("\n"):
            pid = pid.strip()
            if pid and pid.isdigit():
                with open(f"/proc/{pid}/status") as f:
                    for line in f:
                        if line.startswith("VmRSS:"):
                            return int(line.split()[1]) / 1024  # MB
    except:
        pass
    return 0


def log(msg, file=None):
    ts = datetime.now().strftime("%H:%M:%S")
    line = f"[{ts}] {msg}"
    print(line, flush=True)
    if file:
        file.write(line + "\n")
        file.flush()


def run_experiment(mode, request_count, max_tokens=64):
    """Run a single experiment."""
    os.makedirs(RESULTS_DIR, exist_ok=True)
    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    result_file = os.path.join(RESULTS_DIR, f"{mode}_{timestamp}.json")
    progress_file = os.path.join(RESULTS_DIR, f"{mode}_{timestamp}.log")

    pf = open(progress_file, "w")

    # Preflight
    h = health()
    if h.get("state") != "Ready":
        log(f"ABORT: API not ready (state={h.get('state')})", pf)
        pf.close()
        return None

    log(f"=== EXPERIMENT: {mode.upper()} ===", pf)
    log(f"Backend: {h.get('backend')} | Model: {h.get('modelName')}", pf)
    log(f"Requests: {request_count}, MaxTokens: {max_tokens}", pf)

    # Configure experiment mode
    if mode == "baseline":
        set_experiment_mode(clear_kv=False, fresh_ctx=False)
        log("Mode: BASELINE (no KV clearing, shared context)", pf)
    elif mode == "clear":
        set_experiment_mode(clear_kv=True, fresh_ctx=False)
        log("Mode: CLEAR KV after each request", pf)
    elif mode == "fresh":
        set_experiment_mode(clear_kv=False, fresh_ctx=True)
        log("Mode: FRESH CONTEXT per request", pf)
    elif mode == "unload":
        set_experiment_mode(clear_kv=False, fresh_ctx=False)
        log("Mode: UNLOAD/RELOAD RECOVERY test", pf)
    else:
        log(f"Unknown mode: {mode}", pf)
        pf.close()
        return None

    # Get initial KV state
    kv_init = get_kv_status()
    log(f"Initial KV: tokens={kv_init.get('kvTokensInCache')}, cells={kv_init.get('kvUsedCells')}", pf)

    # Run requests
    results = []
    collapse_detected = False
    collapse_point = None
    consecutive_failures = 0
    start_time = time.time()

    for i in range(request_count):
        prompt = PROMPTS[i % len(PROMPTS)]
        r = chat(prompt, max_tokens=max_tokens)
        results.append(r)

        if r["success"]:
            consecutive_failures = 0
        else:
            consecutive_failures += 1

        # Log every 10 requests
        if i % 10 == 0 or (not r["success"] and consecutive_failures <= 3):
            recent = results[-min(10, len(results)):]
            recent_success = [x for x in recent if x["success"]]
            avg_tps = sum(x["tps"] for x in recent_success) / max(1, len(recent_success))
            rss = get_process_rss()
            kv_info = ""
            if "kv_tokens_before" in r and r["kv_tokens_before"] >= 0:
                kv_info = f" kv:{r.get('kv_tokens_before', '?')}->{r.get('kv_tokens_after', '?')}"
            log(f"[{i:4d}/{request_count}] tok/s:{avg_tps:.1f} "
                f"success:{len(recent_success)}/{len(recent)} "
                f"rss:{rss:.0f}MB elapsed:{r['elapsed']:.1f}s{kv_info}", pf)

        # Detect collapse
        if consecutive_failures >= 3 and not collapse_detected:
            collapse_detected = True
            collapse_point = i - 2  # First of 3 consecutive failures
            log(f"*** COLLAPSE DETECTED at request {collapse_point} ***", pf)

        # For unload experiment: after collapse, unload/reload and continue
        if mode == "unload" and collapse_detected and i == collapse_point + 2:
            log("--- COLLAPSE CONFIRMED. Testing unload/reload recovery ---", pf)
            log("Unloading model...", pf)
            api("POST", "/api/model/unload")
            time.sleep(2)
            log("Reloading model...", pf)
            api("POST", "/api/model/load")
            # Wait for ready
            for _ in range(30):
                time.sleep(2)
                h2 = health()
                if h2.get("state") == "Ready":
                    log("Model reloaded. Resuming requests...", pf)
                    break
                log(f"  Waiting... state={h2.get('state')}", pf)
            else:
                log("TIMEOUT waiting for model reload", pf)
            # Reset failure counter
            consecutive_failures = 0
            collapse_detected = False

        # Brief pause every 50 requests
        if i % 50 == 49:
            time.sleep(1)

    elapsed_total = time.time() - start_time

    # Analysis
    successes = [r for r in results if r["success"]]
    failures = [r for r in results if not r["success"]]
    timeouts = [r for r in results if r["finish"] == "timeout"]

    # Token accumulation tracking
    kv_timeline = [(i, r.get("kv_tokens_before", -1), r.get("kv_tokens_after", -1))
                   for i, r in enumerate(results)
                   if r.get("kv_tokens_before", -1) >= 0]

    # Drift analysis (only on successful requests)
    n = len(successes)
    if n >= 5:
        first_chunk = successes[:max(1, n // 5)]
        last_chunk = successes[-max(1, n // 5):]
        first_avg = sum(r["tps"] for r in first_chunk) / len(first_chunk)
        last_avg = sum(r["tps"] for r in last_chunk) / len(last_chunk)
        drift_pct = ((first_avg - last_avg) / first_avg * 100) if first_avg > 0 else 0
    else:
        first_avg = last_avg = drift_pct = 0

    all_lats = sorted([r["elapsed"] for r in successes])
    p50 = all_lats[len(all_lats) // 2] if all_lats else 0
    p95 = all_lats[int(len(all_lats) * 0.95)] if all_lats else 0

    rss_final = get_process_rss()

    report = {
        "experiment": mode,
        "timestamp": datetime.now().isoformat(),
        "config": {"request_count": request_count, "max_tokens": max_tokens,
                    "backend": h.get("backend"), "model": h.get("modelName")},
        "results": {
            "total": len(results),
            "success": len(successes),
            "failure": len(failures),
            "timeout": len(timeouts),
            "success_rate": round(len(successes) / max(1, len(results)), 4),
            "collapse_detected": collapse_detected or (collapse_point is not None),
            "collapse_at_request": collapse_point,
        },
        "performance": {
            "first_20pct_tps": round(first_avg, 2),
            "last_20pct_tps": round(last_avg, 2),
            "drift_pct": round(drift_pct, 1),
            "overall_avg_tps": round(sum(r["tps"] for r in successes) / max(1, len(successes)), 2),
        },
        "latency": {
            "avg": round(sum(r["elapsed"] for r in successes) / max(1, len(successes)), 3),
            "p50": round(p50, 3),
            "p95": round(p95, 3),
        },
        "memory": {"rss_final_mb": round(rss_final, 0)},
        "kv_timeline_sample": kv_timeline[:20],  # First 20 data points
        "elapsed_total_s": round(elapsed_total, 1),
        "raw_results": results,
    }

    with open(result_file, "w") as f:
        json.dump(report, f, indent=2)

    # Summary
    log(f"\n=== RESULTS: {mode.upper()} ===", pf)
    log(f"Total: {len(results)} | Success: {len(successes)} | Failure: {len(failures)}", pf)
    log(f"Success rate: {len(successes)/max(1,len(results))*100:.1f}%", pf)
    log(f"Tok/s: {first_avg:.1f} -> {last_avg:.1f} ({drift_pct:+.1f}% drift)", pf)
    log(f"Latency: avg={sum(r['elapsed'] for r in successes)/max(1,len(successes)):.1f}s p50={p50:.1f}s p95={p95:.1f}s", pf)
    log(f"RSS: {rss_final:.0f}MB", pf)
    log(f"Elapsed: {elapsed_total:.0f}s", pf)

    if collapse_point is not None:
        log(f"COLLAPSE at request {collapse_point}", pf)
    else:
        log("NO COLLAPSE DETECTED", pf)

    # Classification
    if len(successes) / max(1, len(results)) < 0.8:
        log("CLASSIFICATION: CATASTROPHIC", pf)
    elif drift_pct > 20:
        log("CLASSIFICATION: DEGRADING", pf)
    else:
        log("CLASSIFICATION: STABLE", pf)

    log(f"\nResults: {result_file}", pf)
    log(f"Progress: {progress_file}", pf)
    pf.close()

    # Print summary to stdout too
    print(f"\n{'='*50}")
    print(f"EXPERIMENT: {mode.upper()}")
    print(f"Success: {len(successes)}/{len(results)} ({len(successes)/max(1,len(results))*100:.1f}%)")
    if collapse_point is not None:
        print(f"COLLAPSE at request {collapse_point}")
    else:
        print("NO COLLAPSE")
    print(f"Tok/s: {first_avg:.1f} -> {last_avg:.1f}")
    print(f"{'='*50}\n")

    return report


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python3 kv_experiments.py <mode> [request_count]")
        print("Modes: baseline, clear, fresh, unload")
        sys.exit(1)

    mode = sys.argv[1]
    count = int(sys.argv[2]) if len(sys.argv) > 2 else 200

    report = run_experiment(mode, count)
    if report:
        sys.exit(0 if report["results"]["success_rate"] > 0.5 else 1)
    sys.exit(1)
