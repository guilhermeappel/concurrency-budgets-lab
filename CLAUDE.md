# Rules for AI coding agents in this repository

This is a small lab that backs a technical article. Its numbers will be published, so the rules
below protect the integrity of the experiment.

1. **The pre-registration is frozen once committed.** After the commit that sets the date in
   `PREREGISTRATION.md`, do not change hypotheses, thresholds, the matrix, or any parameter in
   `src/Lab.Stubs/StubState.cs` or the defaults in `src/Lab.Runner/LabOptions.cs`. The checks and
   thresholds in `src/Lab.Summary/SummaryReport.cs` are part of the pre-registration and are frozen
   the same way. If a change is
   unavoidable, stop and ask the author; if he approves, add a dated line to the log in
   `PREREGISTRATION.md` saying what changed, why, and which numbers become "computed after the change".
2. **Never edit, delete or regenerate files in `results/`** by hand. Results come only from
   `scripts/run-matrix.sh`. A failed run is retried by the script and logged; it is never removed.
3. **Never invent or estimate a number.** If something was not measured, say it was not measured.
4. **Synthetic data only.** No real company, client, vendor, product or system names anywhere in
   code, comments, commit messages or docs.
5. **Do not make the GitHub repository public, push to a public remote, or publish anything.**
   The author does that himself.
6. **Keep changes minimal.** Fix what breaks the build or the tests; do not refactor or "improve"
   the strategies (C1 to C4) or the stubs. Explain every change in the commit message.
7. **Ask before** creating a remote repository, pushing, installing global tools, or running
   anything that is not `dotnet`, `docker compose` or `git` (local).
