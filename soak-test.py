#!/usr/bin/env python3
"""
200-Request Soak Test with Cleanup Stress
==========================================
Tests the production KV lifecycle management with:
- Rapid short requests
- Repeated long requests
- Cancel + cleanup
- Timeout + cleanup
- Cleanup under memory pressure

Tracks cleanup as first-class telemetry.
"""

import requests
import json
import time
import threading
import random
from datetime import datetime
from typing import Dict, List, Optional

BASE_URL = "http://127.0.0.1:5000"

class SoakTest:
    def __init__(self):
        self.results: List[Dict] = []
        self.cleanup_metrics: List[Dict] = []
        self.health_snapshots: List[Dict] = []
        self.start_time = datetime.utcnow()
        
    def get_health(self) -> Dict:
        """Get current health status."""
        resp = requests.get(f"{BASE_URL}/api/health", timeout=10)
        return resp.json()
    
    def send_request(self, prompt: str, max_tokens: int = 100, timeout: int = 30) -> Dict:
        """Send a chat completion request."""
        try:
            resp = requests.post(
                f"{BASE_URL}/v1/chat/completions",
                json={
                    "messages": [{"role": "user", "content": prompt}],
                    "max_tokens": max_tokens
                },
                timeout=timeout
            )
            return resp.json()
        except requests.exceptions.Timeout:
            return {"error": "timeout"}
        except Exception as e:
            return {"error": str(e)}
    
    def check_cleanup_telemetry(self) -> Dict:
        """Get cleanup telemetry from the experiment endpoint."""
        try:
            resp = requests.get(f"{BASE_URL}/api/experiment/kv-status", timeout=5)
            return resp.json()
        except:
            return {}
    
    def run_single_request(self, request_id: int, prompt: str, max_tokens: int = 100) -> Dict:
        """Run a single request and collect metrics."""
        start = time.time()
        result = self.send_request(prompt, max_tokens)
        duration = time.time() - start
        
        # Extract KV and cleanup info
        kv_info = result.get("_kv", {})
        
        record = {
            "request_id": request_id,
            "timestamp": datetime.utcnow().isoformat(),
            "duration_s": duration,
            "success": "error" not in result,
            "kv_tokens_before": kv_info.get("tokensBefore", -1),
            "kv_tokens_after": kv_info.get("tokensAfter", -1),
            "kv_tokens_after_cleanup": kv_info.get("tokensAfterCleanup", -1),
            "cleanup_result": kv_info.get("cleanupResult", "unknown"),
            "cleanup_duration_ms": kv_info.get("cleanupDurationMs", 0),
            "error": result.get("error")
        }
        
        self.results.append(record)
        return record
    
    def test_rapid_short_requests(self, count: int = 50):
        """Test rapid short requests (cleanup stress)."""
        print(f"\n{'='*60}")
        print(f"PHASE 1: Rapid Short Requests ({count} requests)")
        print(f"{'='*60}")
        
        for i in range(count):
            result = self.run_single_request(
                i + 1,
                f"Say hello in 5 words. Request #{i+1}",
                max_tokens=20
            )
            
            if i % 10 == 0:
                print(f"  Request {i+1}/{count}: "
                      f"{'SUCCESS' if result['success'] else 'FAILED'} "
                      f"({result['duration_s']:.2f}s) "
                      f"cleanup={result['cleanup_result']}")
            
            # No delay - rapid fire
    
    def test_repeated_long_requests(self, count: int = 30):
        """Test repeated long requests (memory pressure)."""
        print(f"\n{'='*60}")
        print(f"PHASE 2: Repeated Long Requests ({count} requests)")
        print(f"{'='*60}")
        
        for i in range(count):
            result = self.run_single_request(
                50 + i + 1,
                f"Write a detailed 200-word essay about the importance of memory management in modern computing systems. Request #{i+1}",
                max_tokens=300
            )
            
            if i % 5 == 0:
                print(f"  Request {50+i+1}/{50+count}: "
                      f"{'SUCCESS' if result['success'] else 'FAILED'} "
                      f"({result['duration_s']:.2f}s) "
                      f"cleanup={result['cleanup_result']}")
            
            time.sleep(0.5)  # Small delay for long requests
    
    def test_cancel_and_cleanup(self, count: int = 20):
        """Test cancel + cleanup scenarios."""
        print(f"\n{'='*60}")
        print(f"PHASE 3: Cancel + Cleanup ({count} requests)")
        print(f"{'='*60}")
        
        for i in range(count):
            # Start request in background
            start = time.time()
            try:
                resp = requests.post(
                    f"{BASE_URL}/v1/chat/completions",
                    json={
                        "messages": [{"role": "user", "content": f"Write a very long story. Request #{i+1}"}],
                        "max_tokens": 500
                    },
                    timeout=2  # Short timeout to trigger cancel
                )
                result = resp.json()
            except requests.exceptions.Timeout:
                result = {"error": "timeout (expected)"}
            except Exception as e:
                result = {"error": str(e)}
            
            duration = time.time() - start
            
            record = {
                "request_id": 80 + i + 1,
                "timestamp": datetime.utcnow().isoformat(),
                "duration_s": duration,
                "success": "error" not in result,
                "kv_tokens_before": -1,
                "kv_tokens_after": -1,
                "kv_tokens_after_cleanup": -1,
                "cleanup_result": "cancelled",
                "cleanup_duration_ms": 0,
                "error": result.get("error")
            }
            self.results.append(record)
            
            if i % 5 == 0:
                print(f"  Request {80+i+1}/{80+count}: "
                      f"{'SUCCESS' if record['success'] else 'CANCELLED/TIMEOUT'} "
                      f"({duration:.2f}s)")
            
            time.sleep(0.1)
    
    def test_memory_pressure(self, count: int = 50):
        """Test cleanup under memory pressure (many concurrent-ish requests)."""
        print(f"\n{'='*60}")
        print(f"PHASE 4: Memory Pressure ({count} requests)")
        print(f"{'='*60}")
        
        for i in range(count):
            # Vary prompt length to stress memory
            prompt_length = random.randint(50, 500)
            prompt = f"Generate exactly {prompt_length} words about artificial intelligence. Request #{i+1}"
            
            result = self.run_single_request(
                100 + i + 1,
                prompt,
                max_tokens=min(prompt_length * 2, 400)
            )
            
            if i % 10 == 0:
                print(f"  Request {100+i+1}/{100+count}: "
                      f"{'SUCCESS' if result['success'] else 'FAILED'} "
                      f"({result['duration_s']:.2f}s) "
                      f"cleanup={result['cleanup_result']}")
            
            time.sleep(0.2)
    
    def test_mixed_workload(self, count: int = 50):
        """Test mixed workload (random request types)."""
        print(f"\n{'='*60}")
        print(f"PHASE 5: Mixed Workload ({count} requests)")
        print(f"{'='*60}")
        
        prompts = [
            ("What is 2+2?", 10),
            ("Explain quantum computing in detail.", 200),
            ("Hello", 5),
            ("Write a poem about memory.", 100),
            ("List 5 facts about CPUs.", 50),
        ]
        
        for i in range(count):
            prompt, max_tokens = random.choice(prompts)
            result = self.run_single_request(
                150 + i + 1,
                prompt,
                max_tokens=max_tokens
            )
            
            if i % 10 == 0:
                print(f"  Request {150+i+1}/{150+count}: "
                      f"{'SUCCESS' if result['success'] else 'FAILED'} "
                      f"({result['duration_s']:.2f}s) "
                      f"cleanup={result['cleanup_result']}")
            
            time.sleep(random.uniform(0.1, 0.5))
    
    def collect_health_snapshot(self):
        """Collect health snapshot with cleanup telemetry."""
        health = self.get_health()
        cleanup = self.check_cleanup_telemetry()
        
        snapshot = {
            "timestamp": datetime.utcnow().isoformat(),
            "state": health.get("state"),
            "runtimeOperational": health.get("runtimeOperational"),
            "runtimeDegraded": health.get("runtimeDegraded"),
            "recentSuccessRate": health.get("recentSuccessRate"),
            "generatedTokensSinceReset": health.get("generatedTokensSinceReset"),
            "cleanup": cleanup.get("cleanup", {})
        }
        self.health_snapshots.append(snapshot)
        return snapshot
    
    def analyze_results(self):
        """Analyze and print results."""
        print(f"\n{'='*60}")
        print("SOAK TEST RESULTS")
        print(f"{'='*60}")
        
        total = len(self.results)
        successful = sum(1 for r in self.results if r["success"])
        failed = total - successful
        
        # Cleanup analysis
        cleanup_success = sum(1 for r in self.results if r["cleanup_result"] == "Success")
        cleanup_failed = sum(1 for r in self.results if r["cleanup_result"] == "Failed")
        cleanup_verification_failed = sum(1 for r in self.results if r["cleanup_result"] == "VerificationFailed")
        cleanup_skipped = sum(1 for r in self.results if r["cleanup_result"] == "Skipped")
        
        # Duration analysis
        durations = [r["duration_s"] for r in self.results if r["success"]]
        avg_duration = sum(durations) / len(durations) if durations else 0
        max_duration = max(durations) if durations else 0
        min_duration = min(durations) if durations else 0
        
        # Cleanup duration analysis
        cleanup_durations = [r["cleanup_duration_ms"] for r in self.results if r["cleanup_duration_ms"] > 0]
        avg_cleanup = sum(cleanup_durations) / len(cleanup_durations) if cleanup_durations else 0
        max_cleanup = max(cleanup_durations) if cleanup_durations else 0
        
        # KV analysis
        kv_before_values = [r["kv_tokens_before"] for r in self.results if r["kv_tokens_before"] > 0]
        kv_after_cleanup_values = [r["kv_tokens_after_cleanup"] for r in self.results if r["kv_tokens_after_cleanup"] >= 0]
        
        print(f"\nRequest Summary:")
        print(f"  Total requests: {total}")
        print(f"  Successful: {successful} ({100*successful/total:.1f}%)")
        print(f"  Failed: {failed} ({100*failed/total:.1f}%)")
        
        print(f"\nCleanup Summary:")
        print(f"  Success: {cleanup_success}")
        print(f"  Failed: {cleanup_failed}")
        print(f"  Verification Failed: {cleanup_verification_failed}")
        print(f"  Skipped: {cleanup_skipped}")
        print(f"  Cleanup Success Rate: {100*cleanup_success/(cleanup_success+cleanup_failed+cleanup_verification_failed):.1f}%")
        
        print(f"\nDuration Analysis:")
        print(f"  Average request duration: {avg_duration:.2f}s")
        print(f"  Max request duration: {max_duration:.2f}s")
        print(f"  Min request duration: {min_duration:.2f}s")
        print(f"  Average cleanup duration: {avg_cleanup:.2f}ms")
        print(f"  Max cleanup duration: {max_cleanup:.2f}ms")
        
        if kv_before_values:
            print(f"\nKV Cache Analysis:")
            print(f"  Average KV tokens before inference: {sum(kv_before_values)/len(kv_before_values):.0f}")
            print(f"  Max KV tokens before inference: {max(kv_before_values)}")
        
        if kv_after_cleanup_values:
            non_zero_after = sum(1 for v in kv_after_cleanup_values if v > 0)
            print(f"  KV tokens after cleanup (non-zero): {non_zero_after}/{len(kv_after_cleanup_values)}")
            if non_zero_after == 0:
                print(f"  ✓ KV cleanup VERIFIED: all requests show 0 tokens after cleanup")
            else:
                print(f"  ✗ KV cleanup ISSUE: {non_zero_after} requests show non-zero tokens after cleanup")
        
        # Final health snapshot
        final_health = self.collect_health_snapshot()
        print(f"\nFinal Health State:")
        print(f"  State: {final_health['state']}")
        print(f"  Runtime Operational: {final_health['runtimeOperational']}")
        print(f"  Runtime Degraded: {final_health['runtimeDegraded']}")
        print(f"  Recent Success Rate: {final_health['recentSuccessRate']}")
        print(f"  Generated Tokens Since Reset: {final_health['generatedTokensSinceReset']}")
        
        if final_health.get('cleanup'):
            c = final_health['cleanup']
            print(f"\nFinal Cleanup Telemetry:")
            print(f"  Total Cleanups: {c.get('totalCleanups', 0)}")
            print(f"  Successful: {c.get('successfulCleanups', 0)}")
            print(f"  Failed: {c.get('failedCleanups', 0)}")
            print(f"  Verification Failures: {c.get('verificationFailures', 0)}")
            print(f"  Success Rate: {c.get('successRate', 1.0):.3f}")
        
        # Save results
        report = {
            "test_start": self.start_time.isoformat(),
            "test_end": datetime.utcnow().isoformat(),
            "summary": {
                "total_requests": total,
                "successful": successful,
                "failed": failed,
                "cleanup_success": cleanup_success,
                "cleanup_failed": cleanup_failed,
                "cleanup_verification_failed": cleanup_verification_failed,
                "avg_duration_s": avg_duration,
                "avg_cleanup_ms": avg_cleanup
            },
            "results": self.results,
            "health_snapshots": self.health_snapshots
        }
        
        with open("soak-test-results.json", "w") as f:
            json.dump(report, f, indent=2)
        print(f"\nDetailed results saved to: soak-test-results.json")
    
    def run(self):
        """Run the complete soak test."""
        print(f"Engram 200-Request Soak Test with Cleanup Stress")
        print(f"Started: {self.start_time.isoformat()}")
        print(f"Target: {BASE_URL}")
        
        # Initial health check
        health = self.get_health()
        print(f"\nInitial State: {health['state']}")
        if health['state'] != 'Ready':
            print(f"ERROR: API not ready (state={health['state']})")
            return
        
        # Run all phases
        self.test_rapid_short_requests(50)
        self.test_repeated_long_requests(30)
        self.test_cancel_and_cleanup(20)
        self.test_memory_pressure(50)
        self.test_mixed_workload(50)
        
        # Analyze results
        self.analyze_results()


if __name__ == "__main__":
    test = SoakTest()
    test.run()
