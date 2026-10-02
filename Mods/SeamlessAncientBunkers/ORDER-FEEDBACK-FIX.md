# 0.4.4 cross-level order feedback

Production and probe Release builds: zero warnings/errors. RimWorld 1.6.4871 rev591, Harmony, all official DLC, production mod and isolated OrderProbe.

- 36 first-process assertions passed: actual cross-map hauling, drafted movement, pickup and repair; destination-map native FeedbackGoto creation; native selection-overlay line rendering with exact destination entrance and target endpoints; source-map exclusion; deselection; cancellation; handoff to vanilla rendering after arrival.
- 24 fresh-process assertions passed: saved move and destination line restored, followed by completed movement, pickup and repair with the same visual checks.
- 71 API/package checks passed against the staged Workshop package.
- Production DLL SHA256: BB7CE3F1C6D084D02F1292D923952F8CAF273BE2407A11359D509B90ADF16C10.

Evidence: Tests/Evidence/order-feedback-044.log and order-feedback-reload-044.log. Both end with ACCEPTANCE COMPLETE and have no test failures or exceptions. Tests observe actual native fleck creation and line-renderer calls, not screenshot/pixel appearance. No manual mouse/keyboard visual inspection was performed. Test fixtures and probes were isolated from personal saves. Initial instrumentation failed due to a Harmony parameter-name mismatch; it was fixed before the two successful runs. Existing fixture warnings remain in the logs.
