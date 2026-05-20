#!/usr/bin/env python3
"""
Quick 50-Request Soak Test (CPU speed optimized)
================================================
Runs fewer requests to complete within timeout on slow CPU inference.
"""

import requests
import json
import time
from datetime import datetime
from typing import Dict, List

BASE_URL = "http://127.0.0.1:5000"

def get_health() -> Dict:
    resp = requests.get(f"{BASE_URL}/api/health", timeout=10)
    return resp.json()

def send_request(prompt: str, max_tokens: int = 50) -> Dict:
    try:
        resp = requests.post(
            f"{BASE_URL}/v1/chat/completions",
            json={"messages": [{"role": "user", "content": prompt}], "max_tokens": max_tokens},
            timeout=60
        )
        return resp.json()
    except Exception as e:
        return {"error": str(e)}

def main():
    print("Quick 50-Request Soak Test")
    print(f"Started: {datetime.utcnow().isoformat()}")
    
    # Check health
    health = get_health()
    print(f"Initial State: {health['state']}")
    if health['state'] != 'Ready':
        print(f"ERROR: API not ready")
        return
    
    results = []
    cleanup_success = 0
    cleanup_failed = 0
    cleanup_verification_failed = 0
    
    for i in range(50):
        start = time.time()
        result = send_request(f"Say hello. Request #{i+1}", max_tokens=30)
        duration = time.time() - start
        
        kv = result.get("_kv", {})
        cleanup_result = kv.get("cleanupResult", "unknown")
        
        if cleanup_result == "Success":
            cleanup_success += 1
        elif cleanup_result == "Failed":
            cleanup_failed += 1
        elif cleanup_result == "VerificationFailed":
            cleanup_verification_failed += 1
        
        success = "error" not in result
        results.append({
            "id": i + 1,
            "success": success,
            "duration_s": duration,
            "cleanup": cleanup_result,
            "kv_before": kv.get("tokensBefore", -1),
            "kv_after_cleanup": kv.get("tokensAfterCleanup", -1)
        })
        
        if i % 10 == 0:
            print(f"  Request {i+1}/50: {'OK' if success else 'FAIL'} ({duration:.1f}s) cleanup={cleanup_result}")
    
    # Analysis
    print(f"\n{'='*60}")
    print("RESULTS")
    print(f"{'='*60}")
    
    successful = sum(1 for r in results if r["success"])
    print(f"Requests: {successful}/50 successful ({100*successful/50:.0f}%)")
    print(f"Cleanup: {cleanup_success} success, {cleanup_failed} failed, {cleanup_verification_failed} verification failed")
    
    # KV verification
    kv_verified = sum(1 for r in results if r["kv_after_cleanup"] == 0)
    print(f"KV Cleanup Verified: {kv_verified}/50 requests show 0 tokens after cleanup")
    
    # Duration
    durations = [r["duration_s"] for r in results if r["success"]]
    if durations:
        print(f"Avg Duration: {sum(durations)/len(durations):.1f}s")
        print(f"Max Duration: {max(durations):.1f}s")
    
    # Final health
    final = get_health()
    print(f"\nFinal State: {final['state']}")
    print(f"Runtime Operational: {final.get('runtimeOperational')}")
    print(f"Runtime Degraded: {final.get('runtimeDegraded')}")
    print(f"Generated Tokens: {final.get('generatedTokensSinceReset')}")
    
    cleanup = final.get("inference", {}).get("cleanup", {})
    print(f"\nCleanup Telemetry:")
    print(f"  Total: {cleanup.get('totalCleanups', 0)}")
    print(f"  Success Rate: {cleanup.get('successRate', 1.0):.3f}")
    print(f"  Avg Duration: {cleanup.get('averageDurationMs', 0):.1f}ms")
    
    # Save
    with open("quick-soak-results.json", "w") as f:
        json.dump({"results": results, "final_health": final}, f, indent=2)
    print(f"\nResults saved to: quick-soak-results.json")

if __name__ == "__main__":
    main()
