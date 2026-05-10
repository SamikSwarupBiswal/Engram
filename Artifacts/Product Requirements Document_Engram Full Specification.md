# Product Requirements Document: Engram — Personal Semantic Operating Layer

Version: 1.1 (Full Infrastructure Spec)

Platform: Windows 11+ (Optimized for standard NPU/CPU hardware)

Status: Engineering Ready

## 1. Executive Summary

Engram is a persistent semantic operating layer for Windows designed to manage a user’s digital life by bridging intent and action. It operates on the principle of Longitudinal Continuity—remembering decisions, extracting commitments, and operating the OS to perform research or tasks on the user's behalf. It leverages a hybrid edge-cloud architecture to provide premium intelligence on consumer-grade hardware.

## 2. Data Ingestion & The "Nervous System"

Engram utilizes a multi-channel passive ingestion strategy to build context without user friction.

### 2.1 Raw Ingestion Channels

- Visual Perception: Captures screen frames at 1–2s intervals. These are processed locally via Windows Copilot Runtime (OCR) to detect active window titles and on-screen text.

- GWS CLI Integration: Deep integration with Google Workspace for read-access to Gmail, Calendar, and Drive metadata.

- Local File Watcher: Monitors C:\Users\User\Downloads, \Documents, and \Desktop for semantic artifacts (PDFs, receipts, tickets).

- Clipboard & App Stream: Monitors the active clipboard and application focus to detect "Active Intent."

### 2.2 The /raw Folder (Immutable History)

- Structure: \.engram\raw\[YYYY-MM-DD]\[Event_ID].json

- Function: Every ingested event (a screenshot OCR, an email, a file change) is stored as an immutable JSON/Markdown file.

- Requirement: Files are append-only. This serves as the "Source of Truth" for future re-processing if the extraction engine improves.

## 3. Dynamic Memory: The Karpathy LLM Wiki

Engram rejects traditional, high-cost vector databases in favor of a Local Markdown Wiki architecture.

### 3.1 The /wiki Folder (Metabolized Knowledge)

- Structure: A flat directory of .md files. Each file represents an entity (Person, Project, Goal, or Concept).

- The Indexing Logic: A master index.md acts as the map. The AI navigates memory by following standard Markdown [[Links]] rather than similarity scores.

- Efficiency: This reduces token usage by ![image7](<Product Requirements Document_Engram Full Specification_media/image7.png>) compared to RAG pipelines, as the model reads specialized indices rather than thousands of unrelated "chunks."

### 3.2 Metabolic Memory Logic

Memory in Engram is not static; it is "metabolized" through three processes:

- Merging: When a new flight receipt appears in /raw, a Reasoning Layer agent updates the existing Travel_Delhi_May.md in /wiki rather than creating a duplicate.

- Salience Decay: Every node has a ![image2](<Product Requirements Document_Engram Full Specification_media/image2.png>) (Salience) score. Salience decays over time ![image4](<Product Requirements Document_Engram Full Specification_media/image4.png>) following a power law:
![image3](<Product Requirements Document_Engram Full Specification_media/image3.png>)
where ![image6](<Product Requirements Document_Engram Full Specification_media/image6.png>) is the decay constant. Topics not interacted with for 30 days are compressed and moved to archives/.

- Conflict Detection (The Drift Engine): If a new decision in /raw contradicts a stored fact in /wiki, a high-priority Drift Alert is triggered.

## 4. Identity Hardening (The "Soul-Baking" Phase)

Upon installation, Engram undergoes an initial Discovery SOP to define the operational boundaries of the AI.

- The Discovery Skill: A specialized agent SOP that interviews the user for 15 minutes.

- Extraction Focus:

- Anti-Goals: What the user explicitly wants to avoid (e.g., "Don't suggest social media during work hours").

- Comfort Triggers: What makes the user feel "handled" or "safe."

- Recurring Anxieties: Forgotten deadlines, unread emails from specific senders.

- Output: The user_identity.md file, which acts as the System Prompt Constraint for all future AI interventions.

## 5. Logic Layers & Hierarchy

Engram selects compute tier based on task complexity to preserve local resources.

| Layer | Type | Technical Implementation | Goal |
| --- | --- | --- | --- |
| Perception Layer | Local (SLM) | Windows OCR + Phi-4. | "What is on the screen right now?" |
| Reasoning Layer | Cloud (VLM) | Gemini 3 Flash / Claude 4.5. | "Why does this matter to the user's life?" |
| Drift Engine | Hybrid | Local heuristics comparing Live Behavior to priorities.md. | Detect intention-action gaps. |
| Intervention Engine | Local | Logic to determine UI output (Notification vs. Card). | Deliver context comfortably. |

## 6. Automation & "Computer Use"

Engram physically operates the Windows environment to resolve "drudgery."

### 6.1 Agentic Research Workflow

- Intent Capture: User asks for research on a product or topic.

- Autonomous Navigation: Agent opens a browser via Playwright or the Computer Use API.

- Synthesis: It opens 5–10 tabs, reads content, and filters for high-signal data.

- Layout Snap: It creates a summary in the Wiki and snaps the source videos/articles side-by-side using Windows Snap Layouts.

## 7. Tiering & Side-by-Side Comparison

| Feature Domain | Free Tier (The Local Hub) | Pro Tier ($20-$30/mo) |
| --- | --- | --- |
| Intelligence Model | 100% Local SLM (Phi-4/Copilot Runtime) | Hybrid SLM + Cloud VLM (Claude/Gemini) |
| Primary Logic | Local Perception & Search | Deep Reasoning & Conflict Analysis |
| Sensing Capabilities | Local OCR & File Watching | GWS/365 Metadata Cloud Ingestion |
| Research Power | Search Links (Manual Research) | Multi-tab Synthesis & Structured Reports |
| Automation | None (Read-only observation) | Full "Computer Use" & Executive Action |
| Memory Sync | Single Device (Local) | Encrypted Cloud Sync & Multi-Device Continuity |
| Interventions | Local Drift Alerts & Notifications | Predictive Pattern Analysis & Resolutions |
| Cost Basis | $0 / mo (Runs on User NPU) | Managed Credit Pooling (Managed API) |

## 8. Solving the API Bottleneck

### 8.1 Pro Tier Strategy: Managed Credit Pooling

To avoid user-provided API keys (adoption barrier) and runaway costs:

- Model Routing: Uses Gemini 3 Flash for 90% of routine ingestion/summarization. Only triggers Claude 4.5 Sonnet for complex research or "Computer Use" tasks.

- Semantic Caching: Frequently researched topics (e.g., "Best Laptops 2026") are stored in a global "Clean Cache" to avoid running redundant agentic loops.

- Local Filtering: The Local SLM pre-processes all screenshots. It only sends "UI State Changes" to the cloud VLM, reducing token ingress by up to ![image5](<Product Requirements Document_Engram Full Specification_media/image5.png>).

### 8.2 Free Tier Strategy: Local-First

- Edge Processing: All OCR and extraction run on the user's NPU using the Windows Copilot Runtime.

- Incentive Loop: Free users get 3 "Energy Units" per week to experience Pro-level research or automation.

## 9. Interface & UX (The "Ghost")

- System Tray Widget: Sparse glass-morphism UI showing "Active Context" and "Energy Credits."

- Semantic Search (Alt + Space): Directly query the Wiki (e.g., "What was the decision on the lease?").

- Morning/Evening Brief: A lightweight card summarizing "Promises Made" and "Intentions Met."

## 10. Non-Functional Requirements

- Privacy: /wiki and /raw folders are encrypted at rest (AES-256).

- Performance: Background sensing must consume ![image1](<Product Requirements Document_Engram Full Specification_media/image1.png>) CPU/NPU.

- Reliability: Idempotent actions; agents resume from the last entry in log.md if interrupted.
