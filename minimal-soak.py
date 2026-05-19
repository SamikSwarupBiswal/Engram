#!/usr/bin/env python3
"""Minimal 10-request soak to verify cleanup lifecycle."""

import requests
import json
import time
from datetime import datetime

BASE_URL = "http://127.0.0.1:5000"

def main():
    print("Minimal 10-Request Soak Test")
    
    health = requests.get(f"{BASE_URL}/api/health", timeout=10).json()
    print(f"State: {health['state']}")
    if health['state'] != 'Ready':
        return
    
    results = []
    for i in range(10):
        start = time.time()
        try:
            resp = requests.post(
                f"{BASE_URL}/v1/chat/completions",
                json={"messages": [{"role": "user", "content": f"Say hello #{i+1}"}], "max_tokens": 20},
                timeout=60
            )
            result = resp.json()
        except Exception as e:
            result = {"error": str(e)}
        
        duration = time.time() - start
        kv = result.get("_kv", {})
        
        print(f"  {i+1}/10: {'OK' if 'error' not in result else 'FAIL'} "
              f"({duration:.1f}s) "
              f"cleanup={kv.get('cleanupResult', '?')} "
              f"kv={kv.get('tokensBefore', '?')}→{kv.get('tokensAfterCleanup', '?')}")
        
        results.append({"id": i+1, "duration": duration, "cleanup": kv.get("cleanupResult"), "kv_before": kv.get("tokensBefore"), "kv_after": kv.get("tokensAfterCleanup")})
    
    # Final health
    final = requests.get(f"{BASE_URL}/api/health", timeout=10).json()
    cleanup = final.get("inference", {}).get("cleanup", {})
    print(f"\nFinal: {final['state']}")
    print(f"Cleanup: {cleanup.get('totalCleanups', 0)} total, {cleanup.get('successRate', 1.0):.3f} rate")
    print(f"Runtime Operational: {final.get('runtimeOperational')}")
    print(f"Runtime Degraded: {final.get('runtimeDegraded')}")

if __name__ == "__main__":
    main()
