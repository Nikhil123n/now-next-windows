# NOW/NEXT — Deferred and Removed Feature Register

**Document purpose:** This file preserves ideas that are deliberately excluded from the Windows rebuild so they are not accidentally reintroduced by Codex or forgotten entirely. “Deferred” means reconsider only after a stated evidence gate. “Removed” means outside the present product direction unless the product strategy changes explicitly.

The authoritative current build scope is in `FEATURES_FORWARD.md`.

---

# Part I — Deferred until the functional prototype proves useful

## Premium visual design

Deferred:

- Apple-like finish and motion quality.
- Complete monochrome design system.
- Multiple dark, light, warm, high-contrast, and evening themes.
- Final iconography.
- Advanced animations and transitions.
- Sophisticated sound design.
- Decorative resting screens.
- Store-quality onboarding.
- Pixel-level responsive polish across many displays.

Reason: visual usability and accessibility are required now, but decorative polish must not delay the complete execution loop.

Reconsideration gate: the P0 workflow has been used successfully for 10–14 working days and the core interaction model is stable.

## AI-assisted planning

Deferred:

- “Estimate with AI.”
- AI-generated focus labels.
- AI clarifying questions.
- AI task decomposition.
- Likely effort ranges.
- Safe calendar allocation.
- Recommended first focus block.
- Confidence and explanation cards.
- AI-generated schedule alternatives.
- Hosted model integration.

Reason: the first version needs reliable personal usage data before it can evaluate whether AI improves duration estimates or merely adds convincing guesses.

Reconsideration gate: at least 30–50 genuinely completed tasks contain planned duration, actual active duration, extensions, completion result, and any scope-change reason.

Permanent boundary: AI may recommend and explain, but it may not silently control the timer, move Fixed commitments, or appear during active focus.

## Personal adaptation and experiments

Deferred:

- Personal Time Model.
- Focus Fingerprint.
- Adaptive focus-block length.
- Adaptive break duration.
- Adaptive Landing duration.
- Time-of-day productivity modeling.
- Personal experiments comparing timer patterns.
- Natural-boundary inference.
- Automatic detection of flow.

Reason: these require sufficient reliable history and a stable product loop.

Reconsideration gate: enough local observations exist to compare a personalized recommendation against a simple historical median.

## Extended task metadata

Deferred:

- Rich task categories.
- Energy requirements.
- Minimum viable outcome.
- Multiple priorities.
- Dependency graphs.
- Recurrence engine.
- Linked files.
- Structured URL attachments.
- Voice notes.
- Screenshots.
- Last completed step and unresolved-question fields as separate properties.
- Multi-day project planning.
- Large backlog management.

Reason: the prototype is a day-execution tool, not a project-management system.

Reconsideration gate: repeated real usage demonstrates a specific missing field that cannot be handled by the existing title, definition of done, first action, next action, and note.

## Advanced schedule behavior

Deferred:

- Drift Twin side-by-side future comparison.
- Multiple generated repair alternatives.
- Optimization solvers such as OR-Tools.
- Multi-day automatic scheduling.
- Complex dependency-aware scheduling.
- Energy-aware scheduling.
- Travel-time automation.
- Protected and Optional block types beyond Fixed and Flexible.
- Schedule Shadow visualization.
- A separate Do Less Engine.
- Independent eye, posture, recovery, focus, and schedule clocks.

Reason: simple, deterministic single-day rules are easier to understand, test, and trust.

Reconsideration gate: real schedules repeatedly produce cases that the simple repair rules cannot handle safely.

## Additional interaction methods

Deferred:

- Surface Pen shortcuts.
- Voice commands.
- Voice interruption capture.
- Gesture-heavy navigation.
- Automatic opening of files.
- Automatic restoration of a work workspace.
- Hidden timer mode.
- Soft check-in timer mode.
- Large library of focus presets.
- Multiple named modes such as Starter, Deep, Flow, Sprint, and Recovery.

Reason: touch and keyboard are sufficient to validate the product.

Reconsideration gate: recurring friction is observed in the core interactions.

## Integrations

Deferred:

- Outlook and Microsoft 365 calendar import.
- Google Calendar.
- Microsoft To Do.
- Todoist.
- Notion.
- Website blocking.
- Windows Do Not Disturb integration.
- Companion application on another computer.
- Import/export integrations with other task managers.

Reason: manual Fixed blocks are enough to prove the workflow.

Reconsideration gate: manually entering Fixed commitments becomes a demonstrated obstacle to continued daily use.

## Distribution and product operations

Deferred:

- Microsoft Store listing.
- Production code signing.
- Automatic updates.
- Stable/beta/canary release channels.
- Remote crash reporting.
- Product telemetry.
- Subscription billing.
- Commercial licensing system.
- Public marketing site.
- Enterprise deployment controls.

Reason: the initial product is a personal functional prototype.

Reconsideration gate: the prototype is ready for another real user or external distribution.

## Advanced analytics

Deferred:

- Weekly dashboards.
- Start-latency analytics.
- Break-return latency.
- Overrun cascade rate.
- Repair acceptance rate.
- Focus-quality scoring.
- Comparative experiments.
- Automatic recommendations based on analytics.

Reason: local session history and planned-versus-actual time are enough initially.

Reconsideration gate: a specific product decision requires the additional measurement.

---

# Part II — Removed from the current product direction

## Cross-platform support

Removed:

- Linux.
- macOS.
- Mobile applications.
- Web application.
- Cross-platform UI framework.
- Cross-platform abstraction layers.

Reason: the rebuild is for one Surface running Windows 11. Windows-native simplicity is more valuable than hypothetical portability.

## Cloud and distributed architecture

Removed:

- Cloud backend.
- Go services.
- PostgreSQL.
- AWS.
- Azure backend services.
- SQS or another queue.
- REST API.
- OpenAPI.
- WebSockets.
- Server-Sent Events.
- Microservices.
- Kubernetes.
- Regional deployment.
- Multi-tenant architecture.
- CRDTs.
- Cloud synchronization.
- Cloud backup.
- Account recovery through a server.
- Remote observability infrastructure.

Reason: one user on one device has no distributed-systems problem.

## Identity and collaboration

Removed:

- User accounts.
- Sign-in.
- OAuth/OIDC.
- Multiple devices.
- Shared schedules.
- Team presence.
- Coworking rooms.
- Body doubling.
- Accountability partners.
- Enterprise administration.
- Team analytics.
- Public sharing.
- Social feeds.

Reason: none are necessary to test the core execution experience.

## Gamification and pressure mechanics

Removed:

- Productivity points.
- Public leaderboards.
- Competitive ranking.
- Punishing streaks.
- Confetti after routine actions.
- Hours-worked productivity scores.
- Mandatory pushups.
- Automatic five-minute hustle window.
- Shame-oriented overdue states.
- Forced automatic transition to the next task.

Reason: the product should promote deliberate action and recovery rather than anxiety, compulsion, or gaming.

## Focus-screen clutter

Removed:

- Progress bar during active focus.
- Backlog during active focus.
- Next task during active focus.
- Statistics during active focus.
- Motivational quotes.
- AI assistant bubble.
- Permanent controls.
- Battery information.
- Rich status widgets.
- Animated background.
- Nonessential notifications.

Reason: active focus is intentionally limited to the task focus label and timer.

## Media and entertainment

Removed:

- Music player.
- Soundscape library.
- Social media integration.
- Video content.
- Virtual pet.
- Decorative achievement system.

Reason: established applications already solve entertainment and ambience, while these features weaken the product identity.

---

# Part III — Features explicitly retained after reconsideration

Two earlier removal decisions have been reversed:

## Countdown mode — RETAINED

Countdown is a first-class timing option alongside count-up. It starts from the planned duration, reaches `00:00`, and then enters positive overtime if work continues.

## Blinking colon — RETAINED

The timer colon blinks at a calm one-second rhythm. It must not change numeral width or move the timer. Windows Reduced Motion may disable the blink for accessibility.

---

# Part IV — Rule for reconsidering a deferred or removed feature

A future Codex prompt must not reintroduce anything in this file merely because it is technically possible. Reconsideration requires a written decision that answers:

1. Which observed user problem does the feature solve?
2. What evidence shows the current simpler design is insufficient?
3. What is the smallest implementation that tests the hypothesis?
4. Which existing product invariant or complexity budget does it affect?
5. How will success or failure be measured?
6. What can be removed if the feature fails to help?

The decision must update both this file and `FEATURES_FORWARD.md` before implementation begins.
