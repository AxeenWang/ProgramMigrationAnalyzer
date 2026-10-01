## CodeLab Governance Bootstrap

CodeLab-Governance: optional

This project supports CodeLab-managed and standalone operation.

When `../../AGENTS.md` contains the exact standalone line `CodeLab-Workspace-Root: v1` and the required anchors are available, use CodeLab-managed mode. Before project work, read `../../AGENTS.md` and `../../governance_eng.md`. Claude Code / Cowork also reads `../../CLAUDE.md`. Read `../../governance_cht.md` only for a Chinese request, bilingual maintenance, or an inconsistency check.

Otherwise, do not search elsewhere for substitutes. Use standalone mode and project-local rules. Without user authorization, do not read or write outside the project directory. Report the mode and reason in interactive work and non-interactive output.

Change the marker to `CodeLab-Governance: required` when failed managed-mode verification must block project work.

## Phase Git Workflow

- Create a Phase branch only when starting implementation of that Phase. Never pre-create branches for later Phases. Branch names listed in a plan are proposed names only.
- Before starting a Phase, verify that the preceding Phase PR is merged and use the latest remote default branch as the base. A merge or cleanup alone does not start the next Phase.
- Execute Tasks sequentially on the same Phase branch. Verify, commit and push each completed Task before starting the next Task.
- After all Tasks in the Phase are complete and verified, create one PR for that Phase. Continue review fixes on the same branch until the PR is merged.
- Direct commits and pushes to the default branch require explicit user authorization.
